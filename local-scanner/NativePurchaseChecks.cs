using System.Text.Json;

namespace Metin2Search.Local;

internal sealed class PurchaseSendSchedule(int intervalMs=1000,int quietMs=500)
{
    private TimeSpan next;
    internal TimeSpan WaitAt(TimeSpan now)=>next>now?next-now:TimeSpan.Zero;
    internal void SentAt(TimeSpan now)=>next=now+TimeSpan.FromMilliseconds(intervalMs);
    internal void CompletedAt(TimeSpan now){var quietEnd=now+TimeSpan.FromMilliseconds(quietMs);if(quietEnd>next)next=quietEnd;}
}

public sealed class NativePurchaseTransport(ITradeBridge bridge,PurchaseCheckBudget budget,Action<PurchaseTarget>? beforeSend=null,int intervalMs=1000):IPurchaseCheckTransport
{
    private bool initialized;
    private readonly System.Diagnostics.Stopwatch pacing=System.Diagnostics.Stopwatch.StartNew();
    private readonly PurchaseSendSchedule schedule=new(intervalMs,intervalMs==500?250:500);
    internal async Task StopMonitor(CancellationToken token)
    {
        var result=await bridge.Command("purchasestop",token);
        if(!result.TryGetProperty("stopped",out var stopped)||stopped.ValueKind!=JsonValueKind.True)
            throw new IOException("Önceki satın alma yanıt izleyicisinin kapandığı doğrulanamadı.");
        initialized=false;
    }
    private async Task WaitForSendSlot(CancellationToken token)
    {
        while(schedule.WaitAt(pacing.Elapsed) is var wait&&wait>TimeSpan.Zero)
            await Task.Delay(wait,token);
    }
    public async Task<PurchasePreflight> Inspect(PurchaseTarget target,CancellationToken token)
    {
        if(!initialized){
            if(intervalMs==500){
                JsonElement timing;
                try{timing=await bridge.Command("purchasetiming 500",token);}
                catch(IOException error)when(error.Message.Contains("Fast purchase timing requires verified zero wallet",StringComparison.Ordinal)){
                    return new(false,0,0,false,false,"Hızlı FAKE kontrolü için 0 Yang / 0 Won doğrulanamadı.");
                }
                if(!timing.TryGetProperty("intervalMs",out var applied)||applied.GetInt32()!=500||timing.GetProperty("quietMs").GetInt32()!=250)
                    return new(false,0,0,false,false,"Native purchase pacing profile not acknowledged.");
            }
            await bridge.Command("purchasestart",token);
            initialized=true;
            await Task.Delay(TimeSpan.FromSeconds(3),token);
        }
        await WaitForSendSlot(token);
        var inspection=$"purchaseinspect {target.Slot} {target.Generation} {target.RequestId}";
        var result=await bridge.Command(inspection,token);
        var readiness=System.Diagnostics.Stopwatch.StartNew();
        while(result.GetProperty("wallet").GetProperty("walletVerified").GetBoolean()&&
              !result.GetProperty("sendingSupported").GetBoolean()&&readiness.Elapsed<TimeSpan.FromSeconds(2)){
            await Task.Delay(50,token);
            result=await bridge.Command(inspection,token);
        }
        var wallet=result.GetProperty("wallet");
        if(!wallet.TryGetProperty("walletVerified",out var verified)||!verified.GetBoolean())
            return new(false,0,0,false,false,"Native wallet identity could not be verified.");
        var same=result.GetProperty("nonce").GetString()==target.RequestId&&
            string.Equals(result.GetProperty("raw").GetString(),target.RawHex,StringComparison.OrdinalIgnoreCase)&&
            result.GetProperty("seller").GetString()==target.Seller&&result.GetProperty("shopVid").GetUInt32()==target.ShopVid&&
            result.GetProperty("yang").GetUInt32()==target.Yang&&result.GetProperty("won").GetUInt32()==target.Won&&
            PurchaseServerIds.Resolve(result.GetProperty("serverInfo").GetString()!)==target.Server&&
            wallet.GetProperty("serverInfo").GetString()==result.GetProperty("serverInfo").GetString();
        return new(wallet.GetProperty("walletVerified").GetBoolean(),wallet.GetProperty("yang").GetInt64(),
            wallet.GetProperty("won").GetInt64(),same,result.GetProperty("sendingSupported").GetBoolean(),"Fresh native row, server and wallet check");
    }
    public async Task<PurchaseResponse> SendOnce(PurchaseTarget target,CancellationToken token)
    {
        await WaitForSendSlot(token);
        beforeSend?.Invoke(target);
        schedule.SentAt(pacing.Elapsed);
        var sent=await bridge.Command($"purchasebegin {target.RequestId} {budget.RemainingYang}",token);
        if(!sent.GetProperty("sent").GetBoolean()||sent.GetProperty("nonce").GetString()!=target.RequestId||sent.GetProperty("sendCount").GetInt32()!=1)
            throw new IOException("Purchase send acknowledgement mismatch; do not retry.");
        var responseWait=System.Diagnostics.Stopwatch.StartNew();
        while(responseWait.Elapsed<TimeSpan.FromSeconds(8)){
            await Task.Delay(intervalMs==500?50:250,token);
            var result=await bridge.Command("purchasepoll",token);
            if(!result.TryGetProperty("nonce",out var nonce)||nonce.GetString()!=target.RequestId)throw new IOException("Purchase response nonce mismatch.");
            var code=result.GetProperty("code").GetString()!;
            if(code=="PENDING")continue;
            var verified=result.GetProperty("requestAndShopVerified").GetBoolean();
            var response=new PurchaseResponse(code,result.GetProperty("text").GetString()!,verified,
                result.TryGetProperty("confirmedSuccess",out var success)&&success.GetBoolean(),
                result.TryGetProperty("suspectedInventoryChange",out var inventory)&&inventory.GetBoolean(),
                result.TryGetProperty("actualYangSpent",out var debit)?debit.GetInt64():null);
            if(verified&&code is "PREMIUM_PREPARING" or "NOT_ENOUGH_MONEY" or "NOT_ENOUGH_MONEY_EX"){
                await bridge.Command($"purchasenext {target.RequestId}",token);
                schedule.CompletedAt(pacing.Elapsed);
            }
            return response;
        }
        throw new TimeoutException("Purchase response unresolved; all further sends suspended.");
    }
}

public sealed class GatedFakeLookups(ScannerPurchaseChecks checks,Func<bool> enabled):IFakeLookupSource
{
    public IReadOnlyList<FakeLookup> Pending()=>enabled()&&!checks.IsHalted?checks.Pending():[];
    public Task<bool> Verify(FakeLookup lookup,TradePage results,CancellationToken token)=>enabled()&&!checks.IsHalted?checks.Verify(lookup,results,token):Task.FromResult(true);
    public void NewRound(){if(enabled()&&!checks.IsHalted)checks.StartCycle();}
}

public sealed class ScannerPurchaseChecks:IDisposable
{
    private readonly LocalStore store;
    private PurchaseCheckBudget budget;
    private NativePurchaseTransport transport;
    private readonly ITradeBridge bridge;
    private PurchaseCheckCycle? cycle;
    private bool halted;
    private readonly string? stateFile;
    private readonly long authorizedYang;
    private int intervalMs;
    private readonly FileStream? stateLease;
    private readonly HashSet<string> quarantinedSellers=[];
    public bool ResumedZeroBudgetTimeout {get;private set;}
    public int QuarantinedCount=>quarantinedSellers.Count;
    public int CheckedCount {get;private set;}
    public string Status=>halted?"FAKE: durdu • "+HaltReason:$"FAKE: {intervalMs} ms • bu geçiş {CheckedCount} kontrol • {QuarantinedCount} belirsiz satıcı bekletiliyor";
    public string HaltReason {get;private set;}="Önceki gönderimin sonucu kesinleşmedi; aynı istek tekrarlanmaz.";
    public bool IsHalted=>halted;
    public ScannerPurchaseChecks(LocalStore store,ITradeBridge bridge,long yangBudget,string? stateFile=null,int intervalMs=1000)
    {
        if(yangBudget is <0 or >126588)throw new InvalidDataException("Authorized test budget exceeded.");
        if(intervalMs is not (500 or 1000))throw new InvalidDataException("Unvalidated purchase interval.");
        this.intervalMs=yangBudget==0?intervalMs:1000;
        this.store=store;this.bridge=bridge;this.stateFile=stateFile;authorizedYang=yangBudget;
        try{
        if(stateFile is not null){
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stateFile))!);
            stateLease=new FileStream(stateFile+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
        }
        var remaining=yangBudget;
        var server=PurchaseServerIds.Resolve(store.ServerName??"");
        foreach(var row in store.Query("SELECT DISTINCT json_extract(payload_json,'$.seller') FROM purchase_checks WHERE json_extract(payload_json,'$.server')=? AND json_extract(payload_json,'$.result')='Unknown' AND json_extract(payload_json,'$.sent')=1",server))
            if(row[0] is string seller&&seller.Length>0)quarantinedSellers.Add(seller);
        if(stateFile is not null&&File.Exists(stateFile)){
            using var state=JsonDocument.Parse(File.ReadAllText(stateFile));var saved=state.RootElement;
            if(saved.GetProperty("schemaVersion").GetInt32()!=1||saved.GetProperty("authorizedYang").GetInt64()!=yangBudget)throw new InvalidDataException("Persisted purchase budget differs; review before continuing.");
            remaining=saved.GetProperty("remainingYang").GetInt64();
            if(remaining<0||remaining>yangBudget)throw new InvalidDataException("Persisted purchase balance invalid.");
            var phase=saved.GetProperty("phase").GetString();
            halted=saved.GetProperty("halted").GetBoolean()||phase is not ("Preparing" or "NotEnoughMoney");
            var unsentHalt=saved.GetProperty("halted").GetBoolean()&&phase=="Unknown";
            if((!saved.GetProperty("halted").GetBoolean()&&phase=="VerifiedNoSend"||unsentHalt)&&saved.TryGetProperty("requestId",out var verifiedId)){
                var evidence=store.Query("SELECT payload_json FROM purchase_checks WHERE json_extract(payload_json,'$.requestId')=?",verifiedId.GetString()).FirstOrDefault();
                if(evidence is not null){
                    using var proof=JsonDocument.Parse((string)evidence[0]!);var p=proof.RootElement;
                    halted=p.GetProperty("sent").GetBoolean()||p.GetProperty("result").GetString()!="Unknown"||p.GetProperty("server").GetString()!=PurchaseServerIds.Resolve(store.ServerName??"");
                }
            }
            if(halted&&yangBudget==0&&remaining==0&&phase=="Unknown"&&saved.TryGetProperty("requestId",out var timeoutId)){
                var evidence=store.Query("SELECT payload_json FROM purchase_checks WHERE json_extract(payload_json,'$.requestId')=?",timeoutId.GetString()).FirstOrDefault();
                if(evidence is not null){
                    var proof=JsonSerializer.Deserialize<PurchaseCheckRecord>((string)evidence[0]!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                    if(proof is {Result:"Unknown",Sent:true,StopAll:true,Reason:"No unambiguous response before timeout"}&&proof.Server==server&&quarantinedSellers.Contains(proof.Seller)){
                        File.Copy(stateFile,stateFile+".zero-timeout-before-"+Guid.NewGuid().ToString("N"));
                        halted=false;ResumedZeroBudgetTimeout=true;
                        store.Event("PurchaseZeroBudgetResume","Eski belirsiz yanıt korunup satıcı bekletildi; yeni köprüde diğer pazarlar yalnız taze 0 Yang/0 Won doğrulamasıyla kontrol edilebilir.");
                    }
                }
            }
        }
        budget=new(remaining);
        transport=new(bridge,budget,target=>Persist(true,"Sending",target.RequestId),this.intervalMs);
        }catch{stateLease?.Dispose();throw;}
    }
    public void Dispose()=>stateLease?.Dispose();
    private void Persist(bool stop,string phase,string requestId)
    {
        if(stateFile is null)return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stateFile))!);
        var temporary=stateFile+".pending";
        var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{schemaVersion=1,authorizedYang,remainingYang=budget.RemainingYang,halted=stop,phase,requestId,updatedAt=DateTimeOffset.UtcNow}));
        using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
        File.Move(temporary,stateFile,true);
    }
    public TimeSpan CleanRecheck {get;init;}=TimeSpan.FromHours(6);
    public TimeSpan FlipWatch {get;init;}=TimeSpan.FromHours(24);
    public TimeSpan FlipRecheck {get;init;}=TimeSpan.FromMinutes(15);
    private readonly HashSet<(uint Vid,string Seller)> wanted=[],done=[];
    private readonly HashSet<string> doneSellers=[];
    public int WantedCount=>wanted.Count;
    public void StartCycle(){cycleNumber++;CheckedCount=0;wanted.Clear();done.Clear();doneSellers.Clear();if(!halted)MakeCycle();}
    private void MakeCycle(){cycle=new(transport,record=>{
        store.SavePurchaseCheck(record);
        if(record.Sent)CheckedCount++;
        if(record.StopAll)HaltReason=record.Reason;
        Persist(record.StopAll,record.Result,record.RequestId);
    },delay:(_,token)=>{token.ThrowIfCancellationRequested();return Task.CompletedTask;},budget:budget,refuseAffordable:true);}
    private async Task<bool> ContinueAfterTimeout(PurchaseCheckRecord? record,CancellationToken token)
    {
        if(authorizedYang!=0||budget.RemainingYang!=0||record is not {Result:"Unknown",Sent:true,StopAll:true,Reason:"No unambiguous response before timeout"})return false;
        halted=true;
        try{
            quarantinedSellers.Add(record.Seller);
            if(stateFile is not null&&File.Exists(stateFile))File.Copy(stateFile,stateFile+".zero-timeout-before-"+Guid.NewGuid().ToString("N"));
            await transport.StopMonitor(token);
            if(intervalMs==500){
                var timing=await bridge.Command("purchasetiming 1000",token);
                if(timing.GetProperty("intervalMs").GetInt32()!=1000)throw new IOException("Purchase fallback timing not acknowledged.");
                intervalMs=1000;
            }
            budget=new(0);
            transport=new(bridge,budget,target=>Persist(true,"Sending",target.RequestId),intervalMs);
            MakeCycle();
            halted=false;
            store.Event("PurchaseTimeoutQuarantined","Cevapsız satıcı yeniden denenmeyecek; eski monitor kapatıldı. Diğer pazarlar taze sıfır bakiye kontrolüyle devam ediyor.");
            Notify?.Invoke("FAKE: cevapsız pazar bekletildi • diğer pazarlarla devam ediliyor");
            return true;
        }catch(Exception error)when(error is IOException or TimeoutException){HaltReason="Yanıt izleyicisi yenilenemedi: "+error.Message;return false;}
    }
    private bool Unaffordable(TradeItem item)=>item.Won>0||item.Yang>budget.RemainingYang;
    public Action<string>? Notify {get;set;}
    public TimeSpan LookupRetry {get;init;}=TimeSpan.FromMinutes(10);
    private readonly Dictionary<(uint Vid,string Seller),(DateTimeOffset At,int Cycle)> returned=[];
    private readonly Dictionary<(uint Vid,string Seller),DateTimeOffset> settled=[],retryAfter=[];
    private int cycleNumber;
    public IReadOnlyList<FakeLookup> Pending()
    {
        if(halted||cycle is null)return [];
        var now=DateTimeOffset.UtcNow;
        foreach(var (shop,entry) in returned.ToArray()){
            if(entry.Cycle==cycleNumber)continue;
            if(!(settled.TryGetValue(shop,out var when)&&when>=entry.At))retryAfter[shop]=now+LookupRetry;
            returned.Remove(shop);
        }
        var lookups=new List<FakeLookup>();
        foreach(var shop in store.KnownFakeShops(60)){
            var key=(shop.Vid,shop.Seller);
            if(quarantinedSellers.Contains(shop.Seller)||done.Contains(key)||doneSellers.Contains(shop.Seller)||(retryAfter.TryGetValue(key,out var until)&&until>now))continue;
            var found=store.FakeShopListings(shop.Seller,shop.Vid,budget.RemainingYang,3);
            if(found.Count==0)continue;
            lookups.AddRange(found);returned[key]=(now,cycleNumber);
        }
        return lookups;
    }
    public async Task<bool> Verify(FakeLookup lookup,TradePage results,CancellationToken token)
    {
        var shop=(lookup.ShopVid,lookup.Seller);
        if(halted||cycle is null||quarantinedSellers.Contains(lookup.Seller)||done.Contains(shop))return true;
        var item=results.Items.FirstOrDefault(i=>i.Seller==lookup.Seller&&i.SellerVid==lookup.ShopVid&&i.Yang==lookup.Yang&&i.Won==lookup.Won&&i.Vnum==lookup.Vnum);
        if(item is null||!Unaffordable(item))return false;
        store.RefreshPresence(lookup.ListingKey,item.SellerVid);
        if(store.PurchaseTargetDirect(item,lookup.ListingKey,results.Generation) is not {} target)return false;
        wanted.Remove(shop);done.Add(shop);doneSellers.Add(shop.Seller);settled[shop]=DateTimeOffset.UtcNow;
        var record=await cycle.Check(target,token);
        if(record is not null)Notify?.Invoke($"FAKE yeniden doğrulama: {target.Seller} • {record.Result}");
        if(cycle.Stopped&&!await ContinueAfterTimeout(record,token)){halted=true;Notify?.Invoke(Status);}
        return true;
    }
    private bool NeedsCheck((uint Vid,string Seller) shop,bool hasChange,DateTimeOffset now)
    {
        if(store.SellerKnownFake(shop.Seller))return true;
        var last=store.LastConclusiveCheck(shop.Seller,shop.Vid);
        if(last is null)return true;
        if(last.Value.Result=="Preparing")return true;
        if(store.SellerPreparingSince(shop.Seller,now-FlipWatch))return now-last.Value.At>=FlipRecheck;
        return hasChange&&now-last.Value.At>=CleanRecheck;
    }
    public async Task Page(TradePage page,Action<string> progress,CancellationToken token)
    {
        if(halted||cycle is null)return;
        var items=page.Items.Where(i=>i.SellerVid>0&&!string.IsNullOrWhiteSpace(i.Seller)).ToList();
        var changed=page.Result?.ChangedSlots;var now=DateTimeOffset.UtcNow;
        foreach(var shop in items.GroupBy(i=>(i.SellerVid,i.Seller))){
            if(quarantinedSellers.Contains(shop.Key.Seller)||doneSellers.Contains(shop.Key.Seller)||done.Contains(shop.Key)||wanted.Contains(shop.Key))continue;
            if(page.FullPriceProbe||NeedsCheck(shop.Key,changed is not null&&shop.Any(i=>changed.Contains(i.Slot)),now))wanted.Add(shop.Key);
        }
        foreach(var shop in wanted.ToArray()){
            var safe=items.Where(i=>i.SellerVid==shop.Vid&&i.Seller==shop.Seller&&Unaffordable(i)).OrderByDescending(i=>i.Won).ThenByDescending(i=>i.Yang).FirstOrDefault();
            if(safe is null)continue;
            if(store.PurchaseTargetFor(page,safe.Slot) is not {} target)continue;
            wanted.Remove(shop);done.Add(shop);doneSellers.Add(shop.Seller);settled[shop]=DateTimeOffset.UtcNow;
            var record=await cycle.Check(target,token);
            if(record is not null)progress($"Pazar kontrolü: {target.Seller} • {record.Result}");
            if(cycle.Stopped&&!await ContinueAfterTimeout(record,token)){halted=true;progress(Status);break;}
        }
    }
}

public sealed partial class LocalStore
{
    public bool SellerPreparingSince(string seller,DateTimeOffset since)
    {
        lock(this)return Query("SELECT 1 FROM purchase_checks WHERE json_extract(payload_json,'$.seller')=? AND json_extract(payload_json,'$.result')='Preparing' AND json_extract(payload_json,'$.checkedAt')>=? LIMIT 1",seller,since.ToString("O")).Count>0;
    }
    public bool SellerKnownFake(string seller)
    {
        lock(this){
            var row=Query("SELECT json_extract(payload_json,'$.result') FROM purchase_checks WHERE json_extract(payload_json,'$.seller')=? AND json_extract(payload_json,'$.result') IN ('Preparing','NotEnoughMoney') ORDER BY id DESC LIMIT 1",seller).FirstOrDefault();
            return row is not null&&(string)row[0]! == "Preparing";
        }
    }
    public (string Result,DateTimeOffset At)? LastConclusiveCheck(string seller,uint shopVid)
    {
        lock(this){
            var row=Query("SELECT json_extract(payload_json,'$.result'),json_extract(payload_json,'$.checkedAt') FROM purchase_checks WHERE json_extract(payload_json,'$.seller')=? AND json_extract(payload_json,'$.shopVid')=? AND json_extract(payload_json,'$.result') IN ('Preparing','NotEnoughMoney') ORDER BY id DESC LIMIT 1",seller,(long)shopVid).FirstOrDefault();
            return row is null?null:((string)row[0]!,DateTimeOffset.Parse((string)row[1]!,System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    public List<(string Seller,uint Vid)> KnownFakeShops(int limit)
    {
        lock(this)return Query("SELECT json_extract(payload_json,'$.seller'),json_extract(payload_json,'$.shopVid') FROM purchase_checks WHERE id IN (SELECT MAX(id) FROM purchase_checks WHERE json_extract(payload_json,'$.result') IN ('Preparing','NotEnoughMoney') GROUP BY json_extract(payload_json,'$.seller')) AND json_extract(payload_json,'$.result')='Preparing' ORDER BY json_extract(payload_json,'$.checkedAt') LIMIT ?",limit)
            .Select(r=>((string)r[0]!,(uint)(long)r[1]!)).ToList();
    }
    public List<FakeLookup> FakeShopListings(string seller,uint shopVid,long remainingYang,int limit)
    {
        lock(this){
            var found=new List<(FakeLookup Lookup,string Seen)>();
            foreach(var row in Query("SELECT l.key,p.bytes,pr.vid,pr.last_seen FROM listing_lifecycle l JOIN compact_versions v ON v.id=l.version_id JOIN compact_payloads p ON p.hash=v.payload_hash JOIN compact_presence pr ON pr.key=l.key WHERE l.is_active=1 AND v.seller=? AND pr.vid>0",seller)){
                var key=(string)row[0]!;var vid=(uint)(long)row[2]!;var seen=(string?)row[3]??"";
                var scope=Query("SELECT scope_id FROM scope_memberships WHERE key=? AND is_active=1 ORDER BY scope_id LIMIT 1",key).FirstOrDefault()?[0] as string;
                var parts=scope?.Split(':');
                if(parts is not {Length:4}||!int.TryParse(parts[1],out var job)||!int.TryParse(parts[2],out var category)||!int.TryParse(parts[3],out var sub))continue;
                var item=DecodeCompact((byte[])row[1]!);
                if(item.Yang==0&&item.Won==0)continue;
                found.Add((new FakeLookup(seller,vid,key,new TradeCategory(job,category,sub,"FAKE yeniden doğrulama"),item.Yang,item.Won,item.Vnum,item.Count),seen));
            }
            var newest=found.GroupBy(f=>f.Lookup.ShopVid).ToDictionary(g=>g.Key,g=>g.Max(f=>f.Seen));
            return found.OrderByDescending(f=>newest[f.Lookup.ShopVid],StringComparer.Ordinal).ThenByDescending(f=>f.Lookup.Won>0||f.Lookup.Yang>remainingYang).ThenByDescending(f=>f.Lookup.Won).ThenByDescending(f=>f.Lookup.Yang).Select(f=>f.Lookup).Take(limit).ToList();
        }
    }
    public void RefreshPresence(string key,uint shopVid)
    {
        lock(this)Execute("INSERT INTO compact_presence VALUES(?,?,?,?) ON CONFLICT(key) DO UPDATE SET session=excluded.session,vid=excluded.vid,last_seen=excluded.last_seen",key,SessionId,(long)shopVid,DateTimeOffset.UtcNow.ToString("O"));
    }
    public PurchaseTarget? PurchaseTargetDirect(TradeItem item,string listingKey,long generation)
    {
        var server=PurchaseServerIds.Resolve(ServerName??"");
        if(server is null||item.SellerVid<=0||string.IsNullOrWhiteSpace(item.Seller))return null;
        return new PurchaseTarget(server,SessionId,item.SellerVid,item.Seller,listingKey,Guid.NewGuid().ToString("N"),item.Won,item.Yang,Convert.ToHexString(item.Raw).ToLowerInvariant(),generation,item.Slot);
    }
    public PurchaseTarget? PurchaseTargetFor(TradePage page,int slot)
    {
        lock(this){
            var server=PurchaseServerIds.Resolve(ServerName??"");
            if(server is null||purchasePageId!=page.Id||!purchasePageKeys.TryGetValue(slot,out var key))return null;
            var item=page.Items.FirstOrDefault(i=>i.Slot==slot);
            if(item is null||item.SellerVid<=0||string.IsNullOrWhiteSpace(item.Seller))return null;
            return new PurchaseTarget(server,SessionId,item.SellerVid,item.Seller,key,Guid.NewGuid().ToString("N"),item.Won,item.Yang,Convert.ToHexString(item.Raw).ToLowerInvariant(),page.Generation,item.Slot);
        }
    }
    public PurchaseTarget[] PurchaseTargets(TradePage page)
    {
        lock(this){
            var server=PurchaseServerIds.Resolve(ServerName??"");
            if(server is null||purchasePageId!=page.Id)return [];
            return page.Items.Where(item=>item.SellerVid>0&&!string.IsNullOrWhiteSpace(item.Seller)&&purchasePageKeys.ContainsKey(item.Slot))
                .Select(item=>new PurchaseTarget(server,SessionId,item.SellerVid,item.Seller,purchasePageKeys[item.Slot],
                    Guid.NewGuid().ToString("N"),item.Won,item.Yang,Convert.ToHexString(item.Raw).ToLowerInvariant(),page.Generation,item.Slot)).ToArray();
        }
    }
}
