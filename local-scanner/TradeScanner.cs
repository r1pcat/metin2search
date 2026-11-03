using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

public sealed record TradeCategory(int Job, int Category, int Sub, string Label)
{
    public TradeCategory[]? CoveredCategories {get;init;}
    public int? QueryJob {get;init;}
    public string DisplayLabel=>CoveredCategories is {Length:>1}?Label+" · tek sorgu / "+CoveredCategories.Length+" kapsam":Label;
}
public sealed record TradePage(string Id, DateTimeOffset ObservedAt, TradeCategory Category, long Generation, uint Page, uint Total, byte[] Raw)
{
    internal List<TradeItem>? DecodedItems {get;init;}
    internal bool FullPriceProbe {get;init;}
    public string? RunId { get; init; }
    public string? QueryId { get; init; }
    public bool RestartsQuery { get; init; }
    public JsonElement? ClientDetails { get; init; }
    public string? DetailError { get; init; }
    public PageSaveResult? Result { get; init; }
    public List<TradeItem> Items => DecodedItems??Enumerable.Range(0, Raw.Length / 187).Select(i =>
    {
        JsonElement? detail = null;
        if (ClientDetails is { ValueKind: JsonValueKind.Array } array)
            foreach (var d in array.EnumerateArray()) if (d.GetProperty("slot").GetInt32() == i) { detail = d.Clone(); break; }
        return TradeDecoder.Decode(Raw.AsSpan(i * 187, 187).ToArray(), i) with { ClientDetails = detail, DetailError = DetailError };
    }).ToList();
    public void Validate()
    {
        if (Raw.Length > 11 * 187 || Raw.Length % 187 != 0 || Page > 1000000 || Total > 10000000)
            throw new InvalidDataException("Ticaret Camı sayfa kaydı geçersiz.");
    }
}
public sealed record TradeItem(int Slot, string Name, string Seller, uint Vnum, uint Count, uint Won, uint Yang,
    uint SellerVid, uint[] Sockets, Bonus[] Bonuses, ExtraBonus[] Extras, uint ChangeLookVnum, uint SetValue,
    uint SungmaScore, uint YinYangValue, byte YinYangType, byte[] RefineElementRaw, byte[] Raw)
{
    public string? ObservationId { get; init; }
    public string? ListingId { get; init; }
    public string? PhysicalItemId => null;
    public object IdentityEvidence => new { PhysicalVerification="unproven", PhysicalSource="No persistent physical UID established", ListingScope="server+legacy-feature-group; not physical identity", ObservationScope="dataset:stored-version; repeated identical sightings are deduplicated", Raw05=Raw.Length==187?(uint?)ItemDecoder.U32(Raw,5):null,Raw00=Raw.Length==187?(byte?)Raw[0]:null, RawSource="search-result-187/server", RawVerification="meaning and persistence unproven" };
    public bool? IsActive { get; init; }
    public string ActivityDisplay => IsActive is null?"Kapsam bilinmiyor":IsActive.Value?"Aktif":"Satış dışı/geçmiş";
    public string? RemovedAt { get; init; }
    public string LifecycleReason { get; init; } = "LegacyUnscoped";
    public string MarketStatus { get; init; } = "Doğrulanmadı";
    public string? LastSeen { get; init; }
    public string LastSeenDisplay=>DateTimeOffset.TryParse(LastSeen,out var time)?time.ToLocalTime().ToString("dd.MM HH:mm:ss"):"";
    public string? Server { get; init; }
    public bool IsFake { get; init; }
    public JsonElement? ClientDetails { get; init; }
    public string? DetailError { get; init; }
    public string[] TooltipLines => ClientDetails is { } d && d.TryGetProperty("lines", out var lines) ? lines.EnumerateArray().Select(l=>l.GetString()??"").ToArray() : [];
    public string StonesSummary => ClientDetails is { } d && d.TryGetProperty("stones", out var stones) ? string.Join(" · ",stones.EnumerateArray().Select(s=>s.TryGetProperty("name",out var name)?name.GetString():$"Yuva {s.GetProperty("slot")}: ham {s.GetProperty("raw")}")) : "Ham soketler mevcut";
    public string ElementSummary => ClientDetails is {} d&&d.TryGetProperty("element",out var e)&&e.TryGetProperty("text",out var text)?text.GetString()??"":string.Join(" · ", TooltipLines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l,"element|(?:ateş|buz|şimşek|rüzgar|toprak|karanlık).*gü",System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
    public string? AbsorptionRateSource => TooltipLines.FirstOrDefault(l=>System.Text.RegularExpressions.Regex.IsMatch(l,"emiş|emilim",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    public decimal? AbsorptionRate
    {
        get {
            if(AbsorptionRateSource is not {} line)return null;
            var match=System.Text.RegularExpressions.Regex.Match(line,@"(?:emiş|emilim)[^%\d]{0,40}%?\s*(\d+(?:[.,]\d+)?)\s*%?",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success&&decimal.TryParse(match.Groups[1].Value.Replace(',','.'),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var rate)?rate:null;
        }
    }
    public string AbsorptionSummary => string.Join(" · ", TooltipLines.Where(l=>System.Text.RegularExpressions.Regex.IsMatch(l,"emiş|emilim|emilen|emdirilen",System.Text.RegularExpressions.RegexOptions.IgnoreCase)))+
        (ClientDetails is {} d&&d.TryGetProperty("absorption",out var a)&&a.TryGetProperty("sourceName",out var name)?" • "+name.GetString():"");
    public string FeatureStatus => ClientDetails is {} d&&d.TryGetProperty("status",out var status)?status.GetString()=="client-rendered"?"İstemci açıklaması":"Kısmi ayrıntı":"Ham kayıt";
    public string BonusSummary => ClientDetails is { } d && d.TryGetProperty("bonuses",out var bonuses) && bonuses.GetArrayLength()>0 ? string.Join(" · ",bonuses.EnumerateArray().Select(b=>b.GetProperty("text").GetString())) : string.Join(" · ", Bonuses.Where(b => b.Type != 0).Select(b => $"{b.Type}: {b.Value:+0;-0;0}"));
}
public static class TradeDecoder
{
    public static TradeItem Decode(byte[] raw, int slot)
    {
        if (raw.Length != 187) throw new InvalidDataException("Ticaret Camı satırı 187 bayt olmalı.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string Text(int offset)
        {
            var bytes = raw.AsSpan(offset, 25); var end = bytes.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("Ürün/satıcı adı sonlandırılmamış; adres profili eşleşmeyebilir.");
            return Encoding.GetEncoding(1254).GetString(bytes[..end]);
        }
        return new(slot, Text(0x15), Text(0x2e), ItemDecoder.U32(raw, 0x11), ItemDecoder.U32(raw, 13),
            ItemDecoder.U32(raw, 0x8b), ItemDecoder.U32(raw, 9), ItemDecoder.U32(raw, 1),
            Enumerable.Range(0, 7).Select(i => ItemDecoder.U32(raw, 0x47 + i * 4)).ToArray(),
            Enumerable.Range(0, 7).Select(i => new Bonus(ItemDecoder.U16(raw, 0x63 + i * 5), ItemDecoder.I16(raw, 0x65 + i * 5), raw[0x67 + i * 5])).ToArray(),
            Enumerable.Range(0, 4).Select(i => new ExtraBonus(ItemDecoder.U16(raw, 0x9b + i * 6), ItemDecoder.U32(raw, 0x9d + i * 6))).ToArray(),
            ItemDecoder.U32(raw, 0x8f), ItemDecoder.U32(raw, 0xb3), ItemDecoder.U32(raw, 0xb7),
            ItemDecoder.U32(raw, 0x86), raw[0x8a], raw.AsSpan(0x93, 8).ToArray(), raw.ToArray());
    }
}

public interface ITradeBridge : IAsyncDisposable
{
    Task<JsonElement> Command(string command, CancellationToken token);
}
public sealed class TradeQueryException(string message) : IOException(message);
public sealed class PageSaveResult
{
    public int Changed {get;set;}
    public int Unchanged {get;set;}
    public int Appeared {get;set;}
    public HashSet<int> ChangedSlots {get;}=[];
}
public enum ScanKind { Full, Hot, Sweep }
public sealed record FakeLookup(string Seller,uint ShopVid,string ListingKey,TradeCategory Category,uint Yang,uint Won,uint Vnum,uint Count);
public interface IFakeLookupSource
{
    IReadOnlyList<FakeLookup> Pending();
    Task<bool> Verify(FakeLookup lookup,TradePage results,CancellationToken token);
    void NewRound(){}
}
public sealed record ScanDecision(ScanKind Kind,IReadOnlyList<string>? CategoryKeys=null);
public interface IScanScheduler
{
    void SetPlan(IEnumerable<string> categoryKeys);
    ScanDecision Next();
    void CategoryRead(ScanKind kind,string categoryKey,uint total,int appeared,int changed,bool completed);
    void CategoryFailed(ScanKind kind,string categoryKey);
}
public sealed record TradeAudit(string Kind,string RunId,string QueryId,TradeCategory? Category,int Pages,int Expected,string Message)
{
    public string? Mode {get;init;}
    public int? PlannedQueries {get;init;}
    public string? PlanQueryId {get;init;}
    public string? PlanLabel {get;init;}
    public TradeCategory? PlanCategory {get;init;}
}
public sealed class NativeTradeBridge : ITradeBridge
{
    private readonly Process host;
    private readonly Task<string> errors;
    private readonly ScanMetrics? metrics;
    public NativeTradeBridge(int pid,ScanMetrics? metrics=null)
    {
        this.metrics=metrics;
        var nativeDirectory=Environment.GetEnvironmentVariable("METIN2_NATIVE_BRIDGE_DIRECTORY")??AppContext.BaseDirectory;
        var executable = Path.Combine(nativeDirectory, "MarketBridge.Host.exe");
        var manifest=Path.Combine(nativeDirectory,"bridge-build.json");
        if(!File.Exists(executable)||!File.Exists(manifest))throw new FileNotFoundException("Sürümlü native köprü eksik; programı aynı derlemenin dosyalarıyla çalıştır.");
        using var identity=JsonDocument.Parse(File.ReadAllText(manifest));var build=identity.RootElement;
        var buildId=build.GetProperty("buildId").GetString()!;var dllName=build.GetProperty("dll").GetString()!;
        if(build.GetProperty("schemaVersion").GetInt32()!=1||build.GetProperty("abi").GetInt32()!=2||!System.Text.RegularExpressions.Regex.IsMatch(buildId,"^[a-f0-9]{16}$")||dllName!="MarketBridge."+buildId+".dll")throw new InvalidDataException("Native köprü sürüm bildirimi geçersiz.");
        var dll=Path.Combine(nativeDirectory,dllName);
        string Hash(string file)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        if(!File.Exists(dll)||Hash(dll)!=build.GetProperty("dllSha256").GetString()||Hash(executable)!=build.GetProperty("hostSha256").GetString())throw new InvalidDataException("Native köprü dosyaları aynı derlemeye ait değil.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(pid.ToString()); start.ArgumentList.Add(dll);
        host = Process.Start(start) ?? throw new IOException("Native köprü başlatılamadı.");
        errors = host.StandardError.ReadToEndAsync();
    }
    public async Task<JsonElement> Command(string command, CancellationToken token)
    {
        var timer=Stopwatch.StartNew();
        try{
        if (host.HasExited) throw new IOException($"Native köprü kapandı ({host.ExitCode}). {await errors}");
        await host.StandardInput.WriteLineAsync(command); await host.StandardInput.FlushAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(18));
        var line = await host.StandardOutput.ReadLineAsync(timeout.Token);
        if (line is null) throw new IOException($"Native köprü cevap vermedi. {await errors}");
        using var json = JsonDocument.Parse(line); var result = json.RootElement.Clone();
        if (result.TryGetProperty("error", out var error))
        {
            var message=error.GetString() +
            (result.TryGetProperty("windowsError", out var code) ? $" (Windows {code})" : "") +
            (result.TryGetProperty("errorType",out var type)?$" [{type.GetString()}]":"");
            if(result.TryGetProperty("dispatchStage",out var stage))message+=$" [hook={result.GetProperty("hookHits")},timer={result.GetProperty("timerHits")},stage={stage},thread={result.GetProperty("ownerThread")},hwnd={result.GetProperty("timerWindow")}]";
            if(result.TryGetProperty("fatal",out var fatal)&&!fatal.GetBoolean())throw new TradeQueryException(message);
            throw new IOException(message);
        }
        return result;
        }finally{metrics?.Record("bridge."+command.Split(' ')[0],timer.Elapsed);}
    }
    public async ValueTask DisposeAsync()
    {
        if (!host.HasExited)
        {
            host.StandardInput.Close();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await host.WaitForExitAsync(limit.Token); } catch (OperationCanceledException) { host.Kill(); }
        }
        host.Dispose();
    }
}

public static class TradeScanner
{
    private sealed class CategoryProgress { public int Saved; public int Expected; public uint FirstTotal; public int Appeared; public int Changed; }

    public static async Task Run(ITradeBridge bridge, Func<TradePage, Task> save, Action<string> progress, CancellationToken token, Action<TradeAudit>? audit = null, Func<MarketCheck,Task>? marketCheck = null,TimeSpan? responseTimeout=null,ScanMetrics? metrics=null,Func<TradePage,CancellationToken,Task>? purchasePage=null,IScanScheduler? scheduler=null,IFakeLookupSource? fakeLookups=null,ISessionGuard? session=null,Func<CancellationToken,Task>? checkpoint=null,Func<bool>? fullOnly=null,Func<IReadOnlyList<TradeAudit>,Task>? auditBatch=null)
    {
        var inspection = await bridge.Command("inspect", token);
        var activeKind=ScanKind.Hot;
        bool BundledCapture()=>inspection.TryGetProperty("captureDetails",out var capability)&&capability.ValueKind==JsonValueKind.True;
        var fastRequestsAllowed=true;
        bool FastRequests()=>fastRequestsAllowed&&inspection.TryGetProperty("fastSearch",out var capability)&&capability.ValueKind==JsonValueKind.True;
        async Task ApplySort()
        {
            var field=activeKind==ScanKind.Full?"unitPriceIndex":"recentIndex";
            var label=activeKind==ScanKind.Full?"En düşük bireysel fiyat":"En yeni";
            if(inspection.TryGetProperty("sort",out var sort)&&sort.ValueKind==JsonValueKind.Object
                &&sort.TryGetProperty(field,out var index)&&index.ValueKind==JsonValueKind.Number)
            {
                var reply=await bridge.Command($"setsort {index.GetInt32()}",token);
                if(!reply.TryGetProperty("sort",out var selectedSort)||!selectedSort.TryGetProperty("selected",out var selected)||selected.GetInt32()!=index.GetInt32())
                    throw new InvalidDataException("İstemci sıralamayı doğrulamadı; tarama başlamadı.");
                progress("Sıralama: "+label);
            }
            else throw new InvalidDataException(label+" sıralaması istemci tablosunda doğrulanamadı; tarama başlamadı.");
        }
        var plan = TradePlan.FromInspection(inspection);
        if (plan.Length == 0) throw new InvalidDataException("Kategori planı boş; tarama başlamadı.");
        scheduler?.SetPlan(plan.Select(ScanScheduler.Key));
        string? mode=null;var scopeCount=0;var plannedQueries=0;
        async Task Audit(TradeAudit data){
            data=data with{Mode=mode};
            var batch=data.Category is null?new[]{data with{Expected=scopeCount,PlannedQueries=plannedQueries}}:
                TradePlan.Covered(data.Category).Select(covered=>data with{Category=covered,QueryId=$"{data.RunId}:{covered.Job}:{covered.Category}:{covered.Sub}",PlanQueryId=data.QueryId,PlanLabel=data.Category.DisplayLabel,PlanCategory=data.Category}).ToArray();
            if(auditBatch is not null)await auditBatch(batch);else foreach(var entry in batch)audit?.Invoke(entry);
        }
        long generation = inspection.GetProperty("generation").GetInt64();
        Dictionary<(uint,string),bool> markets=[];

        async Task<bool> TryRecover(Exception error)
        {
            if(session is null||error is OperationCanceledException)return false;
            if(!await session.IsLostAsync(token))return false;
            progress("Oyun bağlantısı/Ticaret Camı kaybı algılandı • tarama bekliyor");
            await session.RecoverAsync(progress,token);
            for(var attempt=0;;attempt++)
            {
                try{inspection=await bridge.Command("inspect",token);break;}
                catch(IOException) when(attempt<8&&!token.IsCancellationRequested){progress("Oyun verisi yükleniyor; tekrar deneniyor");await Task.Delay(1000,token);}
            }
            generation=Math.Max(generation,inspection.GetProperty("generation").GetInt64());
            await ApplySort();
            progress("Ticaret Camı açıldı • tarama kaldığı yerden devam ediyor");
            return true;
        }

        async Task ScanCategory(TradeCategory category,string runId,string queryId,bool hot,CategoryProgress state,bool restart=false,Task<JsonElement>? primed=null)
        {
            Task? pendingSave=null;
            try {
                var request=await (primed??SearchReady(bridge,category,progress,token,FastRequests(),metrics));
                generation = Math.Max(generation, request.GetProperty("generation").GetInt64());
                var nextPage = 0u;
                do
                {
                    JsonElement snapshot;
                    var responseTimer=Stopwatch.StartNew();
                    var firstReplyLimit=FastRequests()&&nextPage==0?(responseTimeout is {} custom&&custom<TimeSpan.FromSeconds(3)?custom:TimeSpan.FromSeconds(3)):responseTimeout;
                    try {snapshot=await WaitResponse(bridge,generation,token,firstReplyLimit,BundledCapture());}
                    catch(TimeoutException){
                        if(FastRequests()){
                            fastRequestsAllowed=false;
                            await bridge.Command("searchfallback",token);
                            progress("Hızlı arama yanıtı gecikti • normal istemci aralığına dönüldü");
                            metrics?.Record("search.fast-fallback",TimeSpan.Zero);
                        }
                        var message=$"{category.Label} • {(nextPage==0?"arama":$"sayfa {nextPage}")} yanıtı yok; bir kez yeniden deneniyor";
                        progress(message);await Audit(new("Running",runId,queryId,category,state.Saved,state.Expected,message));
                        var retry=nextPage==0?await SearchReady(bridge,category,progress,token,FastRequests(),metrics):await bridge.Command($"page {nextPage}",token);
                        generation=Math.Max(generation,retry.GetProperty("generation").GetInt64());
                        snapshot=await WaitResponse(bridge,generation,token,responseTimeout,BundledCapture());
                    }
                    metrics?.Record(BundledCapture()?"capture.wait":"response.wait",responseTimer.Elapsed);
                    if(checkpoint is not null){if(pendingSave is not null){await pendingSave;pendingSave=null;}await checkpoint(token);}
                    var page = snapshot.GetProperty("page").GetUInt32();
                    if(nextPage==0&&page>1)
                    {
                        var first=await bridge.Command("page 1",token);
                        generation=snapshot.GetProperty("generation").GetInt64();nextPage=1;continue;
                    }
                    if (nextPage != 0 && page != nextPage) throw new TradeQueryException("Beklenen sayfa ile sunucu cevabı eşleşmedi; yanlış kategoriye kayıt yapılmadı.");
                    generation = snapshot.GetProperty("generation").GetInt64();
                    var raw = Convert.FromHexString(snapshot.GetProperty("raw").GetString()!);
                    if (raw.Length != snapshot.GetProperty("count").GetInt32() * 187) throw new InvalidDataException("Sonuç sayısı/ham veri uzunluğu eşleşmedi.");
                    JsonElement? details=null;string? detailError=null;
                    if(BundledCapture()){
                        if(snapshot.TryGetProperty("details",out var bundled))details=bundled.Clone();
                        else if(snapshot.TryGetProperty("detailError",out var error))detailError=error.GetString();
                        else throw new InvalidDataException("Birleşik sayfa cevabında ayrıntı veya hata kanıtı eksik.");
                    }else try {
                        var detail=await bridge.Command("details",token);
                        if(detail.GetProperty("generation").GetInt64()==generation&&detail.GetProperty("raw").GetString()==snapshot.GetProperty("raw").GetString())
                            details=detail.GetProperty("details").Clone();
                        else detailError="Ayrıntı cevabı ham sayfayla eşleşmedi; ham veri korundu.";
                    } catch(TradeQueryException error){detailError=error.Message;}
                    var capture = new TradePage(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, category, generation, page,
                        snapshot.GetProperty("total").GetUInt32(), raw) {RunId=runId,QueryId=queryId,ClientDetails=details,DetailError=detailError,Result=new PageSaveResult(),RestartsQuery=restart&&state.Saved==0,FullPriceProbe=activeKind==ScanKind.Full&&page is 1 or 2};
                    capture.Validate();var decodeTimer=Stopwatch.StartNew();capture=capture with{DecodedItems=capture.Items};metrics?.Record("decode.page",decodeTimer.Elapsed);
                    if(pendingSave is not null){var previous=pendingSave;pendingSave=null;await previous;}
                    var saving=save(capture);
                    if(hot)await saving;else pendingSave=saving;
                    if((hot||capture.FullPriceProbe)&&purchasePage is not null){
                        if(pendingSave is not null){await pendingSave;pendingSave=null;}
                        await purchasePage(capture,token);
                    }
                    foreach(var item in capture.Items)if(item.SellerVid>0&&!string.IsNullOrWhiteSpace(item.Seller))markets[(item.SellerVid,item.Seller)]=true;
                    progress($"{category.Label} • sayfa {page} • {raw.Length / 187} ürün yerel kaydedildi");
                    var pages = (capture.Total + 10) / 11;
                    if(state.Saved==0)state.FirstTotal=capture.Total;
                    state.Saved++;state.Expected=(int)pages;state.Appeared+=capture.Result?.Appeared??0;state.Changed+=capture.Result?.Changed??0;
                    await Audit(new("Running",runId,queryId,category,state.Saved,state.Expected,detailError??$"Sayfa {page}"));
                    if(page==0&&capture.Total>0)throw new TradeQueryException("Dolu sonucun sayfa indeksi 0; sayfa düzeni doğrulanamadı.");
                    if (pages == 0 || page >= pages) break;
                    if(hot&&capture.Result is {} saved)
                    {
                        var count=raw.Length/187;var firstKnown=-1;
                        for(var slot=0;slot<count;slot++)if(!saved.ChangedSlots.Contains(slot)){firstKnown=slot;break;}
                        if(firstKnown>=0)
                        {
                            if(!saved.ChangedSlots.Any(slot=>slot>firstKnown))break;
                            metrics?.Record("hot.order-violation",TimeSpan.Zero);
                        }
                    }
                    nextPage = checked(page + 1);
                    var response = await bridge.Command($"page {nextPage}", token);
                    generation = Math.Max(generation, response.GetProperty("generation").GetInt64());
                } while (true);
                if(pendingSave is not null){var last=pendingSave;pendingSave=null;await last;}
            } finally {
                if(pendingSave is not null){try{await pendingSave;}catch(Exception){}}
            }
        }

        async Task<(int completed,int failed)> Pass(TradeCategory[] categories,ScanKind kind)
        {
            activeKind=kind;await ApplySort();
            mode=kind==ScanKind.Full?null:kind.ToString();var hot=kind==ScanKind.Hot;
            plannedQueries=categories.Length;scopeCount=categories.Sum(c=>TradePlan.Covered(c).Count());
            var tag=kind switch{ScanKind.Hot=>"Hızlı geçiş",ScanKind.Sweep=>"Doğrulama taraması",_=>"Tam tarama"};
            var runId=Guid.NewGuid().ToString("N");var completed=0;var failed=0;
            markets=[];
            await Audit(new("RunStart",runId,"",null,0,categories.Length,inspection.GetRawText()));
            if(!hot)foreach (var category in categories) await Audit(new("Pending",runId,$"{runId}:{category.Job}:{category.Category}:{category.Sub}",category,0,0,"Bekliyor"));
            Task<JsonElement>? nextSearch=null;
            try{for(var categoryIndex=0;categoryIndex<categories.Length;categoryIndex++)
            {
                var category=categories[categoryIndex];
                var queryId=$"{runId}:{category.Job}:{category.Category}:{category.Sub}";
                token.ThrowIfCancellationRequested(); progress($"{tag} • {completed+failed+1}/{categories.Length} • {category.Label} • arama gönderiliyor");
                if(hot)await Audit(new("Pending",runId,queryId,category,0,0,"Bekliyor"));
                await Audit(new("Running",runId,queryId,category,0,0,"Arama"));
                for(var recoveries=0;;recoveries++)
                {
                    var state=new CategoryProgress();
                    try {
                        var primed=nextSearch;nextSearch=null;
                        await ScanCategory(category,runId,queryId,hot,state,recoveries>0,primed);
                        var completionTime=Stopwatch.StartNew();
                        if(categoryIndex+1<categories.Length&&!token.IsCancellationRequested)
                            nextSearch=SearchReady(bridge,categories[categoryIndex+1],progress,token,FastRequests(),metrics);
                        completed++;
                        if(hot)await Audit(new("HotDone",runId,queryId,category,state.Saved,state.Expected,"Hızlı geçiş tamamlandı"));
                        else await Audit(new("Complete",runId,queryId,category,state.Saved,state.Expected,"Tamamlandı"));
                        metrics?.Record("category.complete",completionTime.Elapsed);
                        scheduler?.CategoryRead(kind,ScanScheduler.Key(category),state.FirstTotal,state.Appeared,state.Changed,!hot);
                        break;
                    } catch(Exception error) when(error is TimeoutException or IOException) {
                        if(nextSearch is not null){try{await nextSearch;}catch(Exception){}nextSearch=null;}
                        if(recoveries<3&&await TryRecover(error)){await Audit(new("Running",runId,queryId,category,0,0,"Oturum yenilendi; kategori yeniden okunuyor"));continue;}
                        if(recoveries==0&&FastRequests()&&error is TradeQueryException){
                            fastRequestsAllowed=false;await bridge.Command("searchfallback",token);
                            metrics?.Record("search.fast-fallback",TimeSpan.Zero);
                            progress("Hızlı arama reddedildi • kategori normal aralıkla yeniden okunuyor");continue;
                        }
                        if(error is not (TimeoutException or TradeQueryException))throw;
                        failed++;scheduler?.CategoryFailed(kind,ScanScheduler.Key(category));await Audit(new("Failed",runId,queryId,category,state.Saved,state.Expected,error.Message));
                        progress($"{category.Label}: {error.Message} • sonraki kategoriye geçiliyor");
                        break;
                    }
                }
            }}finally{if(nextSearch is not null){try{await nextSearch;}catch(Exception){}}}
            if(marketCheck is not null && completed>0)
                foreach(var market in markets.Keys){token.ThrowIfCancellationRequested();progress($"Pazar doğrulanıyor: {market.Item2}");await marketCheck(await MarketVerification.Check(bridge,market.Item1,market.Item2,token));}
            await Audit(new("RunEnd",runId,"",null,completed,categories.Length,$"Tamamlanan {completed}, hatalı {failed}"));
            progress($"{tag} bitti: {completed}/{categories.Length} tamamlandı, {failed} hata");
            return (completed,failed);
        }

        async Task FakePass()
        {
            if(fakeLookups is null)return;
            var settled=new HashSet<(uint,string)>();var tried=new Dictionary<(uint,string),int>();
            var passTimer=Stopwatch.StartNew();
            foreach(var lookup in fakeLookups.Pending())
            {
                token.ThrowIfCancellationRequested();
                if(TradePlan.Excluded(lookup.Category))continue;
                if(checkpoint is not null)await checkpoint(token);
                if(passTimer.Elapsed>TimeSpan.FromSeconds(150)){progress("FAKE yeniden doğrulama: süre doldu, kalanlar sonraki geçişte");break;}
                var shop=(lookup.ShopVid,lookup.Seller);
                if(settled.Contains(shop)||tried.GetValueOrDefault(shop)>=3)continue;
                tried[shop]=tried.GetValueOrDefault(shop)+1;
                progress($"FAKE yeniden doğrulama • {lookup.Seller}");var timer=Stopwatch.StartNew();
                try{
                    var request=await SearchPriceReady(bridge,lookup,progress,token);
                    generation=Math.Max(generation,request.GetProperty("generation").GetInt64());
                    var nextPage=0u;
                    for(var read=0;read<4;read++)
                    {
                        var snapshot=await WaitResponse(bridge,generation,token,responseTimeout);
                        var page=snapshot.GetProperty("page").GetUInt32();
                        if(nextPage==0&&page>1){await bridge.Command("page 1",token);generation=snapshot.GetProperty("generation").GetInt64();nextPage=1;continue;}
                        generation=snapshot.GetProperty("generation").GetInt64();
                        var raw=Convert.FromHexString(snapshot.GetProperty("raw").GetString()!);
                        if(raw.Length!=snapshot.GetProperty("count").GetInt32()*187)throw new InvalidDataException("Sonuç sayısı/ham veri uzunluğu eşleşmedi.");
                        var results=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,lookup.Category,generation,page,snapshot.GetProperty("total").GetUInt32(),raw);
                        results.Validate();
                        if(await fakeLookups.Verify(lookup,results,token)){settled.Add(shop);break;}
                        var pages=(results.Total+10)/11;
                        if(pages==0||page>=pages||read>=2)break;
                        nextPage=checked(page+1);
                        var response=await bridge.Command($"page {nextPage}",token);
                        generation=Math.Max(generation,response.GetProperty("generation").GetInt64());
                    }
                }catch(Exception error) when(error is TimeoutException or TradeQueryException){progress($"FAKE yeniden doğrulama: {lookup.Seller}: {error.Message}");}
                metrics?.Record("fake.lookup",timer.Elapsed);
            }
        }

        var idleFake=Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if(checkpoint is not null)await checkpoint(token);
            var scheduled=scheduler?.Next()??new ScanDecision(ScanKind.Full);
            var decision=fullOnly?.Invoke()==true?new ScanDecision(ScanKind.Full):scheduled;
            var categories=plan;
            if(decision.Kind==ScanKind.Sweep){
                var keys=decision.CategoryKeys is null?null:new HashSet<string>(decision.CategoryKeys);
                categories=keys is null?plan:plan.Where(c=>keys.Contains(ScanScheduler.Key(c))).ToArray();
                if(categories.Length==0){await Task.Delay(1000,token);continue;}
            }
            else if(decision.Kind==ScanKind.Hot&&decision.CategoryKeys is not null){
                var byKey=plan.ToDictionary(ScanScheduler.Key);
                categories=decision.CategoryKeys.Where(byKey.ContainsKey).Select(k=>byKey[k]).ToArray();
                if(categories.Length==0){
                    if(fakeLookups is not null&&idleFake.Elapsed>=TimeSpan.FromMinutes(2)){
                        activeKind=ScanKind.Hot;await ApplySort();
                        idleFake.Restart();fakeLookups.NewRound();
                        try{await FakePass();}
                        catch(IOException error) when(!token.IsCancellationRequested){if(!await TryRecover(error))throw;}
                        idleFake.Restart();
                    }
                    await Task.Delay(1000,token);continue;
                }
            }
            idleFake.Restart();
            var (completed,_)=await Pass(categories,decision.Kind);
            if(completed==0)throw new IOException("Hiçbir kategori tamamlanamadı. Hata listesi yerel kaydedildi.");
            if(decision.Kind==ScanKind.Hot)
            {
                try{await FakePass();}
                catch(IOException error) when(!token.IsCancellationRequested){if(!await TryRecover(error))throw;}
            }
        }
    }
    private static async Task<JsonElement> SearchReady(ITradeBridge bridge,TradeCategory category,Action<string> progress,CancellationToken token,bool fast=false,ScanMetrics? metrics=null)
    {
        var readiness=Stopwatch.StartNew();
        while(true){
            var request=await bridge.Command($"{(fast?"searchfast":"search")} {category.QueryJob??category.Job} {category.Category} {category.Sub}",token);
            if(!request.TryGetProperty("busy",out var busy)||!busy.GetBoolean()){metrics?.Record("search.ready",readiness.Elapsed);return request;}
            if(readiness.Elapsed>TimeSpan.FromSeconds(60))throw new TradeQueryException("İstemci arama bekleme süresi 60 saniyede dolmadı; istemci saati ilerlemiyor olabilir.");
            var waitMs=request.TryGetProperty("waitMs",out var wait)?Math.Clamp(wait.GetInt32(),20,500):20;
            progress($"{category.Label} • istemcinin arama süresi bekleniyor");await Task.Delay(waitMs,token);
        }
    }
    private static async Task<JsonElement> SearchPriceReady(ITradeBridge bridge,FakeLookup lookup,Action<string> progress,CancellationToken token)
    {
        var readiness=Stopwatch.StartNew();
        while(true){
            var request=await bridge.Command($"searchprice {lookup.Category.Job} {lookup.Category.Category} {lookup.Category.Sub} {lookup.Yang} {lookup.Yang} {lookup.Won} {lookup.Won}",token);
            if(!request.TryGetProperty("busy",out var busy)||!busy.GetBoolean())return request;
            if(readiness.Elapsed>TimeSpan.FromSeconds(60))throw new TradeQueryException("İstemci arama bekleme süresi 60 saniyede dolmadı; istemci saati ilerlemiyor olabilir.");
            var waitMs=request.TryGetProperty("waitMs",out var wait)?Math.Clamp(wait.GetInt32(),20,500):20;
            await Task.Delay(waitMs,token);
        }
    }
    public static async Task<JsonElement> WaitResponse(ITradeBridge bridge, long generation, CancellationToken token,TimeSpan? responseTimeout=null,bool captureDetails=false)
    {
        var deadline = Stopwatch.StartNew();
        var limit=responseTimeout??TimeSpan.FromSeconds(15);
        while (deadline.Elapsed < limit)
        {
            token.ThrowIfCancellationRequested();
            var callTime=Stopwatch.StartNew();
            var result = await bridge.Command(captureDetails?$"capture {generation}":"snapshot", token);
            if (result.GetProperty("generation").GetInt64() > generation) return result;
            if(callTime.ElapsedMilliseconds<2)await Task.Delay(2,token);
        }
        throw new TimeoutException("Sunucudan yeni Ticaret Camı cevabı gelmedi (15 s). İstemci arama kısıtını, Ticaret Camını ve bağlantıyı kontrol et. Eski tampon kaydedilmedi.");
    }
}
