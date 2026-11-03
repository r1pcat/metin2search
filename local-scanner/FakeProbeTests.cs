using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class FakeProbeTests
{
    private sealed class Bridge:ITradeBridge
    {
        internal readonly List<(string Seller,int Slot)> Sent=[];
        internal readonly HashSet<string> Preparing=[];
        internal readonly HashSet<string> Timeouts=[];
        internal readonly List<string> Commands=[];
        internal long Wallet=5000;
        internal bool StopAck=true;
        internal List<TradeItem> Items=[];
        private int slot=-1;private string nonce="",seller="";
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(command.Split(' ')[0]);object reply=new{ready=true,completed=true,stopped=StopAck};
            if(command.StartsWith("purchaseinspect "))
            {
                var parts=command.Split(' ');slot=int.Parse(parts[1]);nonce=parts[3];var item=Items.Single(i=>i.Slot==slot);seller=item.Seller;
                reply=new{nonce,raw=Convert.ToHexString(item.Raw).ToLowerInvariant(),seller=item.Seller,shopVid=item.SellerVid,yang=item.Yang,won=item.Won,serverInfo="Safir, CH1",sendingSupported=true,
                    wallet=new{walletVerified=true,yang=Wallet,won=0,serverInfo="Safir, CH1"}};
            }
            else if(command.StartsWith("purchasebegin ")){Sent.Add((seller,slot));reply=new{sent=true,nonce,sendCount=1};}
            else if(command=="purchasepoll")reply=new{nonce,code=Timeouts.Contains(seller)?"UNKNOWN":Preparing.Contains(seller)?"PREMIUM_PREPARING":"NOT_ENOUGH_MONEY",text=Timeouts.Contains(seller)?"No unambiguous response before timeout":"fixture",requestAndShopVerified=!Timeouts.Contains(seller)};
            return Task.FromResult(JsonSerializer.SerializeToElement(reply));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Probe(long wallet):IPurchaseCheckTransport
    {
        public int Sends;
        public Task<PurchasePreflight> Inspect(PurchaseTarget target,CancellationToken token)=>Task.FromResult(new PurchasePreflight(true,wallet,0,true,true,""));
        public Task<PurchaseResponse> SendOnce(PurchaseTarget target,CancellationToken token){Sends++;return Task.FromResult(new PurchaseResponse("NOT_ENOUGH_MONEY","fixture",true));}
    }
    private static byte[] Record(string seller,uint vid,uint yang,uint won=0)
    {
        var raw=new byte[187];BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(1),vid);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(9),yang);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(13),1);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(17),189);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x8b),won);
        Encoding.ASCII.GetBytes(seller).CopyTo(raw,0x2e);return raw;
    }

    internal static void Run(string directory,List<string> results)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);results.Add("PASS: "+label);}

        var target=new PurchaseTarget("safir","session",22,"Seller","key","nonce",0,1000,new string('0',374),1,0);
        var rich=new Probe(5000);var saved=new List<PurchaseCheckRecord>();
        var guarded=new PurchaseCheckCycle(rich,saved.Add,(_,_)=>Task.CompletedTask,new PurchaseCheckBudget(5000),refuseAffordable:true);
        Check(guarded.Check(target,CancellationToken.None).GetAwaiter().GetResult() is null&&rich.Sends==0&&!guarded.Stopped&&saved.Count==0,"Cüzdanın ödeyebileceği ilan için hiçbir istek gönderilmez ve hiçbir kayıt yazılmaz (bütçe defterine dokunulmaz)");
        var poor=new Probe(500);var poorSaved=new List<PurchaseCheckRecord>();
        var unaffordable=new PurchaseCheckCycle(poor,poorSaved.Add,(_,_)=>Task.CompletedTask,new PurchaseCheckBudget(500),refuseAffordable:true).Check(target,CancellationToken.None).GetAwaiter().GetResult();
        Check(unaffordable is {Result:"NotEnoughMoney",Sent:true}&&poor.Sends==1,"Cüzdanın ödeyemeyeceği ilan normal şekilde denenir");
        var wonTarget=target with{Won=2,Yang=0,RequestId="won"};var wonProbe=new Probe(500);
        new PurchaseCheckCycle(wonProbe,poorSaved.Add,(_,_)=>Task.CompletedTask,new PurchaseCheckBudget(500),refuseAffordable:true).Check(wonTarget,CancellationToken.None).GetAwaiter().GetResult();
        Check(wonProbe.Sends==1,"Won fiyatlı ilan (cüzdanda Won yok) denenebilir");

        using var store=new LocalStore(Path.Combine(directory,"fake-probe"));store.ConfigureServer("Safir, CH1");
        var category=new TradeCategory(0,2,0,"Silah");
        var rows=new[]{
            ("Alpha",101u,1000u,0u),
            ("Alpha",101u,90000u,0u),
            ("Bravo",102u,500u,0u),
            ("Charlie",103u,9000u,0u),
            ("Delta",104u,9000u,0u),
            ("Echo",105u,0u,2u),
            ("Foxtrot",106u,9000u,0u),
            ("Golf",107u,700u,0u)};
        var page=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,5,1,19,rows.SelectMany(r=>Record(r.Item1,r.Item2,r.Item3,r.Item4)).ToArray()){Result=new PageSaveResult()};
        store.SaveTrade(page);
        page.Result!.ChangedSlots.Clear();page.Result.ChangedSlots.UnionWith([0,2,5,6,7]);
        PurchaseCheckRecord History(int slot,string result,TimeSpan ago)
        {
            var t=store.PurchaseTargetFor(page,slot)!;var shop=result=="Preparing";
            return new PurchaseCheckRecord(1,t.Server,t.Session,t.ShopVid,t.Seller,Guid.NewGuid().ToString("N"),t.ListingKey,(DateTimeOffset.UtcNow-ago).ToString("O"),result,"request-and-shop",shop?"shop":"listing","fixture","offline-test",true,false);
        }
        store.SavePurchaseCheck(History(4,"Preparing",TimeSpan.FromMinutes(30)));
        store.SavePurchaseCheck(History(5,"NotEnoughMoney",TimeSpan.FromHours(1)));
        store.SavePurchaseCheck(History(6,"NotEnoughMoney",TimeSpan.FromHours(7)));
        Check(store.LastConclusiveCheck("Delta",104) is {Result:"Preparing"}&&store.LastConclusiveCheck("Echo",105) is {Result:"NotEnoughMoney"}&&store.LastConclusiveCheck("Charlie",103) is null&&store.LastConclusiveCheck("Delta",999) is null,"Satıcının son kesin cevabı satıcı adı ve pazar VID'siyle okunur; başka VID veya hiç denenmemiş satıcı boş döner");

        var bridge=new Bridge{Items=page.Items};bridge.Preparing.Add("Delta");bridge.Preparing.Add("Foxtrot");
        using var checks=new ScannerPurchaseChecks(store,bridge,5000,Path.Combine(directory,"fake-probe-ledger","budget.json"));
        checks.StartCycle();var messages=new List<string>();
        checks.Page(page,messages.Add,CancellationToken.None).GetAwaiter().GetResult();
        Check(bridge.Sent.SequenceEqual([("Alpha",1),("Charlie",3),("Delta",4),("Foxtrot",6)]),$"Tam turda kaydedilmiş değişmeyen fakat hiç doğrulanmamış pazar da ilk hızlı geçişte kontrol edilir ({string.Join(",",bridge.Sent.Select(s=>s.Seller+"#"+s.Slot))})");
        Check(!bridge.Sent.Any(s=>s.Seller is "Bravo" or "Echo" or "Golf"),"Ödenebilir ilan veya yakın zamanda temiz sonuç için istek gönderilmez");
        Check(store.LastConclusiveCheck("Alpha",101) is {Result:"NotEnoughMoney"}&&store.LastConclusiveCheck("Delta",104) is {Result:"Preparing"}&&store.LastConclusiveCheck("Foxtrot",106) is {Result:"Preparing"},"Sonuçlar satıcı bazında kaydedilir (Alpha temiz, Delta ve Foxtrot hazırlanıyor)");

        var second=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,6,2,19,Record("Golf",107,8000)){Result=new PageSaveResult()};
        store.SaveTrade(second);second.Result!.ChangedSlots.Clear();
        bridge.Items=second.Items;
        checks.Page(second,messages.Add,CancellationToken.None).GetAwaiter().GetResult();
        Check(bridge.Sent.Count==5&&bridge.Sent[4]==("Golf",0),"Ödenemeyen ilan sonraki sayfada çıkarsa o geçişte satıcı yine denenir");
        bridge.Items=page.Items;
        checks.Page(page,messages.Add,CancellationToken.None).GetAwaiter().GetResult();
        Check(bridge.Sent.Count==5,"Aynı geçişte aynı satıcı ikinci kez denenmez");
        checks.StartCycle();
        Check(checks.WantedCount==0,"Yeni geçiş bekleyen satıcı listesini sıfırlar");
        using(var timeoutStore=new LocalStore(Path.Combine(directory,"zero-timeout"))){
            timeoutStore.ConfigureServer("Safir, CH1");
            var timeoutPage=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,8,1,2,Record("Silent",901,9000).Concat(Record("Next",902,8000)).ToArray()){Result=new PageSaveResult()};
            timeoutStore.SaveTrade(timeoutPage);timeoutPage.Result!.ChangedSlots.Clear();
            var zeroBridge=new Bridge{Wallet=0,Items=timeoutPage.Items};zeroBridge.Timeouts.Add("Silent");
            var zeroPath=Path.Combine(directory,"zero-timeout-ledger","state.json");
            using(var zeroChecks=new ScannerPurchaseChecks(timeoutStore,zeroBridge,0,zeroPath)){
                zeroChecks.StartCycle();zeroChecks.Page(timeoutPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
                Check(!zeroChecks.IsHalted&&zeroChecks.QuarantinedCount==1&&zeroBridge.Sent.Select(s=>s.Seller).SequenceEqual(new[]{"Silent","Next"}),"Sıfır bütçede cevapsız satıcı bekletilir, aynı hızlı sayfadaki diğer pazar kontrolü durmadan sürer");
                Check(zeroBridge.Commands.Count(c=>c=="purchasestop")==1&&zeroBridge.Commands.Count(c=>c=="purchasestart")==2,"Cevapsız isteğin monitorü kapatılır; sonraki hedef yeni monitor ve taze cüzdan kontrolünden geçer");
                zeroChecks.StartCycle();zeroChecks.Page(timeoutPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
                Check(zeroBridge.Sent.Count(s=>s.Seller=="Silent")==1&&!timeoutStore.SellerKnownFake("Silent"),"Cevapsız satıcı sonraki geçişte tekrar denenmez ve Unknown sonucu FAKE sayılmaz");
            }
            var uncertain=timeoutStore.Query("SELECT json_extract(payload_json,'$.requestId') FROM purchase_checks WHERE json_extract(payload_json,'$.seller')='Silent'").Single()[0] as string;
            File.WriteAllText(zeroPath,JsonSerializer.Serialize(new{schemaVersion=1,authorizedYang=0,remainingYang=0,halted=true,phase="Unknown",requestId=uncertain}));
            var beforeLedger=File.ReadAllText(zeroPath);
            using(var restarted=new ScannerPurchaseChecks(timeoutStore,new Bridge(),0,zeroPath))
                Check(!restarted.IsHalted&&restarted.ResumedZeroBudgetTimeout&&restarted.QuarantinedCount==1&&File.ReadAllText(zeroPath)==beforeLedger,"Eski sıfır bütçeli timeout kaydı kaynak kanıtla yeni bağlantıda diğer pazarlara izin verir; orijinal kayıt korunur");
            var funded=new Bridge{Wallet=1,Items=timeoutPage.Items};
            using(var guardedZero=new ScannerPurchaseChecks(timeoutStore,funded,0,zeroPath)){
                guardedZero.StartCycle();guardedZero.Page(timeoutPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
                var fresh=timeoutPage with{Id=Guid.NewGuid().ToString("N"),Raw=Record("Fresh",903,5000)};timeoutStore.SaveTrade(fresh);funded.Items=fresh.Items;
                guardedZero.Page(fresh,_=>{},CancellationToken.None).GetAwaiter().GetResult();
                Check(guardedZero.IsHalted&&funded.Sent.Count==0,"Timeout sonrası devam profili dolu cüzdana izin vermez; 1 Yang bile varsa hiçbir satın alma isteği gönderilmez");
            }
        }

        using var fakeStore=new LocalStore(Path.Combine(directory,"fake-recheck"));fakeStore.ConfigureServer("Safir, CH1");
        var fakeRows=new[]{("Zulu",201u,9000u,0u),("Zulu",201u,300u,0u),("Yankee",202u,0u,3u)};
        var fakePage=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,9,1,3,fakeRows.SelectMany(r=>Record(r.Item1,r.Item2,r.Item3,r.Item4)).ToArray()){Result=new PageSaveResult()};
        fakeStore.SaveTrade(fakePage);
        var stamp=DateTimeOffset.UtcNow.ToString("O");
        for(var slot=0;slot<3;slot++)
        {
            var known=fakeStore.PurchaseTargetFor(fakePage,slot)!;
            fakeStore.Execute("INSERT OR REPLACE INTO listing_lifecycle VALUES(?,1,?,NULL,'Observed',(SELECT MAX(id) FROM compact_versions WHERE key=?))",known.ListingKey,stamp,known.ListingKey);
            fakeStore.Execute("INSERT OR REPLACE INTO scope_memberships VALUES(?,?,?,?,0,1)","ch1:0:2:0",known.ListingKey,stamp,"run");
        }
        PurchaseCheckRecord Known(int slot,string result){var k=fakeStore.PurchaseTargetFor(fakePage,slot)!;return new PurchaseCheckRecord(1,k.Server,k.Session,k.ShopVid,k.Seller,Guid.NewGuid().ToString("N"),k.ListingKey,DateTimeOffset.UtcNow.AddMinutes(-40).ToString("O"),result,"request-and-shop",result=="Preparing"?"shop":"listing","fixture","offline-test",true,false);}
        fakeStore.SavePurchaseCheck(Known(0,"Preparing"));fakeStore.SavePurchaseCheck(Known(2,"Preparing"));
        var fakeBridge=new Bridge();fakeBridge.Preparing.Add("Zulu");fakeBridge.Preparing.Add("Yankee");
        using var recheck=new ScannerPurchaseChecks(fakeStore,fakeBridge,5000,Path.Combine(directory,"fake-recheck-ledger","budget.json")){Notify=messages.Add};
        recheck.StartCycle();
        var pending=recheck.Pending();
        Check(pending.Count==3&&pending.Where(p=>p.Seller=="Zulu").Select(p=>p.Yang).SequenceEqual([9000u,300u])&&pending.Single(p=>p.Seller=="Yankee").Won==3&&pending.All(p=>p.Category.Job==0&&p.Category.Category==2&&p.Category.Sub==0),"Bilinen FAKE satıcıların aktif ilanları aday olarak çıkar: ödenemeyen/pahalı önce, kategori ilanın aktif kapsamından gelir");
        TradePage Results(params (string Seller,uint Vid,uint Yang,uint Won)[] items)=>new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,11,1,(uint)items.Length,items.SelectMany(r=>Record(r.Seller,r.Vid,r.Yang,r.Won)).ToArray());
        var zulu9000=pending.First(p=>p.Seller=="Zulu"&&p.Yang==9000);var zulu300=pending.First(p=>p.Seller=="Zulu"&&p.Yang==300);
        var none=Results(("Other",300u,9000u,0u));fakeBridge.Items=none.Items;
        Check(!recheck.Verify(zulu9000,none,CancellationToken.None).GetAwaiter().GetResult()&&fakeBridge.Sent.Count==0,"Fiyat aramasında ilan görünmüyorsa istek gönderilmez; sonraki aday denenir");
        var payable=Results(("Zulu",201u,300u,0u));fakeBridge.Items=payable.Items;
        Check(!recheck.Verify(zulu300,payable,CancellationToken.None).GetAwaiter().GetResult()&&fakeBridge.Sent.Count==0,"Bulunan ilan bütçeyle ödenebiliyorsa (300 < 5000 Yang) istek gönderilmez");
        fakeStore.Execute("INSERT INTO compact_presence VALUES(?,?,?,?) ON CONFLICT(key) DO UPDATE SET session=excluded.session",zulu9000.ListingKey,"old-session",201L,stamp);
        var found=Results(("Other",300u,50u,0u),("Zulu",201u,9000u,0u));fakeBridge.Items=found.Items;
        var settled=recheck.Verify(zulu9000,found,CancellationToken.None).GetAwaiter().GetResult();
        Check(settled&&fakeBridge.Sent.SequenceEqual([("Zulu",1)])&&fakeStore.LastConclusiveCheck("Zulu",201) is {Result:"Preparing"},"Fiyat aramasında bulunan ödenemeyen ilan doğru sonuç slotundan denenir ve sonuç satıcıya kaydedilir");
        Check((string)fakeStore.Query("SELECT session FROM compact_presence WHERE key=?",zulu9000.ListingKey)[0][0]! == fakeStore.SessionId,"Eski oturumdaki ilanın varlık kaydı yenilenir; böylece sonuç kaydı doğrulamadan geçer");
        Check(recheck.Pending().Select(p=>p.Seller).Distinct().SequenceEqual(["Yankee"]),"Aynı geçişte kontrol edilen FAKE satıcı tekrar aday olmaz");
        recheck.StartCycle();
        Check(recheck.Pending().Select(p=>p.Seller).Distinct().SequenceEqual(["Zulu"]),"Sonraki geçişte bulunamayan FAKE satıcı (Yankee) 10 dk geri çekilir; bulunan satıcı yine denenir");

        using var moved=new LocalStore(Path.Combine(directory,"fake-vid-change"));moved.ConfigureServer("Safir, CH1");
        var movedRows=new[]{("Zulu",201u,9000u,0u),("Zulu",301u,7000u,0u)};
        var movedPage=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,9,1,2,movedRows.SelectMany(r=>Record(r.Item1,r.Item2,r.Item3,r.Item4)).ToArray()){Result=new PageSaveResult()};
        moved.SaveTrade(movedPage);
        for(var slot=0;slot<2;slot++)
        {
            var known=moved.PurchaseTargetFor(movedPage,slot)!;
            moved.Execute("INSERT OR REPLACE INTO listing_lifecycle VALUES(?,1,?,NULL,'Observed',(SELECT MAX(id) FROM compact_versions WHERE key=?))",known.ListingKey,stamp,known.ListingKey);
            moved.Execute("INSERT OR REPLACE INTO scope_memberships VALUES(?,?,?,?,0,1)","ch1:0:2:0",known.ListingKey,stamp,"run");
            moved.Execute("UPDATE compact_presence SET last_seen=? WHERE key=?",slot==1?DateTimeOffset.UtcNow.ToString("O"):DateTimeOffset.UtcNow.AddHours(-2).ToString("O"),known.ListingKey);
        }
        var oldShop=moved.PurchaseTargetFor(movedPage,0)!;
        moved.SavePurchaseCheck(new PurchaseCheckRecord(1,oldShop.Server,oldShop.Session,oldShop.ShopVid,oldShop.Seller,Guid.NewGuid().ToString("N"),oldShop.ListingKey,DateTimeOffset.UtcNow.AddHours(-3).ToString("O"),"Preparing","request-and-shop","shop","fixture","offline-test",true,false));
        Check(moved.SellerKnownFake("Zulu")&&moved.KnownFakeShops(10).Select(k=>k.Seller).SequenceEqual(["Zulu"]),"FAKE durumu satıcıya aittir: dükkan numarası değişse de satıcı bilinen FAKE kalır");
        using var movedChecks=new ScannerPurchaseChecks(moved,new Bridge(),5000,Path.Combine(directory,"fake-vid-ledger","budget.json"));movedChecks.StartCycle();
        var movedPending=movedChecks.Pending();
        Check(movedPending.Count==2&&movedPending[0].ShopVid==301&&movedPending[0].Yang==7000,"Satıcı yeni dükkan numarasıyla açıldıysa en son görülen dükkanın ilanı önce aranır, arama o dükkan numarasıyla doğrulanır");
        using(var fullStore=new LocalStore(Path.Combine(directory,"full-price-probe"))){
            fullStore.ConfigureServer("Safir, CH1");
            var fullPage=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,1,1,1,Record("RecentClean",991,9000)){Result=new PageSaveResult()};
            fullStore.SaveTrade(fullPage);var anchor=fullStore.PurchaseTargetFor(fullPage,0)!;
            fullStore.SavePurchaseCheck(new(1,anchor.Server,anchor.Session,anchor.ShopVid,anchor.Seller,Guid.NewGuid().ToString("N"),anchor.ListingKey,DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),"NotEnoughMoney","request-and-shop","listing","fixture","offline-test",true,false));
            fullPage.Result!.ChangedSlots.Clear();var fullBridge=new Bridge{Wallet=0,Items=fullPage.Items};
            using var fullChecks=new ScannerPurchaseChecks(fullStore,fullBridge,0);
            fullChecks.StartCycle();fullChecks.Page(fullPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
            Check(fullBridge.Sent.Count==0,"Hızlı mod yakın zamanda temiz bulunan değişmeyen satıcıyı tekrar kontrol etmez");
            fullChecks.Page(fullPage with{FullPriceProbe=true},_=>{},CancellationToken.None).GetAwaiter().GetResult();
            Check(fullBridge.Sent.Count==1,"Tam turun ilk iki fiyat sayfasında yakın zamanda temiz bulunan satıcı da tekrar kontrol edilir");
            fullChecks.Page(fullPage with{FullPriceProbe=true},_=>{},CancellationToken.None).GetAwaiter().GetResult();
            Check(fullBridge.Sent.Count==1,"Tam turda aynı satıcı diğer sayfa/kategoride tekrar denenmez");
        }
    }
}
