using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class IncrementalScanTests
{
    private static byte[] Item(uint marker,uint yang=100)
    {
        var b=new byte[187];b[46]=(byte)'A';
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5),marker);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9),yang);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13),1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(17),19);
        return b;
    }
    private static JsonElement Body(CategoryBatch batch)
    {
        using var input=new MemoryStream(batch.Body);using var br=new BrotliStream(input,CompressionMode.Decompress);using var json=JsonDocument.Parse(br);return json.RootElement.Clone();
    }
    private sealed class Market { public List<byte[]> Items=[]; }
    private sealed class MarketBridge(Market market,bool bundle=false):ITradeBridge
    {
        public List<string> Commands {get;}=[];
        public List<byte[]> LookupItems {get;set;}=[];
        private bool lookup;private List<byte[]> Source=>lookup?LookupItems:market.Items;
        private long generation;private uint page=1;private bool answerPending;private string lastRaw="";
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(command);object result;
            if(command=="inspect")result=new{generation,captureDetails=bundle,plan=new[]{new{job=0,category=1,sub=0,label="Savaşçı / Silahlar"}},sort=new{recentIndex=2,unitPriceIndex=1,selected=0}};
            else if(command.StartsWith("setsort "))result=new{sort=new{selected=int.Parse(command[8..])}};
            else if(command.StartsWith("searchprice ")){page=1;lookup=true;answerPending=true;result=new{requested=true,generation};}
            else if(command.StartsWith("search ")){page=1;lookup=false;answerPending=true;result=new{requested=true,generation};}
            else if(command.StartsWith("page ")){page=uint.Parse(command[5..]);answerPending=true;result=new{requested=true,generation};}
            else if(command=="snapshot"||command.StartsWith("capture "))
            {
                if(answerPending){answerPending=false;generation++;}
                var raw=Source.Skip((int)(page-1)*11).Take(11).SelectMany(x=>x).ToArray();lastRaw=Convert.ToHexString(raw);
                if(command.StartsWith("capture ")){
                    if(generation<=long.Parse(command[8..]))result=new{generation};
                    else result=new{generation,count=raw.Length/187,total=(uint)Source.Count,page,raw=lastRaw,details=new[]{new{slot=0,status="client-rendered",lines=new[]{"bundled-fixture"}}}};
                }else result=new{generation,count=raw.Length/187,total=(uint)Source.Count,page,raw=lastRaw};
            }
            else if(command=="details")result=new{generation,count=0,total=(uint)Source.Count,page,raw=lastRaw,details=Array.Empty<object>()};
            else throw new TradeQueryException("unexpected command "+command);
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }

    private sealed class ScriptedFake(List<FakeLookup> lookups,Func<FakeLookup,TradePage,bool> verify):IFakeLookupSource
    {
        public int PendingCalls;
        public IReadOnlyList<FakeLookup> Pending(){PendingCalls++;return lookups;}
        public Task<bool> Verify(FakeLookup lookup,TradePage page,CancellationToken token)=>Task.FromResult(verify(lookup,page));
    }
    internal static void Run(string directory,List<string> results)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);results.Add("PASS: "+label);}
        static DateTimeOffset Istanbul(int day,int hour,int minute)=>new DateTimeOffset(2026,10,day,hour,minute,0,TimeSpan.FromHours(3));
        {
            var combinedMarket=new Market();for(uint i=1;i<=25;i++)combinedMarket.Items.Add(Item(i));
            var combinedBridge=new MarketBridge(combinedMarket,bundle:true);var combinedPages=new List<TradePage>();
            using var combinedStop=new CancellationTokenSource();
            try{TradeScanner.Run(combinedBridge,p=>{combinedPages.Add(p);return Task.CompletedTask;},_=>{},combinedStop.Token,a=>{if(a.Kind=="RunEnd")combinedStop.Cancel();}).GetAwaiter().GetResult();}
            catch(OperationCanceledException)when(combinedStop.IsCancellationRequested){}
            Check(combinedPages.Select(p=>p.Page).SequenceEqual(new uint[]{1,2,3})&&combinedPages.All(p=>p.ClientDetails is not null&&p.Items[0].TooltipLines.Contains("bundled-fixture")),"Birleşik capture bütün sayfaları ve aynı nesle ait tooltip ayrıntılarını kayıpsız taşır");
            Check(combinedBridge.Commands.Count(c=>c.StartsWith("capture "))==3&&!combinedBridge.Commands.Contains("details")&&!combinedBridge.Commands.Contains("snapshot"),"Birleşik okuma her yeni sayfada ayrı snapshot/details köprü çağrısını kaldırır");
            using var quietStop=new CancellationTokenSource(60);var quietTimedOut=false;
            try{TradeScanner.WaitResponse(combinedBridge,3,quietStop.Token,captureDetails:true).GetAwaiter().GetResult();}
            catch(OperationCanceledException)when(quietStop.IsCancellationRequested){quietTimedOut=true;}
            Check(quietTimedOut,"Yalnız nesil içeren eski capture yanıtı ürün sayfası sayılmaz ve iptal edilebilir");
        }

        var now=Istanbul(17,11,59);
        var slots=new ScanScheduler(()=>now);slots.SetPlan(["0:1:0","0:2:0"]);
        var opening=slots.Next();
        Check(opening.Kind==ScanKind.Hot&&opening.CategoryKeys!.Count==2,"Açılışta tam tur yapılmaz: ilk geçiş hızlı geçiştir ve henüz görülmemiş kategorilerin hepsini ziyaret eder");
        var fullSlots=new List<string>();
        foreach(var (day,hour,minute,expectFull) in new[]{(17,12,1,true),(17,12,30,false),(17,17,59,false),(17,18,1,true),(17,23,59,false),(18,0,1,true),(18,5,59,false),(18,6,1,true),(18,6,30,false)})
        {
            now=Istanbul(day,hour,minute);var kind=slots.Next().Kind;
            if((kind==ScanKind.Full)!=expectFull)throw new Exception($"FAILED: tam tur zamanı {day}.{hour:00}:{minute:00} -> {kind}");
            if(kind==ScanKind.Full)fullSlots.Add($"{day}.{hour:00}:{minute:00}");
        }
        Check(fullSlots.SequenceEqual(["17.12:01","17.18:01","18.00:01","18.06:01"]),"Tam tur yalnız İstanbul 00:00, 06:00, 12:00 ve 18:00 diliminde bir kez istenir");

        now=Istanbul(17,9,0);
        var bootstrap=new ScanScheduler(()=>now){HasCompleteRead=key=>key=="0:1:0"};bootstrap.SetPlan(["0:1:0","0:2:0","0:3:0"]);
        var needed=bootstrap.Next();
        Check(needed.Kind==ScanKind.Sweep&&needed.CategoryKeys!.SequenceEqual(["0:2:0","0:3:0"]),"Hiç tam okunmamış kategoriler (yeni sunucu/kategori) ilk ziyarette baştan sona okunur; okunmuş olan okunmaz");
        bootstrap.CategoryRead(ScanKind.Sweep,"0:2:0",30,0,30,true);bootstrap.CategoryFailed(ScanKind.Sweep,"0:3:0");
        Check(bootstrap.Next().Kind==ScanKind.Hot,"Tam okuması denenen kategori (başarılı veya başarısız) tekrar tam okumaya zorlanmaz");

        now=Istanbul(17,9,0);
        var plain=new ScanScheduler(()=>now){EnableSweeps=false};plain.SetPlan(["0:1:0","0:2:0"]);
        plain.Next();foreach(var key in new[]{"0:1:0","0:2:0"})plain.CategoryRead(ScanKind.Full,key,40,0,0,true);
        plain.CategoryRead(ScanKind.Hot,"0:1:0",37,0,0,false);now+=TimeSpan.FromMinutes(70);
        Check(plain.Next().Kind==ScanKind.Hot&&plain.Next().Kind==ScanKind.Hot,"Tek kategorili doğrulama taraması kapalıyken sayım düşse veya süre geçse de yalnız hızlı geçiş yapılır");
        now=Istanbul(17,9,0);
        var sweeping=new ScanScheduler(()=>now){EnableSweeps=true};sweeping.SetPlan(["0:1:0","0:2:0"]);
        sweeping.Next();foreach(var key in new[]{"0:1:0","0:2:0"})sweeping.CategoryRead(ScanKind.Full,key,40,0,0,true);
        sweeping.Next();sweeping.CategoryRead(ScanKind.Hot,"0:1:0",37,0,0,false);now+=TimeSpan.FromMinutes(6);
        var sweep=sweeping.Next();
        Check(sweep.Kind==ScanKind.Sweep&&sweep.CategoryKeys!.SequenceEqual(["0:1:0"]),"Açıkken: toplamı beklenenden fazla düşen kategori doğrulama taramasına alınır; diğeri alınmaz");

        var tierNow=Istanbul(17,9,0);
        string[] keys4=["0:1:0","0:2:0","0:3:0","0:4:0"];
        var tier=new ScanScheduler(()=>tierNow);tier.SetPlan(keys4);
        var firstHot=tier.Next();
        Check(firstHot.Kind==ScanKind.Hot&&firstHot.CategoryKeys!.Count==4,"İlk hızlı geçiş bütün kategorileri bir kez ziyaret eder");
        foreach(var k in keys4)tier.CategoryRead(ScanKind.Hot,k,k=="0:2:0"?13u:10u,k=="0:2:0"?3:0,k=="0:2:0"?3:0,false);
        var second=tier.Next();
        Check(second.Kind==ScanKind.Hot&&second.CategoryKeys!.SequenceEqual(["0:2:0"]),"Yeni ilan çıkan kategori hemen yeniden ziyaret edilir; sakin kategoriler 8 dk beklemeye alınır");
        tierNow+=TimeSpan.FromMinutes(9);
        var third=tier.Next();
        Check(third.Kind==ScanKind.Hot&&third.CategoryKeys!.Count==4&&third.CategoryKeys[0]=="0:2:0",$"Sakin kategoriler 8 dk sonra yeniden sıraya girer; en yeni değişen kategori başta gider ({string.Join(",",third.CategoryKeys!)})");
        foreach(var k in keys4)tier.CategoryRead(ScanKind.Hot,k,k=="0:2:0"?13u:10u,0,0,false);
        tierNow+=TimeSpan.FromMinutes(5);
        var warm=tier.Next();
        Check(warm.Kind==ScanKind.Hot&&warm.CategoryKeys!.SequenceEqual(["0:2:0"]),"Değişimi 10 dk'dan eski kategori 'ılık' olur ve 2 dk aralıkla ziyaret edilir");
        tier.CategoryFailed(ScanKind.Hot,"0:3:0");tier.CategoryFailed(ScanKind.Hot,"0:3:0");
        tierNow+=TimeSpan.FromMinutes(5);
        Check(!tier.Next().CategoryKeys!.Contains("0:3:0"),"İki kez cevap vermeyen kategori 6 dk geri çekilir (üstel geri çekilme)");
        tierNow+=TimeSpan.FromMinutes(2);
        Check(tier.Next().CategoryKeys!.Contains("0:3:0"),"Geri çekilme süresi dolunca kategori yeniden denenir");

        using(var timed=new LocalStore(Path.Combine(directory,"timed-items")))
        {
            timed.ConfigureServer("Safir, CH1");
            JsonElement Lines(params string[] lines)=>JsonSerializer.SerializeToElement(new[]{new{slot=0,status="client-rendered",lines,stones=Array.Empty<object>(),bonuses=Array.Empty<object>(),rendererCalls=new{AppendTextLine=lines.Select(l=>new object[]{l,-1}).ToArray()}}});
            PageSaveResult Save(byte[] raw,JsonElement details)
            {
                var result=new PageSaveResult();
                timed.SaveTrade(new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,new TradeCategory(0,9,2,"Kostüm"),1,1,1,raw){ClientDetails=details,Result=result});
                return result;
            }
            long Versions()=>(long)timed.Query("SELECT COUNT(*) FROM compact_versions")[0][0]!;
            var horns=Item(77);
            var created=Save(horns,Lines("Alevli boynuzlar+ (e)","Kalan Süre : 13g 14sa. 5dk."));
            var counted=Save(horns,Lines("Alevli boynuzlar+ (e)","Kalan Süre : 13g 14sa. 4dk."));
            Check(created.Appeared==1&&counted.Changed==0&&counted.ChangedSlots.Count==0&&counted.Unchanged==1&&Versions()==1,"Yalnız 'Kalan Süre' geri sayımı değişen süreli ilan yeni sürüm üretmez");
            var wrapped=Save(horns,Lines("Alevli boynuzlar+","(e)","Kalan Süre : 13g 14sa. 3dk."));
            Check(wrapped.Changed==0&&Versions()==1,"Satır kaydırması değişse de geri sayım dışında aynı metin değişmemiş sayılır");
            var priced=Save(Item(77,200),Lines("Alevli boynuzlar+ (e)","Kalan Süre : 13g 14sa. 2dk."));
            Check(priced.Changed==1&&priced.ChangedSlots.SetEquals([0])&&Versions()==2,"Süreli ilanda fiyat değişirse yeni sürüm oluşur");
            var edited=Save(Item(77,200),Lines("Alevli boynuzlar+ (e)","Ölümsüzlere karşı güçlü +%10","Kalan Süre : 13g 14sa. 1dk."));
            Check(edited.Changed==1&&Versions()==3,"Süreli ilanda geri sayım dışında bir metin değişirse yeni sürüm oluşur");
        }

        {
            var fakeMarket=new Market();for(uint i=1;i<=3;i++)fakeMarket.Items.Add(Item(500-i));
            var fakeBridge=new MarketBridge(fakeMarket){LookupItems=[Item(900),Item(901)]};
            var cat=new TradeCategory(0,1,0,"x");
            var wanted=new List<FakeLookup>{new("Zulu",201,"k1",cat,9000,0,19,1),new("Zulu",201,"k2",cat,5000,0,19,1),new("Yankee",202,"k3",cat,0,3,19,1)};
            using var fakeStore=new LocalStore(Path.Combine(directory,"fake-pass"));fakeStore.ConfigureServer("Safir, CH1");
            using var stopFake=new CancellationTokenSource();var verified=new List<string>();
            var source=new ScriptedFake(wanted,(l,page)=>{verified.Add(l.ListingKey+":"+page.Items.Count);if(l.ListingKey=="k3")stopFake.Cancel();return l.ListingKey!="k1";});
            try{TradeScanner.Run(fakeBridge,p=>{fakeStore.SaveTrade(p);return Task.CompletedTask;},_=>{},stopFake.Token,fakeStore.TradeAudit,scheduler:new ScanScheduler{HasCompleteRead=_=>true},fakeLookups:source).GetAwaiter().GetResult();}
            catch(OperationCanceledException)when(stopFake.IsCancellationRequested){}
            var sent=fakeBridge.Commands.Where(c=>c.StartsWith("search")||c.StartsWith("page ")).ToArray();
            Check(sent.SequenceEqual(["search 0 1 0","searchprice 0 1 0 9000 9000 0 0","searchprice 0 1 0 5000 5000 0 0","searchprice 0 1 0 0 0 3 3"])&&verified.SequenceEqual(["k1:2","k2:2","k3:2"]),$"Hızlı geçişten sonra her bilinen FAKE satıcı kategori + tam fiyat aralığıyla aranır; bulunamazsa aynı satıcının sonraki ilanı denenir, bulunca satıcı bitmiş sayılır ({string.Join(" | ",sent)})");
            var fullOnly=new ScriptedFake(wanted,(_,_)=>true);using var stopFull=new CancellationTokenSource();
            try{TradeScanner.Run(new MarketBridge(fakeMarket),p=>{fakeStore.SaveTrade(p);return Task.CompletedTask;},_=>{},stopFull.Token,a=>{fakeStore.TradeAudit(a);if(a.Kind=="RunEnd")stopFull.Cancel();},fakeLookups:fullOnly).GetAwaiter().GetResult();}
            catch(OperationCanceledException)when(stopFull.IsCancellationRequested){}
            Check(fullOnly.PendingCalls==0,"Tam turdan sonra FAKE yeniden doğrulama geçişi çalışmaz");
        }

        var clock=Istanbul(17,15,0);
        var scheduler=new ScanScheduler(()=>clock){EnableSweeps=true,HasCompleteRead=_=>false};
        var market=new Market();for(uint i=1;i<=25;i++)market.Items.Add(Item(1000-i));
        using var store=new LocalStore(Path.Combine(directory,"incremental"));store.ConfigureServer("Safir, CH1");
        var bridge=new MarketBridge(market);var runEnds=0;var mark=0;var purchaseCalls=0;
        string? lastSavedId=null;var fullProbePages=new List<uint>();
        string[] Since(){var slice=bridge.Commands.Skip(mark).Where(c=>c.StartsWith("search ")||c.StartsWith("page ")).ToArray();mark=bridge.Commands.Count;return slice;}
        long Active()=>(long)store.Query("SELECT COUNT(*) FROM listing_lifecycle WHERE is_active=1")[0][0]!;
        string KindOf(int ordinal)=>(string)store.Query("SELECT k.kind FROM lifecycle_runs r JOIN scan_run_kinds k ON k.run_id=r.run_id ORDER BY r.started")[ordinal][0]!;
        var steps=new List<Action>
        {
            () => {
                var read=Since();
                var batches=store.PendingCategoryBatches();var body=Body(batches[0]);
                Check(KindOf(0)=="Sweep"&&read.SequenceEqual(["search 0 1 0","page 2","page 3"])&&bridge.Commands.Contains("setsort 2"),"Yeni sunucuda hiç tam okunmamış kategori ilk ziyarette baştan sona okunur; En yeni sıralaması seçilir");
                Check(batches.Count==1&&body.GetProperty("complete").GetBoolean()&&body.GetProperty("upserts").GetArrayLength()==25&&Active()==25,"Bu ilk okuma bütün ilanları tek tam kategori paketiyle yayınlar");
                clock+=TimeSpan.FromMinutes(9);
            },
            () => {
                var read=Since();
                Check(KindOf(1)=="Hot"&&read.SequenceEqual(["search 0 1 0"])&&store.PendingCategoryBatches().Count==1,"Sonraki geçişte ilk ilan zaten bilinen ilansa kategori ilk sayfada biter ve paket üretilmez");
                market.Items.Insert(0,Item(2000));market.Items.Insert(0,Item(2001));clock+=TimeSpan.FromMinutes(9);
            },
            () => {
                var read=Since();var batches=store.PendingCategoryBatches();var body=Body(batches.Last());
                Check(read.SequenceEqual(["search 0 1 0"]),"Yeni ilanlar en üstte, ardından bilinen ilan gelince kategori orada biter (ikinci sayfa okunmaz)");
                Check(batches.Count==2&&!body.GetProperty("complete").GetBoolean()&&body.GetProperty("kind").GetString()=="incremental"&&body.GetProperty("upserts").GetArrayLength()==2
                    &&body.GetProperty("tombstones").GetArrayLength()==0&&body.GetProperty("membershipRemovals").GetArrayLength()==0&&body.GetProperty("previousRevision").GetInt64()==1&&body.GetProperty("revision").GetInt64()==2,"Hızlı geçiş yalnız yeni ilanları, kaldırma içermeyen kısmi paketle yayınlar; revision zinciri sürer");
                Check(Active()==27&&scheduler.State("0:1:0")!.Value.disappeared==0,"Yeni ilanlar aktif olur; sayım muhasebesi kayıp görmez");
                market.Items.RemoveRange(12,3);clock+=TimeSpan.FromMinutes(6);
            },
            () => {
                var read=Since();
                Check(read.SequenceEqual(["search 0 1 0"])&&store.PendingCategoryBatches().Count==2,"Ortadan ilan kaybolsa da hızlı geçiş ilk sayfada durur ve yeni paket üretmez");
                Check(Active()==27&&scheduler.State("0:1:0")!.Value.disappeared==3,"Hızlı geçiş ilan kaldırmaz; toplamdaki 3'lük düşüş yalnız ipucu olur");
                Check((long)store.Query("SELECT COALESCE(MAX(misses),0) FROM scope_memberships")[0][0]! == 0,"Hızlı geçiş yokluk sayaçlarına dokunmaz");
            },
            () => {
                var read=Since();var body=Body(store.PendingCategoryBatches().Last());
                Check(read.SequenceEqual(["search 0 1 0","page 2","page 3"]),"Doğrulama taraması (açıkken) kirli kategoriyi baştan sona okur");
                Check(body.GetProperty("complete").GetBoolean()&&body.GetProperty("tombstones").GetArrayLength()==3&&Active()==24,"Sayım tutarlı olduğundan tek tam okumada kaybolan üç ilan kaldırılır");
                Check(store.ScanRuns().Count==2,"Hızlı geçişler tur geçmişini doldurmaz");
                market.Items.RemoveAt(5);clock=Istanbul(17,18,1);
            },
            () => {
                var read=Since();var body=Body(store.PendingCategoryBatches().Last());
                var runs=store.Query("SELECT r.status,k.kind FROM lifecycle_runs r JOIN scan_run_kinds k ON k.run_id=r.run_id ORDER BY r.started").Select(r=>((string)r[0]!,(string)r[1]!)).ToList();
                Check(runs.Last()==("Complete","Full")&&read.SequenceEqual(["search 0 1 0","page 2","page 3"]),"İstanbul 18:00 diliminde tam tur çalışır ve bütün sayfaları okur");
                Check(body.GetProperty("tombstones").GetArrayLength()==1&&Active()==23,"Tam tur, araya satılan ilanı kaldırır");
                Check(runs.Where(r=>r.Item2=="Hot").All(r=>r.Item1=="Partial")&&runs.Count(r=>r.Item2=="Full")==1,"Tur türleri kayıtlıdır: hızlı geçişler 'Partial', tam tur 'Complete'");
                market.Items.Insert(5,Item(3000));clock+=TimeSpan.FromMinutes(9);
            },
            () => {
                var read=Since();var body=Body(store.PendingCategoryBatches().Last());
                Check(read.SequenceEqual(["search 0 1 0","page 2"])&&body.GetProperty("kind").GetString()=="incremental"&&body.GetProperty("upserts").GetArrayLength()==1,"Bilinen ilanın altında yeni ilan görülürse sıraya güvenilmez: sonraki sayfa da okunur ve ilan yakalanır");
                Check(JsonSerializer.SerializeToElement(store.Metrics.Snapshot()).GetProperty("stages").TryGetProperty("hot.order-violation",out _),"Sıra bozulması ölçüm olarak kaydedilir");
            },
        };
        using var stop=new CancellationTokenSource();
        void OnAudit(TradeAudit audit)
        {
            store.TradeAudit(audit);
            if(audit.Kind!="RunEnd")return;
            runEnds++;steps[runEnds-1]();
            if(runEnds>=steps.Count)stop.Cancel();
        }
        try{TradeScanner.Run(bridge,async p=>{await Task.Yield();store.SaveTrade(p);lastSavedId=p.Id;},_=>{},stop.Token,OnAudit,metrics:store.Metrics,purchasePage:(p,t)=>{if(lastSavedId!=p.Id)throw new Exception("FAKE callback ran before durable page save");purchaseCalls++;if(p.FullPriceProbe)fullProbePages.Add(p.Page);return Task.CompletedTask;},scheduler:scheduler).GetAwaiter().GetResult();}
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        Check(runEnds==steps.Count&&store.Integrity=="ok","Artımlı tarama senaryosu bütünlük bozulmadan tamamlanır");
        Check(purchaseCalls==7&&fullProbePages.SequenceEqual(new uint[]{1,2}),$"Tam turda yalnız ilk iki sayfa, hızlı geçişte okunan sayfalar kontrol edilir; 3. tam sayfa/Sweep kontrol edilmez ve kayıt beklenir ({purchaseCalls} çağrı)");
        var sorts=bridge.Commands.Where(c=>c.StartsWith("setsort ")).ToArray();
        Check(sorts.SequenceEqual(new[]{"setsort 2","setsort 2","setsort 2","setsort 2","setsort 2","setsort 1","setsort 2"}),"Tam tur bireysel en düşük fiyatla başlar; sonraki hızlı geçiş yeniden En yeni sırasına döner");
    }
}
