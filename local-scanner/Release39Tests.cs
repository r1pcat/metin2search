using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class Release39Tests
{
    private sealed class DeniedProfileBridge:ITradeBridge
    {
        public int Calls;
        public Task<JsonElement> Command(string command,CancellationToken token){Calls++;throw new IOException("Fast purchase timing requires verified zero wallet");}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class HotScheduler:IScanScheduler
    {
        private string[] keys=[];
        public void SetPlan(IEnumerable<string> plan)=>keys=plan.ToArray();
        public ScanDecision Next()=>new(ScanKind.Hot,keys);
        public void CategoryRead(ScanKind kind,string key,uint total,int appeared,int changed,bool completed){}
        public void CategoryFailed(ScanKind kind,string key){}
    }
    private sealed class Bridge(bool fast=false):ITradeBridge
    {
        public int Searches;public List<string> Commands=[];
        public bool RejectFastOnce;
        private long generation;private int page=1;private bool pending;private string raw="";
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(command);object result;
            if(command=="inspect")result=new{generation,fastSearch=fast,sort=new{recentIndex=2,unitPriceIndex=1},plan=Enumerable.Range(0,5).SelectMany(job=>new[]{new{job,category=4,sub=0,label="Jewelry / first"},new{job,category=4,sub=1,label="Jewelry / second"}})};
            else if(command.StartsWith("setsort "))result=new{sort=new{selected=int.Parse(command[8..])}};
            else if(command=="searchfallback")result=new{fastSearch=false};
            else if(command.StartsWith("search ")||command.StartsWith("searchfast ")){
                if(command.StartsWith("searchfast ")&&RejectFastOnce){RejectFastOnce=false;throw new TradeQueryException("scripted fast rejection");}
                Searches++;page=1;pending=true;result=new{generation};
            }
            else if(command.StartsWith("page ")){page=int.Parse(command[5..]);pending=true;result=new{generation};}
            else if(command=="snapshot"){
                if(pending){generation++;pending=false;}
                raw=Convert.ToHexString(Enumerable.Range((page-1)*11,page==1?11:1).SelectMany(i=>{
                    var bytes=new byte[187];bytes[13]=1;bytes[17]=19;bytes[46]=(byte)'F';
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1),42);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(9),(uint)(1000+i));return bytes;
                }).ToArray());
                result=new{generation,page,total=12,count=raw.Length/374,raw};
            }else if(command=="details")result=new{generation,raw,details=Array.Empty<object>()};
            else throw new IOException("Unexpected scripted command");
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    internal static void Run(string root,List<string> results)
    {
        void Check(bool condition,string label){if(!condition)throw new Exception("FAILED: "+label);results.Add("PASS: "+label);}
        var path=Path.Combine(root,"secret-test","password.dpapi");const string fixture="offline-fixture";
        StoredStoragePassword.Save(path,fixture);
        Check(StoredStoragePassword.Read(path)==fixture&&!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(fixture),"Depo şifresi Windows CurrentUser korumasıyla açılır; dosyada düz metin tutulmaz");
        var damaged=File.ReadAllBytes(path);damaged[0]^=255;File.WriteAllBytes(path,damaged);var rejected=false;
        try{StoredStoragePassword.Read(path);}catch(CryptographicException){rejected=true;}
        Check(rejected,"Bozuk/başka kullanıcıya ait şifre kaydı sessizce başka şifreye dönüştürülmez");
        StoredStoragePassword.Save(path,"");Check(!File.Exists(path)&&StoredStoragePassword.Read(path)=="","Depo şifresini boşaltmak kayıtlı şifreyi kaldırır");
        Check(JsonData.Decode<UiSettings>(JsonData.Encode(new UiSettings{FullOnly=true})).FullOnly,"Yalnız tam tarama seçimi ayarlarda kalıcıdır");
        var fast=new PurchaseSendSchedule(500,250);fast.SentAt(TimeSpan.Zero);fast.CompletedAt(TimeSpan.FromMilliseconds(100));
        Check(fast.WaitAt(TimeSpan.FromMilliseconds(250))==TimeSpan.FromMilliseconds(250),"500ms FAKE profili yanıtı aralığa sayar; ek bir saniye bekletmez");
        fast.SentAt(TimeSpan.FromSeconds(1));fast.CompletedAt(TimeSpan.FromSeconds(2));
        Check(fast.WaitAt(TimeSpan.FromSeconds(2))==TimeSpan.FromMilliseconds(250),"Hızlı FAKE profilinde yanıt sonrası sessizlik korunur");
        var deniedProfile=new DeniedProfileBridge();var denied=new NativePurchaseTransport(deniedProfile,new(0),intervalMs:500);
        var deniedWallet=denied.Inspect(new("safir","fixture",1,"Fixture","key",Guid.NewGuid().ToString("N"),0,1,"",1,0),CancellationToken.None).GetAwaiter().GetResult();
        Check(!deniedWallet.WalletVerified&&deniedProfile.Calls==1,"Hızlı profilin sıfır bakiye reddi bağlantı kopması diye yutulmaz; hiçbir gönderim yapılmaz");
        using var store=new LocalStore(Path.Combine(root,"batch-full-only"));store.ConfigureServer("Safir, CH1");
        var bridge=new Bridge();var full=true;var modes=new List<string?>();var batchSizes=new List<int>();var prefetched=false;
        using var stop=new CancellationTokenSource();
        try{TradeScanner.Run(bridge,p=>{store.SaveTrade(p);return Task.CompletedTask;},_=>{},stop.Token,scheduler:new HotScheduler(),fullOnly:()=>full,auditBatch:async batch=>{
            var entry=batch[^1];batchSizes.Add(batch.Count);
            if(entry.Kind=="Complete"&&!prefetched)prefetched=bridge.Searches==2;
            await Task.Run(()=>store.TradeAuditBatch(batch));
            if(entry.Kind=="RunStart")modes.Add(entry.Mode);
            if(entry.Kind=="RunEnd"){if(modes.Count==1)full=false;else stop.Cancel();}
        }).GetAwaiter().GetResult();}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        Check(modes.SequenceEqual(new string?[]{null,"Hot"})&&bridge.Commands.Where(c=>c.StartsWith("setsort ")).SequenceEqual(new[]{"setsort 1","setsort 2"}),"Full-only planlayıcıyı tur sınırında değiştirir; kapatılınca En yeni Hot'a döner");
        Check(prefetched&&batchSizes.Contains(5),"Sonraki kategori isteği beş ortak kapsamın tamamlanmasıyla örtüşür; aliaslar tek batch'tir");
        Check(store.PendingCategoryBatches().Count==2&&store.TradeCoverage().Count==10&&store.Integrity=="ok","Ortak audit batch on kapsamı ve iki kategori paketini eksiksiz üretir");
        store.Execute("CREATE TRIGGER audit_batch_failure BEFORE INSERT ON trade_queries WHEN NEW.label='rollback' BEGIN SELECT RAISE(ABORT,'fixture rollback'); END");
        var id=Guid.NewGuid().ToString("N");var rolled=false;
        try{store.TradeAuditBatch(new[]{new TradeAudit("RunStart",id,"",null,0,2,""){PlannedQueries=2},new("Pending",id,id+":good",new(0,4,0,"good"),0,0,""),new("Pending",id,id+":bad",new(0,4,1,"rollback"),0,0,"")});}
        catch(IOException){rolled=true;}
        Check(rolled&&store.Query("SELECT 1 FROM lifecycle_runs WHERE run_id=?",id).Count==0&&store.Query("SELECT 1 FROM trade_queries WHERE run_id=?",id).Count==0,"Batch audit hatası RunStart dahil bütün kapsamları atomik geri alır");
        var fallback=new Bridge(fast:true){RejectFastOnce=true};var fallbackPages=new List<TradePage>();
        using var fallbackStop=new CancellationTokenSource();
        try{TradeScanner.Run(fallback,p=>{fallbackPages.Add(p);return Task.CompletedTask;},_=>{},fallbackStop.Token,a=>{if(a.Kind=="RunEnd")fallbackStop.Cancel();}).GetAwaiter().GetResult();}
        catch(OperationCanceledException)when(fallbackStop.IsCancellationRequested){}
        Check(fallback.Commands.Count(c=>c.StartsWith("searchfast "))==1&&fallback.Commands.Count(c=>c=="searchfallback")==1&&fallbackPages.Count==4,"Reddedilen hızlı arama normal aralığa bir kez döner; iki kategorinin bütün sayfaları yine okunur");
    }
}
