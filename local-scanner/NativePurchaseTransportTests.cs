using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class NativePurchaseTransportTests
{
    private sealed class Bridge: ITradeBridge
    {
        internal readonly List<string> Commands=[];
        internal Func<string,JsonElement> Reply= _=>JsonSerializer.SerializeToElement(new{});
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(command);return Task.FromResult(Reply(command));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    internal static void Run(string directory,List<string> results)
    {
        void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);results.Add("PASS: "+label);}
        var schedule=new PurchaseSendSchedule();
        Check(schedule.WaitAt(TimeSpan.Zero)==TimeSpan.Zero,"İlk satın alma için gereksiz per-satıcı bekleme eklenmez");
        schedule.SentAt(TimeSpan.Zero);schedule.CompletedAt(TimeSpan.FromMilliseconds(250));
        Check(schedule.WaitAt(TimeSpan.FromMilliseconds(750))==TimeSpan.FromMilliseconds(250),"Yanıt ve sessizlik süresi aynı gönderim aralığına sayılır; beklemeler üst üste eklenmez");
        Check(schedule.WaitAt(TimeSpan.FromSeconds(2))==TimeSpan.Zero,"Sayfa okuma/kayıt süresi aralığı doldurduysa ek bekleme yapılmaz");
        schedule.SentAt(TimeSpan.FromSeconds(2));schedule.CompletedAt(TimeSpan.FromSeconds(4));
        Check(schedule.WaitAt(TimeSpan.FromSeconds(4))==TimeSpan.FromMilliseconds(500),"Yavaş yanıttan sonra native bağlamın sessizlik süresi korunur");
        var target=new PurchaseTarget("safir","session",22,"Seller","key","nonce",0,1000,new string('0',374),7,0);
        var row=new Dictionary<string,object?>{
            ["nonce"]=target.RequestId,["raw"]=target.RawHex,["seller"]=target.Seller,["shopVid"]=target.ShopVid,
            ["yang"]=target.Yang,["won"]=target.Won,["serverInfo"]="Safir, CH1",["sendingSupported"]=true,
            ["wallet"]=new{walletVerified=true,yang=0,won=0,serverInfo="Safir, CH1"}};
        JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
        var code="NOT_ENOUGH_MONEY";var verified=true;var pollNonce=target.RequestId;var ackNonce=target.RequestId;var ackCount=1;
        var bridge=new Bridge();
        bridge.Reply=command=>command.StartsWith("purchaseinspect ")?Json(row):
            command.StartsWith("purchasebegin ")?Json(new{sent=true,nonce=ackNonce,sendCount=ackCount}):
            command=="purchasepoll"?Json(new{nonce=pollNonce,code,text="fixture response",requestAndShopVerified=verified}):Json(new{ready=true,completed=true});
        var transport=new NativePurchaseTransport(bridge,new PurchaseCheckBudget(126588));
        var saved=new List<PurchaseCheckRecord>();
        PurchaseCheckCycle Cycle()=>new(transport,saved.Add,(_,_)=>Task.CompletedTask);
        PurchaseCheckRecord? Run(PurchaseCheckCycle cycle)=>cycle.Check(target,CancellationToken.None).GetAwaiter().GetResult();
        int Begins()=>bridge.Commands.Count(c=>c.StartsWith("purchasebegin "));
        int Nexts()=>bridge.Commands.Count(c=>c.StartsWith("purchasenext "));

        row["wallet"]=new{walletVerified=false,reason="Identity missing"};var unverified=Run(Cycle());
        Check(unverified is {Sent:false,StopAll:true}&&Begins()==0,"Native transport eksik alanlı doğrulanmamış wallet yanıtını göndermeden reddeder");
        row["wallet"]=new{walletVerified=true,yang=0,won=0,serverInfo="Safir, CH1"};
        var normalReply=bridge.Reply;var readyReads=0;
        bridge.Reply=command=>{
            if(command.StartsWith("purchaseinspect ")&&readyReads++==0){var quiet=new Dictionary<string,object?>(row){["sendingSupported"]=false};return Json(quiet);}
            return normalReply(command);
        };
        var becameReady=transport.Inspect(target,CancellationToken.None).GetAwaiter().GetResult();
        Check(becameReady.SendingSupported&&readyReads==2&&Begins()==0,"İstemci saatinin sessizlik sınırında yalnız inceleme tekrarlanır; hazır olmadan satın alma gönderilmez");
        bridge.Reply=normalReply;
        foreach(var (field,bad) in new (string,object)[]{("raw","ff"+target.RawHex[2..]),("nonce","other"),("serverInfo","Marmara, CH1"),("shopVid",23u),("seller","Other"),("yang",1001u)}){
            var old=row[field];row[field]=bad;var rejected=Run(Cycle());
            Check(rejected is {Sent:false,StopAll:true}&&Begins()==0,"Native transport hedef uyuşmazlığında göndermez: "+field);row[field]=old;
        }
        var terminal=Run(Cycle());
        Check(terminal is {Result:"NotEnoughMoney",Sent:true,StopAll:false}&&Begins()==1&&Nexts()==1&&bridge.Commands.Count(c=>c=="purchasepoll")==1,"Native transport tek begin/poll ve bağlı terminal için tek next gönderir");
        Check(bridge.Commands.Count(c=>c=="purchasestart")==1,"Native transport monitörünü Inspect çağrıları arasında tekrar başlatmaz");

        bridge.Commands.Clear();ackNonce="wrong";var ackCycle=Cycle();Run(ackCycle);Run(ackCycle);
        Check(Begins()==1&&bridge.Commands.Count(c=>c=="purchasepoll")==0&&Nexts()==0&&ackCycle.Stopped,"Yanlış begin nonce yanıtında poll/retry yapılmaz");ackNonce=target.RequestId;
        bridge.Commands.Clear();ackCount=2;var countCycle=Cycle();Run(countCycle);
        Check(Begins()==1&&Nexts()==0&&countCycle.Stopped,"Birden fazla gönderim ACK'si bütün denemeleri durdurur");ackCount=1;
        bridge.Commands.Clear();pollNonce="wrong";var nonceCycle=Cycle();Run(nonceCycle);Run(nonceCycle);
        Check(Begins()==1&&bridge.Commands.Count(c=>c=="purchasepoll")==1&&Nexts()==0&&nonceCycle.Stopped,"Yanlış poll nonce yanıtında next veya yeniden gönderim yok");pollNonce=target.RequestId;

        foreach(var responseCode in new[]{"PREMIUM_PREPARING","NOT_ENOUGH_MONEY","NOT_ENOUGH_MONEY_EX"}){
            bridge.Commands.Clear();code=responseCode;verified=false;var rejected=Run(Cycle());
            Check(Nexts()==0&&rejected?.Correlation=="time-only","Bağlı olmayan terminal next göndermez: "+responseCode);
        }
        bridge.Commands.Clear();code="PREMIUM_PREPARING";verified=true;var preparing=Run(Cycle());
        Check(preparing is {Result:"Preparing",Scope:"shop"}&&Begins()==1&&Nexts()==1,"Bağlı premium hazırlık sonucu shop kapsamıyla ilerler");
        bridge.Commands.Clear();code="UNKNOWN";var unknownCycle=Cycle();Run(unknownCycle);
        Check(Begins()==1&&Nexts()==0&&unknownCycle.Stopped,"Bağlı Unknown sonucu bile next veya retry oluşturmaz");
        bridge.Commands.Clear();code="PENDING";var timeoutCycle=Cycle();Run(timeoutCycle);Run(timeoutCycle);
        Check(Begins()==1&&bridge.Commands.Count(c=>c=="purchasepoll")==32&&Nexts()==0&&timeoutCycle.Stopped,"32 Pending sonrası zaman aşımı tek gönderimde durur, retry yapmaz");

        using var store=new LocalStore(Path.Combine(directory,"purchase-transport-store"));store.ConfigureServer("Safir, CH1");
        var raw=new byte[187];BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(1),22);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(9),1000);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(13),1);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(17),189);Encoding.ASCII.GetBytes("Seller").CopyTo(raw,46);
        var page=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,new(0,2,0,"Silah"),7,1,1,raw);
        Check(store.PurchaseTargets(page).Length==0,"Kaydedilmemiş sayfadan satın alma hedefi üretilmez");store.SaveTrade(page);
        var targets=store.PurchaseTargets(page);var stored=targets.Single();
        Check(stored.Server=="safir"&&stored.Session==store.SessionId&&stored.ShopVid==22&&stored.Yang==1000&&stored.Generation==7&&stored.RawHex==Convert.ToHexString(raw).ToLowerInvariant()&&store.Query("SELECT 1 FROM compact_presence WHERE key=? AND session=? AND vid=?",stored.ListingKey,store.SessionId,22).Count==1,"Commit edilen sayfa/slot hedefi kalıcı ilan anahtarı ve ham kayıtla eşleşir");
        var proof=new PurchaseCheckRecord(1,stored.Server,stored.Session,stored.ShopVid,stored.Seller,stored.RequestId,stored.ListingKey,DateTimeOffset.UtcNow.ToString("O"),"Preparing","request-and-shop","shop","fixture","offline-test",true,false);
        Check(store.SavePurchaseCheck(proof)&&!store.SavePurchaseCheck(proof),"Doğrulanmış Preparing kaydı kalıcı saklanır; requestId tekrarı idempotenttir");
        try{store.SavePurchaseCheck(proof with{RequestId="unbound",Correlation="time-only"});throw new Exception("Unbound Preparing accepted");}catch(InvalidDataException){Check(true,"Zaman eşleşmeli Preparing veritabanına kesin kanıt olarak kaydedilmez");}
        var next=page with{Id=Guid.NewGuid().ToString("N"),Generation=8};store.SaveTrade(next);
        Check(store.PurchaseTargets(page).Length==0&&store.PurchaseTargets(next).Single().Generation==8,"Yeni commit sonrası eski sayfanın hedef anahtarı kullanılamaz");
        store.Execute("CREATE TRIGGER purchase_test_fail BEFORE INSERT ON compact_pages BEGIN SELECT RAISE(ABORT,'offline purchase rollback'); END");
        var failed=next with{Id=Guid.NewGuid().ToString("N"),Generation=9};
        try{store.SaveTrade(failed);throw new Exception("Rollback fixture committed");}catch(IOException){}
        Check(store.PurchaseTargets(failed).Length==0&&store.PurchaseTargets(next).Length==1,"Başarısız transaction sayfa/slot satın alma bağlamını değiştirmez");

        var checkpointBridge=new Bridge();
        checkpointBridge.Reply=command=>command.StartsWith("purchasebegin ")?Json(new{sent=true,nonce=target.RequestId,sendCount=1}):Json(new{nonce=target.RequestId,code="UNKNOWN",text="fixture",requestAndShopVerified=false});
        var checkpointTransport=new NativePurchaseTransport(checkpointBridge,new(126588),_=>checkpointBridge.Commands.Add("checkpoint"));
        checkpointTransport.SendOnce(target,CancellationToken.None).GetAwaiter().GetResult();
        Check(checkpointBridge.Commands[0]=="checkpoint"&&checkpointBridge.Commands[1].StartsWith("purchasebegin "),"Kalıcı gönderim checkpoint'i begin komutundan önce çalışır");
        checkpointBridge.Commands.Clear();var failedCheckpoint=new NativePurchaseTransport(checkpointBridge,new(126588),_=>throw new IOException("fixture checkpoint failure"));
        try{failedCheckpoint.SendOnce(target,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Checkpoint failure sent");}catch(IOException){}
        Check(checkpointBridge.Commands.Count==0,"Checkpoint yazılamazsa hiçbir begin/poll komutu gönderilmez");

        store.Execute("DROP TRIGGER purchase_test_fail");
        var rawWon=raw.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(rawWon.AsSpan(9),0);BinaryPrimitives.WriteUInt32LittleEndian(rawWon.AsSpan(0x8b),2);
        var wonPage=new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,new(0,2,0,"Silah"),10,1,1,rawWon){Result=new PageSaveResult()};
        store.SaveTrade(wonPage);wonPage.Result!.ChangedSlots.Clear();wonPage.Result.ChangedSlots.Add(0);
        var ledgerDirectory=Path.Combine(directory,"purchase-ledgers");Directory.CreateDirectory(ledgerDirectory);
        string State(string name,object value){var path=Path.Combine(ledgerDirectory,name+".json");File.WriteAllText(path,JsonSerializer.Serialize(value));return path;}
        var unsent=proof with{RequestId="verified-unsent",Result="Unknown",Correlation="none",Scope="listing",Sent=false,StopAll=true};store.SavePurchaseCheck(unsent);
        foreach(var (resumeId,allowed) in new[]{("verified-unsent",true),(proof.RequestId,false),("missing-source-proof",false)}){
            var resumePath=State("resume-"+resumeId,new{schemaVersion=1,authorizedYang=126588,remainingYang=126588,halted=false,phase="VerifiedNoSend",requestId=resumeId});
            using var resumed=new ScannerPurchaseChecks(store,new Bridge(),126588,resumePath);
            Check(resumed.IsHalted!=allowed,"Gönderilmemiş kontrolü sürdürme kaynak veritabanındaki sent:false kanıtını gerektirir: "+resumeId);
        }
        foreach(var (haltId,resumes) in new[]{("verified-unsent",true),(proof.RequestId,false),("missing-source-proof",false)}){
            var haltPath=State("halt-unknown-"+haltId,new{schemaVersion=1,authorizedYang=126588,remainingYang=126588,halted=true,phase="Unknown",requestId=haltId});
            using var afterDrop=new ScannerPurchaseChecks(store,new Bridge(),126588,haltPath);
            Check(afterDrop.IsHalted!=resumes,"Gönderilmediği kayıtla kanıtlanmış belirsiz kilit yeniden girişte kendiliğinden kalkar, gönderilmiş olan kalmaz: "+haltId);
        }
        var droppedCycle=new PurchaseCheckCycle(new DroppedBeforeSend(),_=>throw new Exception("Gönderilmemiş kontrol kaydedilmemeli"),budget:new(126588),refuseAffordable:true);
        Check(droppedCycle.Check(target,CancellationToken.None).GetAwaiter().GetResult() is null&&!droppedCycle.Stopped,"Gönderimden önce istemci kopunca kontrol durmaz ve kayıt/kilit oluşmaz");
        foreach(var (name,state)in new (string,object)[]{
            ("wrong-budget",new{schemaVersion=1,authorizedYang=126587,remainingYang=1,halted=false,phase="Preparing"}),
            ("negative",new{schemaVersion=1,authorizedYang=126588,remainingYang=-1,halted=false,phase="Preparing"}),
            ("excess",new{schemaVersion=1,authorizedYang=126588,remainingYang=126589,halted=false,phase="Preparing"}),
            ("wrong-schema",new{schemaVersion=2,authorizedYang=126588,remainingYang=126588,halted=false,phase="Preparing"})}){
            var invalidPath=State(name,state);
            try{_ = new ScannerPurchaseChecks(store,new Bridge(),126588,invalidPath);throw new Exception("Invalid ledger accepted: "+name);}catch(InvalidDataException){Check(true,"Kalıcı bütçe/şema geçersizse devam edilmez: "+name);}
        }
        foreach(var phase in new[]{"Sending","Unknown","Success","Unexpected"}){
            var haltedPath=State("halt-"+phase,new{schemaVersion=1,authorizedYang=126588,remainingYang=126588,halted=false,phase});
            var noCommands=new Bridge{Reply=_=>throw new Exception("Halted ledger called bridge")};
            using var haltedScanner=new ScannerPurchaseChecks(store,noCommands,126588,haltedPath);haltedScanner.StartCycle();haltedScanner.Page(wonPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
            Check(noCommands.Commands.Count==0,"Kalıcı belirsiz/başarılı aşama halted=false olsa bile yeniden başlamaz: "+phase);
        }
        var explicitHalt=State("explicit-halt",new{schemaVersion=1,authorizedYang=126588,remainingYang=126588,halted=true,phase="Preparing"});
        var explicitBridge=new Bridge{Reply=_=>throw new Exception("Explicitly halted ledger called bridge")};
        using var explicitScanner=new ScannerPurchaseChecks(store,explicitBridge,126588,explicitHalt);explicitScanner.StartCycle();explicitScanner.Page(wonPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
        Check(explicitBridge.Commands.Count==0,"Açık halted kaydı bilinen Preparing aşamasında da gönderimi engeller");

        var readyPath=Path.Combine(ledgerDirectory,"classified-ready.json");var ledgerNonce="";var sawSending=false;
        var ledgerBridge=new Bridge();
        ledgerBridge.Reply=command=>{
            if(command.StartsWith("purchaseinspect ")){
                ledgerNonce=command.Split(' ')[3];
                return Json(new{nonce=ledgerNonce,raw=Convert.ToHexString(rawWon).ToLowerInvariant(),seller="Seller",shopVid=22,yang=0,won=2,serverInfo="Safir, CH1",sendingSupported=true,wallet=new{walletVerified=true,yang=126588,won=0,serverInfo="Safir, CH1"}});
            }
            if(command.StartsWith("purchasebegin ")){
                using var checkpoint=JsonDocument.Parse(File.ReadAllText(readyPath));var entry=checkpoint.RootElement;
                sawSending=entry.GetProperty("halted").GetBoolean()&&entry.GetProperty("phase").GetString()=="Sending"&&entry.GetProperty("requestId").GetString()==ledgerNonce;
                return Json(new{sent=true,nonce=ledgerNonce,sendCount=1});
            }
            return command=="purchasepoll"?Json(new{nonce=ledgerNonce,code="PREMIUM_PREPARING",text="fixture native INFO",requestAndShopVerified=true}):Json(new{ready=true,completed=true});
        };
        using var readyScanner=new ScannerPurchaseChecks(store,ledgerBridge,126588,readyPath);readyScanner.StartCycle();readyScanner.Page(wonPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
        using(var durable=JsonDocument.Parse(File.ReadAllText(readyPath))){var state=durable.RootElement;
            Check(sawSending&&!state.GetProperty("halted").GetBoolean()&&state.GetProperty("phase").GetString()=="Preparing"&&state.GetProperty("remainingYang").GetInt64()==126588&&store.Query("SELECT 1 FROM purchase_checks WHERE json_extract(payload_json,'$.requestId')=?",ledgerNonce).Count==1,"Doğrulanmış harcamasız Preparing önce kalıcı kayda, sonra devam edebilir ledger'a yazılır");}
        Check(!File.Exists(readyPath+".pending"),"Başarılı ledger atomik taşıma sonrası geçici dosya bırakmaz");

        var pendingPath=Path.Combine(ledgerDirectory,"interrupted-sending.json");var pendingBridge=new Bridge();
        pendingBridge.Reply=command=>{
            if(command.StartsWith("purchasebegin "))throw new InterruptedFixture();
            return ledgerBridge.Reply(command);
        };
        using var interrupted=new ScannerPurchaseChecks(store,pendingBridge,126588,pendingPath);interrupted.StartCycle();
        try{interrupted.Page(wonPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Interrupted fixture completed");}catch(InterruptedFixture){}
        using(var pending=JsonDocument.Parse(File.ReadAllText(pendingPath)))Check(pending.RootElement.GetProperty("halted").GetBoolean()&&pending.RootElement.GetProperty("phase").GetString()=="Sending","Gönderim sırasında süreç kesilirse Sending checkpoint'i kalır");
        interrupted.Dispose();
        var restartBridge=new Bridge{Reply=_=>throw new Exception("Interrupted purchase retried")};
        using var restarted=new ScannerPurchaseChecks(store,restartBridge,126588,pendingPath);restarted.StartCycle();restarted.Page(wonPage,_=>{},CancellationToken.None).GetAwaiter().GetResult();
        Check(restartBridge.Commands.Count==0,"Sending checkpoint'inden yeniden açılış Inspect veya ikinci buy başlatmaz");
        var leasePath=Path.Combine(ledgerDirectory,"single-process.json");var leaseBridge=new Bridge();
        using(var leaseOwner=new ScannerPurchaseChecks(store,leaseBridge,126588,leasePath)){
            try{using var duplicate=new ScannerPurchaseChecks(store,leaseBridge,126588,leasePath);throw new Exception("Concurrent budget lease accepted");}catch(IOException){Check(leaseBridge.Commands.Count==0,"Aynı bütçe ledger'ını ikinci süreç/koordinatör gönderim yapmadan reddeder");}
        }
        using(var released=new ScannerPurchaseChecks(store,leaseBridge,126588,leasePath))Check(leaseBridge.Commands.Count==0,"Dispose sonrası aynı bütçe lease'i yeniden alınabilir; constructor gönderim yapmaz");
    }
    private sealed class InterruptedFixture:Exception;
    private sealed class DroppedBeforeSend:IPurchaseCheckTransport
    {
        public Task<PurchasePreflight> Inspect(PurchaseTarget target,CancellationToken token)=>throw new IOException("Istemci penceresine mesaj gonderilemedi (Windows 1400)");
        public Task<PurchaseResponse> SendOnce(PurchaseTarget target,CancellationToken token)=>throw new Exception("Gönderim çağrılmamalı");
    }
}
