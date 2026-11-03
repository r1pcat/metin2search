using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;
internal static class TradeTests
{
    internal static void Run(string directory, List<string> results)
    {
        void Check(bool value,string label) { if (!value) throw new Exception("FAILED: " + label); results.Add("PASS: "+label); }
        TradePlanTests.Run(directory,results);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var raw = new byte[187];
        void U(int offset,uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(offset,4),value);
        U(1,12345); U(9,11000000); U(13,3); U(0x11,50493); U(0x8b,2);
        Encoding.GetEncoding(1254).GetBytes("Yılan bıçağı+0").CopyTo(raw,0x15);
        Encoding.GetEncoding(1254).GetBytes("ÇağrıTR").CopyTo(raw,0x2e);
        U(0x47,28030); BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x63,2),71);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0x65,2),-5); raw[0x67]=0xab;raw[186]=0xfe;
        var item=TradeDecoder.Decode(raw,0);
        Check(item.Vnum==50493&&item.Count==3&&item.Won==2&&item.Yang==11000000,"Ticaret Camı 187 bayt: adet, won ve yang ayrı alanlardan çözülüyor");
        Check(item.Seller=="ÇağrıTR"&&item.Name=="Yılan bıçağı+0"&&item.SellerVid==12345,"Ticaret Camı satıcı/eşya adı Türkçe karakterlerle okunuyor");
        Check(item.Bonuses[0]==new Bonus(71,-5,0xab)&&item.Sockets[0]==28030&&item.Raw[186]==0xfe,"Ticaret Camı efsun, soket ve bilinmeyen baytlar korunuyor");
        try {TradeDecoder.Decode(new byte[128],0);throw new Exception("128 bayt kabul edildi");} catch(InvalidDataException) {results.Add("PASS: Normal pazar kaydı Ticaret Camı satırı olarak kabul edilmiyor");}
        using var store=new LocalStore(Path.Combine(directory,"trade"));
        var category=new TradeCategory(0,1,0,"Silahlar");
        var page=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,1,1,1,raw);
        store.SaveTrade(page);store.SaveTrade(page with{Id=Guid.NewGuid().ToString("N"),Generation=2});
        Check(store.TradePageCount==2&&store.TradeItems().Count==1&&store.CompactSeenCount==2&&store.Integrity=="ok","Değişmeyen ilan tekrar sürüm oluşturmuyor; son görülme/kontrol sayısı güncelleniyor");
        Check(store.TradeItems()[0].Raw.SequenceEqual(raw),"SQLite Ticaret Camı ham veri/ayrıntı turu kayıpsız");
        using var stop=new CancellationTokenSource();
        var fake=new FakeBridge(raw);
        var saved=new List<TradePage>();
        var fullProbes=new List<TradePage>();
        var audits=new List<TradeAudit>();
        try
        {
            TradeScanner.Run(fake,p=>{saved.Add(p);store.SaveTrade(p);if(saved.Count==4)stop.Cancel();return Task.CompletedTask;},_=>{},stop.Token,a=>{audits.Add(a);store.TradeAudit(a);},purchasePage:(p,t)=>{fullProbes.Add(p);return Task.CompletedTask;}).GetAwaiter().GetResult();
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) {}
        Check(saved.Count==4&&saved.Select(p=>p.Page).SequenceEqual(new uint[]{1,2,1,2})&&
            fake.Commands.Where(c=>c.StartsWith("search ")||c.StartsWith("page ")).SequenceEqual(new[]{"search 0 1 0","page 2","search 1 2 0","page 2"}),
            "Sınıf/kategori değişmeden tüm sayfalar tamamlanıyor; sıradaki kategori ilk sayfadan başlıyor ve durdurmaya uyuyor");
        Check(fake.StaleReadCount==4&&saved.Select(p=>p.Generation).SequenceEqual(new long[]{1,2,3,4}),"Eski cevap yeni sayfa olarak kaydedilmiyor; içerik aynı olsa da yeni nesil kabul ediliyor");
        Check(fullProbes.Count==4&&fullProbes.All(p=>p.FullPriceProbe)&&fullProbes.GroupBy(p=>p.Category).All(g=>g.Select(p=>p.Page).SequenceEqual(new uint[]{1,2})),"Tam tur FAKE ilk iki sayfa sınırını her kategoride yeniden uygular");
        var detailed=store.TradeItems()[0];
        Check(detailed.StonesSummary.Contains("Öldürücü")&&detailed.ElementSummary.Contains("Ateş")&&detailed.AbsorptionSummary.Contains("%20")&&detailed.BonusSummary.Contains("Beceri"),"Taş, element, emiş ve Türkçe efsun ayrıntıları SQLite üzerinden kayıpsız geri okunuyor");
        Check(detailed.AbsorptionRate==20&&detailed.AbsorptionRateSource=="Emiş oranı: %20","Emiş oranı istemcinin gösterdiği metinden kaynağıyla ayrı okunuyor");
        Check(store.TradeItems("Öldürücü").Count==2&&store.TradeItems("bulunmayan-ürün").Count==0,"Özellik araması bütün yerel geçmişte yapılır");
        Check(store.TradeCoverage().Count==2&&store.TradeCoverage().All(r=>r.Status=="Complete"&&r.Pages==2),"Kategori kapsamı ve tamamlanan sayfalar kalıcı tutuluyor");
        var export=Path.Combine(directory,"trade-export.json");store.ExportTrade(export);
        using(var json=JsonDocument.Parse(File.ReadAllText(export)))Check(json.RootElement.GetArrayLength()==3,"JSON dışa aktarımı bütün değişiklik sürümlerini ve ayrıntıları içeriyor");
        var csv=Path.Combine(directory,"trade-export.csv");store.ExportTradeCsv(csv);
        Check(File.ReadAllText(csv).Contains("Öldürücü")&&File.ReadAllText(csv).Contains(Convert.ToBase64String(raw)),"CSV dışa aktarımında Türkçe ayrıntılar ve ham baytlar korunuyor");
        using(var firstStop=new CancellationTokenSource()){
            var pages=new List<TradePage>();var retained=new FakeBridge(raw,retainCursor:true);
            try{TradeScanner.Run(retained,p=>{pages.Add(p);if(pages.Count==2)firstStop.Cancel();return Task.CompletedTask;},_=>{},firstStop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException)when(firstStop.IsCancellationRequested){}
            Check(retained.Commands.Contains("page 1")&&pages.Select(p=>p.Page).SequenceEqual(new uint[]{1,2}),"Arama eski sayfada dönerse ilk sayfa ayrıca alınır; hiçbir ilk sayfa atlanmaz");
        }
        using(var failureStop=new CancellationTokenSource()){
            var pages=new List<TradePage>();var failures=new List<TradeAudit>();
            try{TradeScanner.Run(new FakeBridge(raw,failFirst:true),p=>{pages.Add(p);if(pages.Count==2)failureStop.Cancel();return Task.CompletedTask;},_=>{},failureStop.Token,failures.Add).GetAwaiter().GetResult();}catch(OperationCanceledException)when(failureStop.IsCancellationRequested){}
            Check(pages.Count==2&&pages.All(p=>p.Category.Job==1)&&failures.Any(a=>a.Kind=="Failed"),"Bir kategorideki hata kalan sınıf/kategorileri durdurmuyor; hata kapsam raporuna giriyor");
        }
        foreach(var pagination in new[]{false,true}){
            using var retryStop=new CancellationTokenSource();var pages=new List<TradePage>();var retryAudits=new List<TradeAudit>();
            var retryBridge=new FakeBridge(raw,retryFirstResponse:!pagination,retrySecondPage:pagination);
            try{TradeScanner.Run(retryBridge,p=>{pages.Add(p);if(pages.Count==2)retryStop.Cancel();return Task.CompletedTask;},_=>{},retryStop.Token,retryAudits.Add,responseTimeout:TimeSpan.FromMilliseconds(35)).GetAwaiter().GetResult();}catch(OperationCanceledException)when(retryStop.IsCancellationRequested){}
            Check(pages.Select(p=>p.Page).SequenceEqual(new uint[]{1,2})&&pages.Select(p=>p.QueryId).Distinct().Count()==1&&retryBridge.Commands.Count(c=>c==(pagination?"page 2":"search 0 1 0"))==2&&retryAudits.Any(a=>a.Message.Contains("yeniden deneniyor")),pagination?"Sayfa yanıtı zaman aşımında yalnızca eksik sayfa bir kez tekrar istenir; ilk sayfa çift yazılmaz":"Arama yanıtı zaman aşımında aynı kapsam bir kez yeniden aranır; yeni nesil cevap beklenir");
        }
        var absentAudits=new List<TradeAudit>();var absentPages=new List<TradePage>();var absent=new FakeBridge(raw,neverRespond:true);
        try{TradeScanner.Run(absent,p=>{absentPages.Add(p);return Task.CompletedTask;},_=>{},CancellationToken.None,absentAudits.Add,responseTimeout:TimeSpan.FromMilliseconds(35)).GetAwaiter().GetResult();throw new Exception("Unanswered queries succeeded");}catch(IOException){}
        Check(absentPages.Count==0&&absentAudits.Count(a=>a.Kind=="Failed")==2&&!absentAudits.Any(a=>a.Kind=="Complete")&&absent.Commands.Count(c=>c.StartsWith("search "))==4,"İki yanıtsız deneme boş/tamamlanmış kapsam üretmez; sınırlı deneme sonunda hata korunur");
        using(var sharedStore=new LocalStore(Path.Combine(directory,"shared-scanner")))
        using(var sharedStop=new CancellationTokenSource()){
            sharedStore.ConfigureServer("SharedTest, CH1");var sharedBridge=new FakeBridge(raw,sharedPlan:true);var pages=0;
            try{TradeScanner.Run(sharedBridge,p=>{sharedStore.SaveTrade(p);pages++;return Task.CompletedTask;},_=>{},sharedStop.Token,a=>{sharedStore.TradeAudit(a);if(a.Kind=="RunEnd")sharedStop.Cancel();}).GetAwaiter().GetResult();}catch(OperationCanceledException)when(sharedStop.IsCancellationRequested){}
            Check(pages==2&&sharedBridge.Commands.Count(c=>c.StartsWith("search "))==1&&sharedStore.ScanRuns()[0].Status=="Tamamlandı"&&sharedStore.TradeCoverage().Count==3,"Tarayıcı üç ortak sınıf için yalnız bir arama ve iki sayfa okur; üç kapsamı aynı turda tamamlar");
            Check(sharedStore.ScanRuns()[0].Expected==1&&sharedStore.ScanRuns()[0].Completed==1&&sharedStore.TradePlanCoverage().Count==1,"Ekran ve tur geçmişi üç eski kapsam yerine bir gerçek kategori gösterir");
            var interrupted=Guid.NewGuid().ToString("N");var interruptedCategory=new TradeCategory(0,3,2,"Ortak / Kalkanlar");
            sharedStore.TradeAudit(new("RunStart",interrupted,"",null,0,5,"test"){PlannedQueries=1});
            sharedStore.TradeAudit(new("Pending",interrupted,interrupted+":0",interruptedCategory,0,0,""){PlanQueryId=interrupted+":plan",PlanLabel=interruptedCategory.Label});
            sharedStore.StopTradeRun(interrupted,"Test durduruldu");
            Check(sharedStore.TradePlanCoverage()[0].Status=="Interrupted"&&sharedStore.ScanRuns()[0].Expected==1,"Durdurulan ortak kategori bekliyor kalmaz; gerçek kategori sayısı korunur");
        }
    }
    private sealed class FakeBridge(byte[] raw,bool retainCursor=false,bool failFirst=false,bool retryFirstResponse=false,bool retrySecondPage=false,bool neverRespond=false,bool sharedPlan=false):ITradeBridge
    {
        public List<string> Commands {get;}=[];
        public int StaleReadCount {get;private set;}
        private int generation,page=1; private bool stale;
        private bool retained;
        private readonly Dictionary<string,int> attempts=[];
        private bool blocked;
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(command);object result;var payload=sharedPlan&&page==1?Enumerable.Range(0,11).SelectMany(_=>raw).ToArray():raw;
            if(command=="inspect")result=new {generation=0,sort=new{recentIndex=2,unitPriceIndex=1},plan=sharedPlan?Enumerable.Range(0,3).Select(job=>new{job,category=3,sub=2,label="Karakter / Ekipmanlar / Kalkanlar"}).ToArray():new[]{new {job=0,category=1,sub=0,label="Savaşçı / Silahlar"},new {job=1,category=2,sub=0,label="Ninja / Zırhlar"}}};
            else if(command.StartsWith("setsort "))result=new{sort=new{selected=int.Parse(command[8..])}};
            else if(command=="details")result=new {generation,count=payload.Length/187,total=12,page,raw=Convert.ToHexString(payload),details=new[]{new {slot=0,source="test",status="client-rendered",lines=new[]{"Emiş oranı: %20","Ateşin Gücü +%12"},stones=new[]{new {slot=0,raw=28030,name="Öldürücü Darbe Taşı+4"}},bonuses=new[]{new {slot=0,type=71,value=-5,text="Beceri hasarı -%5"}}}}};
            else if(command.StartsWith("search ")||command.StartsWith("page "))
            {
                if(failFirst&&command=="search 0 1 0")throw new TradeQueryException("KeyError(1) test");
                var attempt=attempts.GetValueOrDefault(command)+1;attempts[command]=attempt;
                blocked=neverRespond||(retryFirstResponse&&command=="search 0 1 0"&&attempt==1)||(retrySecondPage&&command=="page 2"&&attempt==1);
                page=command.StartsWith("page ")?int.Parse(command[5..]):1;
                if(retainCursor&&!retained&&command.StartsWith("search ")){page=3;retained=true;}
                stale=true;result=new {requested=true,generation};
            }
            else
            {
                if(stale||blocked){stale=false;StaleReadCount++;result=new {generation,count=payload.Length/187,total=12,page,raw=Convert.ToHexString(payload)};}
                else {generation++;result=new {generation,count=payload.Length/187,total=12,page,raw=Convert.ToHexString(payload)};}
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}


