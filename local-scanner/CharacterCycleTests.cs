using System.Text.Json;

namespace Metin2Search.Local;

internal static class CharacterCycleTests
{
    private sealed class Game : ITradeBridge
    {
        public int Slot;
        public string[] Names=["C0","C1","C2","C3","C4"];
        public int[] RawOrder=[0,1,2,3,4],DisplayOrder=[0,1,2,3,4];
        public bool ReorderOnEnter;
        public int IdentityWarmupReads;
        public List<int> EnteredRawSlots=[];
        public string Phase="game",Server="Safir, CH1";
        public bool StorageOpen,GlassOpen=true,DropDeposit,DropWithdraw,CrashDeposit,CrashWithdraw,DelayLogout,FullStorage,FailPrepare,KeepStorageOpen;
        public bool PasswordSent;
        public bool FailTarget,LoginTarget=true;
        public Func<DateTimeOffset> Clock=()=>DateTimeOffset.UtcNow;
        public List<(string Name,DateTimeOffset At)> Timeline=[];
        public uint Stored;
        public TimeSpan StorageDelay; public DateTimeOffset StorageRevealAt; public bool MissingAfterLogin,StorageInvisible;
        public bool UnsortedZeroSlot;
        public int LastDepositSlot=-1,LastWithdrawSlot=-1;
        public Dictionary<int,uint> Glass=new(){{0,60005},{1,0},{2,0},{3,0},{4,0}};
        public List<string> Commands=[];
        public int DepositSends,WithdrawSends,LoginSends;
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(command.Split(' ')[0]);Timeline.Add((command.Split(' ')[0],Clock()));var args=command.Split(' ');object result=new{ok=true};var shownStored=StorageInvisible||Clock()<StorageRevealAt?0u:Stored;
            if(command is "storageopen" or "storagewithdraw" or "storagedeposit" or "navgrid"&&GlassOpen)throw new IOException("Trade glass still open");
            if(command=="characterlist")result=new{roster=RawOrder.Select(i=>Names[i]).ToArray(),displaySlots=DisplayOrder.Select(i=>Array.IndexOf(RawOrder,i)).ToArray()};
            else if(command=="rotationstatus")result=new{serverInfo=Server,characterSlot=Array.IndexOf(RawOrder,Slot),character=Names[Slot],roster=RawOrder.Select(i=>Names[i]).ToArray(),displaySlots=DisplayOrder.Select(i=>Array.IndexOf(RawOrder,i)).ToArray(),inventory=new[]{new uint[]{2,Glass[Slot],Glass[Slot]>0?1u:0u},new uint[]{3,0,0}},storageOpen=StorageOpen,storage=StorageOpen?(FullStorage?new[]{new uint[]{7,123,1},new uint[]{8,124,1}}:UnsortedZeroSlot?new[]{new uint[]{8,0,0},new uint[]{0,shownStored,shownStored>0?1u:0u}}:new[]{new uint[]{7,shownStored,shownStored>0?1u:0u},new uint[]{8,0,0}}):Array.Empty<uint[]>()};
            else if(command.StartsWith("loginprepare ")){LoginTarget=!FailTarget;result=new{ok=LoginTarget,reason="Test target missing"};}
            else if(command=="rotationprepare"){if(!FailPrepare)GlassOpen=false;if(!StorageOpen)PasswordSent=false;result=new{prepared=!FailPrepare,glassClosed=!GlassOpen,shopClosed=true};}
            else if(command=="storageopen")result=new{open=StorageOpen,passwordReady=!PasswordSent};
            else if(command.StartsWith("storagepassword ")){PasswordSent=true;StorageOpen=true;result=new{submitted=true};}
            else if(command.StartsWith("storagedeposit ")){LastDepositSlot=int.Parse(args[2]);DepositSends++;if(!DropDeposit){Stored=60005;Glass[Slot]=0;}if(CrashDeposit)throw new IOException("process exited");}
            else if(command.StartsWith("storagewithdraw ")){LastWithdrawSlot=int.Parse(args[2]);WithdrawSends++;if(!DropWithdraw){Stored=0;Glass[Slot]=60005;}if(CrashWithdraw)throw new IOException("process exited");}
            else if(command=="storageclose"){if(!KeepStorageOpen)StorageOpen=false;result=new{closed=!StorageOpen};}
            else if(command=="rotationlogout"){if(!DelayLogout)Phase="select";}
            else if(command=="loginconnect")Phase="select";
            else if(command.StartsWith("loginenter ")){var raw=int.Parse(args[1]);EnteredRawSlots.Add(raw);Slot=RawOrder[raw];if(ReorderOnEnter){RawOrder=[Slot,..RawOrder.Where(i=>i!=Slot)];DisplayOrder=[Slot,..DisplayOrder.Where(i=>i!=Slot)];}Phase="game";StorageOpen=false;GlassOpen=false;LoginSends++;if(Stored>0){StorageRevealAt=Clock()+StorageDelay;StorageInvisible=MissingAfterLogin;}}
            else if(command=="useglass"){if(Glass[Slot]!=60005)throw new TradeQueryException("no glass");GlassOpen=true;}
            else if(command=="gamestate")result=new{phase=Phase,playerReady=Phase=="game",inventoryReady=Phase=="game",glassOpen=GlassOpen&&Phase=="game",glassSlot=Glass[Slot]>0?2:-1,serverInfo=Server,channel=1,loginTarget=LoginTarget,character=IdentityWarmupReads-->0?"":Names[Slot],characterSlot=Array.IndexOf(RawOrder,Slot)};
            else if(command is "navgrid" or "navinfo")result=new{x=1000,y=1000,npcX=1100,npcY=1000,npcVid=9005,side=3,cell=100,originX=900,originY=900,grid="000000000"};
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Launcher : IClientLauncher
    {
        public int Clicks,Polls;public bool Ambiguous,NeverReady,KeepExisting;
        public IReadOnlyList<GameClientIdentity> Clients(){Polls++;return Clicks==0?[new(9,1,"other",true)]:Ambiguous?[new(10,2,"game",true),new(11,3,"game",true)]:NeverReady?[]:KeepExisting?[new(9,1,"other",true),new(10,2,"game",true)]:[new(10,2,"game",true)];}
        public void ClickMetin2Play(CancellationToken token){token.ThrowIfCancellationRequested();Clicks++;}
    }
    private sealed class Pages : ITradeBridge
    {
        public int Page=1;
        public Task<JsonElement> Command(string command,CancellationToken token)
        {
            object value;
            if(command=="inspect")value=new{generation=0,sort=new{recentIndex=2,unitPriceIndex=1},plan=new[]{new{job=0,category=2,sub=3,label="Silah"}}};
            else if(command.StartsWith("setsort "))value=new{sort=new{selected=int.Parse(command[8..])}};
            else if(command.StartsWith("search ")){Page=1;value=new{generation=0};}
            else if(command.StartsWith("page ")){var before=Page;Page=int.Parse(command.Split(' ')[1]);value=new{generation=before};}
            else value=new{generation=Page,page=Page,total=22,count=11,raw=new string('0',11*187*2),details=Array.Empty<object>()};
            return Task.FromResult(JsonSerializer.SerializeToElement(value));
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    public static void Run(string root,List<string> results)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add("PASS: "+name);}
        void Reject(Action action,string name){var rejected=false;try{action();}catch(Exception error)when(error is InvalidDataException or CycleManualInterventionException or IOException or OperationCanceledException){rejected=true;}Check(rejected,name);}
        var folder=Path.Combine(root,"character-cycle");Directory.CreateDirectory(folder);var now=DateTimeOffset.Parse("2026-10-18T08:00:00Z");
        Task Wait(TimeSpan span,CancellationToken token){token.ThrowIfCancellationRequested();now+=span;return Task.CompletedTask;}
        var options=CharacterCycleOptions.Parse("120","1,3,5,2,4",0);
        Check(options.Minutes==120&&options.Order.SequenceEqual(new[]{0,2,4,1,3}),"Karakter süresi dakika ve sırası GUI yuvalarından çözülür");
        Reject(()=>CharacterCycleOptions.Parse("0","1,2",0),"Sıfır dakika reddedilir");
        Reject(()=>CharacterCycleOptions.Parse("120","1,1,2",0),"Tekrarlı karakter sırası reddedilir");
        Reject(()=>CharacterCycleOptions.Parse("120","2,3",0),"Başlangıç karakterini içermeyen sıra reddedilir");
        var game=new Game{Clock=()=>now};var path=Path.Combine(folder,"cycle.json");var selected=0;
        CharacterCycle New(string file= "cycle.json")=>new(Path.Combine(folder,file),options,()=>"test-secret",_=>{},slot=>selected=slot,()=>now,Wait,TimeSpan.FromSeconds(1));
        var cycle=New();cycle.EnsureReady(game,CancellationToken.None).GetAwaiter().GetResult();
        Check(cycle.LoginSlot==0&&cycle.ForceFull&&!cycle.IsDue,"İlk manuel karakterde süre ve zorunlu tam tur başlar");
        Check(!File.ReadAllText(path).Contains("test-secret"),"Depo şifresi kalıcı devir günlüğüne yazılmaz");
        cycle.FullTourCompleted();Check(!New().ForceFull,"Tam tur başarısı yeniden başlatmada korunur");
        now=now.AddMinutes(120);
        var switched=false;try{cycle.Checkpoint(game,CancellationToken.None).GetAwaiter().GetResult();}catch(CharacterCycleChangedException){switched=true;}
        Check(switched&&game.Slot==2&&selected==2&&game.Glass[2]==60005&&game.Stored==0&&game.GlassOpen,"Süre dolunca cam depo üzerinden sıradaki karaktere devredilir ve cam açılır");
        Check(game.DepositSends==1&&game.WithdrawSends==1&&game.LoginSends==1&&cycle.ForceFull,"Bir devir yalnız bir checkin/checkout/giriş ve yeni karakterde zorunlu tam tur üretir");
        Check(game.Commands.IndexOf("storagedeposit")<game.Commands.IndexOf("rotationlogout")&&game.Commands.IndexOf("loginenter")<game.Commands.IndexOf("storagewithdraw"),"Depo teyidi çıkıştan önce, cam alımı doğru karakter girişinden sonra gerçekleşir");
        Check(game.Commands.IndexOf("rotationprepare")<game.Commands.IndexOf("storageopen")&&game.Commands.IndexOf("storagepassword")<game.Commands.IndexOf("storagedeposit"),"Cam kapatma teyidi ve depo şifresi eşya aktarımından önce tamamlanır");
        Check(game.Timeline.First(e=>e.Name=="rotationlogout").At-game.Timeline.First(e=>e.Name=="storageclose").At>=TimeSpan.FromSeconds(11),"Depo kapatıldıktan sonra en az 11 saniye geçmeden çıkış istenmez");
        Check(game.Commands.LastIndexOf("storageclose")<game.Commands.LastIndexOf("useglass")&&!game.StorageOpen,"Yeni karakter camı aldıktan sonra depo kapanmadan cam açılmaz");
        var zeroGame=new Game{UnsortedZeroSlot=true};var zeroCycle=New("zero-slot.json");zeroCycle.EnsureReady(zeroGame,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        try{zeroCycle.Checkpoint(zeroGame,CancellationToken.None).GetAwaiter().GetResult();}catch(CharacterCycleChangedException){}
        Check(zeroGame.LastDepositSlot==0&&zeroGame.LastWithdrawSlot==0&&zeroGame.Glass[2]==60005,"Depo satırları sırasız gelse de ilk boş hücreye (slot 0) bırakılır ve aynı hücreden alınır");
        var targetMissing=new Game{FailTarget=true};var targetCycle=New("target-missing.json");targetCycle.EnsureReady(targetMissing,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>targetCycle.Checkpoint(targetMissing,CancellationToken.None).GetAwaiter().GetResult(),"Giriş hedefi çözülemezse devir cam bırakılmadan durur");
        Check(targetMissing.DepositSends==0&&!targetMissing.Commands.Contains("rotationprepare")&&targetMissing.Glass[0]==60005,"Sunucu doğrulaması cam/pazar kapanışı ve eşya aktarımından önce yapılır");
        var resumeGame=new Game{Phase="login",LoginTarget=false,GlassOpen=false,Stored=60005,UnsortedZeroSlot=true,RawOrder=[4,0,1,2,3]};resumeGame.Glass[0]=0;
        var resumeOptions=CharacterCycleOptions.Parse("120","1,2,3,4,5",0);var resumePath=Path.Combine(folder,"resume-selecting.json");
        File.WriteAllText(resumePath,JsonData.Encode(new CycleJournal{Server=resumeGame.Server,RosterHash=CycleSnapshot.Hash(resumeGame.Names),Characters=resumeGame.Names,Order=resumeOptions.Order,Phase="Selecting",CurrentSlot=0,NextSlot=1,InventorySlot=2,StorageSlot=0,StartedAt=now.AddHours(-10)}));
        var resumeCycle=new CharacterCycle(resumePath,resumeOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait);
        new SessionGuard(resumeGame,resumeGame.Server,()=>true,()=>resumeCycle.LoginSlot,delay:Wait,requireGlass:()=>false,resolveCharacter:resumeCycle.ResolveLoginSlot).RecoverAsync(_=>{},CancellationToken.None).GetAwaiter().GetResult();
        resumeCycle.EnsureReady(resumeGame,CancellationToken.None).GetAwaiter().GetResult();
        Check(resumeGame.Slot==1&&resumeGame.EnteredRawSlots.SequenceEqual(new[]{2})&&resumeGame.DepositSends==0&&resumeGame.WithdrawSends==1&&resumeGame.LastWithdrawSlot==0&&resumeCycle.ForceFull,"Selecting kaydı giriş hedefi kaybolsa da isimle 2. karakterin güncel yuvasına girer, camı slot 0'dan alır; tekrar bırakmaz");
        var resumedJournal=JsonData.Decode<CycleJournal>(File.ReadAllText(resumePath));
        Check(!resumeCycle.IsDue&&now-resumedJournal.StartedAt<TimeSpan.FromSeconds(10)&&resumedJournal.InventorySlot==-1&&resumedJournal.StorageSlot==-1,"Geceden kalan devir tamamlanınca süre cam alınıp depo kapandıktan sonra yeniden başlar; önceki hücreler temizlenir");
        var previousCommands=resumeGame.Commands.Count;resumeCycle.Checkpoint(resumeGame,CancellationToken.None).GetAwaiter().GetResult();
        Check(resumeGame.Commands.Count==previousCommands,"Yeni karakterin ilk checkpoint'i eski saate bakıp tekrar depo açmaz");
        var timerRoot=Path.Combine(folder,"timer-evidence");var timerFile=Path.Combine(folder,"premature-timer.json");
        var timerGame=new Game{Slot=2,Clock=()=>now,GlassOpen=false};timerGame.Glass[0]=0;timerGame.Glass[2]=60005;
        var timerJournal=new CycleJournal{Server=timerGame.Server,RosterHash=CycleSnapshot.Hash(timerGame.Names),Characters=timerGame.Names,Order=resumeOptions.Order,Phase="Depositing",CurrentSlot=2,NextSlot=3,InventorySlot=2,StorageSlot=0,StartedAt=now.AddHours(-10),ForceFull=true};
        string TimerEvent(string phase)=>JsonData.Encode(new{Phase=phase,timerJournal.CurrentSlot,timerJournal.NextSlot,timerJournal.InventorySlot,timerJournal.StorageSlot,timerJournal.ForceFull});
        using(var evidenceStore=new LocalStore(ServerStorage.DirectoryFor(timerRoot,timerGame.Server))){
            evidenceStore.ConfigureServer(timerGame.Server);
            evidenceStore.Execute("INSERT INTO events(session_id,occurred,kind,message) VALUES(?,?,?,?)",evidenceStore.SessionId,now.AddMinutes(-1).ToString("O"),"CycleState",TimerEvent("Scanning"));
            evidenceStore.Execute("INSERT INTO events(session_id,occurred,kind,message) VALUES(?,?,?,?)",evidenceStore.SessionId,now.AddSeconds(-54).ToString("O"),"CycleState",TimerEvent("Depositing"));
            Check(CycleTimerRecovery.IsPremature(timerRoot,timerJournal,120),"Sunucuya ait olay kaydı altı saniyede eski saatle başlayan erken devri kanıtlar");
            timerJournal.Phase="DepositPending";Check(!CycleTimerRecovery.IsPremature(timerRoot,timerJournal,120),"Gönderim niyeti yazılmış depo aktarımı sayaç onarımıyla sıfırlanmaz");timerJournal.Phase="Depositing";
            File.WriteAllText(timerFile,JsonData.Encode(timerJournal));
            var timerCycle=new CharacterCycle(timerFile,resumeOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait,audit:(kind,message)=>evidenceStore.Event(kind,message),recoverPrematureDeposit:j=>CycleTimerRecovery.IsPremature(timerRoot,j,120));
            timerCycle.EnsureReady(timerGame,CancellationToken.None).GetAwaiter().GetResult();timerCycle.Checkpoint(timerGame,CancellationToken.None).GetAwaiter().GetResult();
            Check(!timerCycle.IsDue&&timerGame.Glass[2]==60005&&timerGame.GlassOpen&&timerGame.DepositSends==0&&!timerGame.Commands.Contains("storageopen")&&Directory.GetFiles(folder,"premature-timer.json.timer-before-*").Length==1,"Kanıtlı erken devir eldeki camla yedeklenip toparlanır; cam açılır, tekrar depo açılmaz veya eşya gönderilmez");
        }
        var delayedStorage=new Game{Clock=()=>now,StorageDelay=TimeSpan.FromSeconds(15)};var traces=new List<string>();
        var delayedCycle=new CharacterCycle(Path.Combine(folder,"delayed-storage.json"),options,()=>"test-secret",_=>{},_=>{},()=>now,Wait,audit:(kind,message)=>traces.Add(kind+message));
        delayedCycle.EnsureReady(delayedStorage,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        try{delayedCycle.Checkpoint(delayedStorage,CancellationToken.None).GetAwaiter().GetResult();}catch(CharacterCycleChangedException){}
        Check(delayedStorage.Glass[2]==60005&&delayedStorage.WithdrawSends==1&&delayedStorage.DepositSends==1,"Depo penceresi açılıp cam 15 saniye geç görünse de beklenir ve yalnız bir kez alınır");
        Check(traces.Any(t=>t.StartsWith("CycleState"))&&traces.Any(t=>t.StartsWith("CycleStorageRead"))&&traces.All(t=>!t.Contains("test-secret")&&!t.Contains("\"Characters\"")),"Devir aşaması ve depo okuma özeti parola/karakter listesi olmadan kalıcı olay kaydına verilir");
        var absentStorage=new Game{Clock=()=>now,MissingAfterLogin=true};var absentCycle=New("absent-storage.json");absentCycle.EnsureReady(absentStorage,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        var absentStart=now;Reject(()=>absentCycle.Checkpoint(absentStorage,CancellationToken.None).GetAwaiter().GetResult(),"Cam görünmezse tek okumayla kayıp denmez; sınırlı beklemeden sonra durulur");
        Check(now-absentStart>=TimeSpan.FromSeconds(30)&&absentStorage.WithdrawSends==0&&JsonData.Decode<CycleJournal>(File.ReadAllText(Path.Combine(folder,"absent-storage.json"))).Phase=="Withdrawing","Cam yokken alma isteği gönderilmez ve Withdrawing günlüğü korunur");
        absentStorage.StorageInvisible=false;absentStorage.MissingAfterLogin=false;New("absent-storage.json").EnsureReady(absentStorage,CancellationToken.None).GetAwaiter().GetResult();
        Check(absentStorage.Glass[2]==60005&&absentStorage.DepositSends==1&&absentStorage.WithdrawSends==1,"Cam yeniden görünürse korunan Withdrawing kaydı camı tekrar bırakmadan tek alma ile tamamlanır");

        foreach(var duringDeposit in new[]{true,false}){
            now=now.AddDays(1);var label=duringDeposit?"deposit-crash.json":"withdraw-crash.json";var g=new Game{CrashDeposit=duringDeposit,CrashWithdraw=!duringDeposit};var c=New(label);c.EnsureReady(g,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
            Reject(()=>c.Checkpoint(g,CancellationToken.None).GetAwaiter().GetResult(),"Devir ortasındaki istemci kapanması günlüğü tamamlandı diye işaretlemez "+label);
            g.CrashDeposit=false;g.CrashWithdraw=false;g.StorageOpen=false;
            var resumed=New(label);resumed.EnsureReady(g,CancellationToken.None).GetAwaiter().GetResult();
            Check(g.DepositSends==1&&g.WithdrawSends==1&&g.Slot==2&&g.Glass[2]==60005,"Kayıp aktarım yanıtı envanter/depo ile uzlaştırılır; istek tekrar edilmez "+label);
        }
        now=now.AddDays(1);var timeoutGame=new Game{DropDeposit=true};var timeout=New("timeout.json");timeout.EnsureReady(timeoutGame,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>timeout.Checkpoint(timeoutGame,CancellationToken.None).GetAwaiter().GetResult(),"Teyitsiz depo isteği zaman aşımında durur");
        Reject(()=>New("timeout.json").EnsureReady(timeoutGame,CancellationToken.None).GetAwaiter().GetResult(),"Teyitsiz depo isteği program yeniden açıldığında tekrar gönderilmez");
        Check(timeoutGame.DepositSends==1&&timeoutGame.LoginSends==0&&timeoutGame.Glass[0]==60005,"Cam bırakılmadan karakter çıkışı veya ikinci bırakma isteği yoktur");
        var boxFull=new Game{FullStorage=true};var fullCycle=New("full-storage.json");fullCycle.EnsureReady(boxFull,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>fullCycle.Checkpoint(boxFull,CancellationToken.None).GetAwaiter().GetResult(),"Dolu depoda başka eşyaya dokunmadan durulur");Check(boxFull.DepositSends==0&&boxFull.LoginSends==0,"Dolu depo çıkış veya eşya bırakma isteği üretmez");
        var noLogout=new Game{DelayLogout=true};var pendingLogout=New("logout.json");pendingLogout.EnsureReady(noLogout,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>pendingLogout.Checkpoint(noLogout,CancellationToken.None).GetAwaiter().GetResult(),"Çıkış gerçekleşmeden eski karakter yeni giriş sanılmaz");Check(noLogout.LoginSends==0&&noLogout.WithdrawSends==0,"Geciken çıkışta yanlış karaktere cam aldırılmaz");
        var noWithdraw=new Game{DropWithdraw=true};var pendingWithdrawal=New("withdraw-timeout.json");pendingWithdrawal.EnsureReady(noWithdraw,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>pendingWithdrawal.Checkpoint(noWithdraw,CancellationToken.None).GetAwaiter().GetResult(),"Teyitsiz cam alma isteği taramayı başlatmaz");
        Reject(()=>New("withdraw-timeout.json").EnsureReady(noWithdraw,CancellationToken.None).GetAwaiter().GetResult(),"Teyitsiz cam alma isteği yeniden gönderilmez");Check(noWithdraw.WithdrawSends==1&&noWithdraw.Stored==60005,"Alma yanıtı belirsizken cam depoda tutulur");
        var wrong=new Game{Slot=1};Reject(()=>New("wrong.json").EnsureReady(wrong,CancellationToken.None).GetAwaiter().GetResult(),"Yanlış manuel karakterde devir başlamaz");
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();Reject(()=>New("cancel.json").EnsureReady(new Game(),canceled.Token).GetAwaiter().GetResult(),"Durdur karakter işlemlerini iptal eder");}
        var prepareFailed=new Game{FailPrepare=true};var failPreparation=New("prepare-failed.json");failPreparation.EnsureReady(prepareFailed,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>failPreparation.Checkpoint(prepareFailed,CancellationToken.None).GetAwaiter().GetResult(),"Ticaret Camı kapanmazsa depo işlemleri başlamaz");
        Check(!prepareFailed.Commands.Contains("storageopen")&&prepareFailed.DepositSends==0,"Kapatma teyidi yoksa NPC veya depo aktarımı yoktur");
        var closeFailed=new Game{KeepStorageOpen=true};var failClosing=New("close-failed.json");failClosing.EnsureReady(closeFailed,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
        Reject(()=>failClosing.Checkpoint(closeFailed,CancellationToken.None).GetAwaiter().GetResult(),"Depo kapanmazsa karakter değişimine devam edilmez");
        Check(!closeFailed.Commands.Contains("rotationlogout")&&closeFailed.WithdrawSends==0,"Depo açık kaldığında çıkış ve cam alma isteği yoktur");
        using(var waitCancel=new CancellationTokenSource()){
            var cancelGame=new Game();Task CancelDelay(TimeSpan span,CancellationToken token){if(span==TimeSpan.FromSeconds(11))waitCancel.Cancel();return Wait(span,token);}
            var cancelCycle=new CharacterCycle(Path.Combine(folder,"cancel-delay.json"),options,()=>"test-secret",_=>{},_=>{},()=>now,CancelDelay);
            cancelCycle.EnsureReady(cancelGame,CancellationToken.None).GetAwaiter().GetResult();now=now.AddMinutes(120);
            Reject(()=>cancelCycle.Checkpoint(cancelGame,waitCancel.Token).GetAwaiter().GetResult(),"11 saniyelik bekleme Durdur ile iptal edilir");
            Check(!cancelGame.Commands.Contains("rotationlogout")&&cancelGame.Stored==60005,"Beklemede durdurulunca çıkış yapılmaz ve cam depoda korunur");
        }
        var identityOptions=CharacterCycleOptions.Parse("1","1,2,3,4,5",0);
        var moving=new Game{ReorderOnEnter=true,Clock=()=>now};var movingPath=Path.Combine(folder,"moving-order.json");
        CharacterCycle Moving()=>new(movingPath,identityOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait);
        var identityCycle=Moving();identityCycle.EnsureReady(moving,CancellationToken.None).GetAwaiter().GetResult();
        foreach(var expected in new[]{1,2,3,4,0,1}){
            now=now.AddMinutes(1);try{identityCycle.Checkpoint(moving,CancellationToken.None).GetAwaiter().GetResult();}catch(CharacterCycleChangedException){}
            Check(moving.Slot==expected&&moving.Glass[expected]==60005&&identityCycle.LoginCharacter=="C"+expected,"Son girilen başa taşınsa da sabit döngü kimliğiyle doğru karaktere girilir: "+expected);
        }
        Check(moving.EnteredRawSlots.SequenceEqual(new[]{1,2,3,4,4,4}),"Son karakterden ilkine dönüş güncel 5. indeksle çözülür; sabit 1. indekse girilmez");
        var resumedIdentity=Moving();resumedIdentity.EnsureReady(moving,CancellationToken.None).GetAwaiter().GetResult();
        Check(resumedIdentity.LoginCharacter=="C1"&&resumedIdentity.LoginSlot==1,"Program yeniden açılınca değişen sıralamaya rağmen son karakter kimliği korunur");
        var depositCount=moving.DepositSends;moving.Names[3]="OtherAccountCharacter";now=now.AddMinutes(1);
        Reject(()=>resumedIdentity.Checkpoint(moving,CancellationToken.None).GetAwaiter().GetResult(),"Gerçek hesap karakter listesi değişirse kimlik koruması durdurur");
        Check(moving.DepositSends==depositCount,"Gerçek hesap uyuşmazlığında cam taşınmaz");
        var originalNames=new[]{"C0","C1","C2","C3","C4"};
        CycleJournal Legacy(string phase)=>new(){SchemaVersion=1,Server="Safir, CH1",RosterHash=CycleSnapshot.Hash(originalNames,true),Order=[0,1,2,3,4],CurrentSlot=0,NextSlot=1,StartedAt=now,Phase=phase};
        var legacyPath=Path.Combine(folder,"legacy-order.json");File.WriteAllText(legacyPath,JsonData.Encode(Legacy("Scanning")));
        var legacyGame=new Game{RawOrder=[4,3,2,1,0]};var legacyCycle=new CharacterCycle(legacyPath,identityOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait);
        legacyCycle.EnsureReady(legacyGame,CancellationToken.None).GetAwaiter().GetResult();
        var converted=JsonData.Decode<CycleJournal>(File.ReadAllText(legacyPath));
        Check(converted.SchemaVersion==2&&converted.Characters.SequenceEqual(originalNames)&&Directory.EnumerateFiles(folder,"legacy-order.json.previous-*").Any(),"Eski sıralı hash aynı beş karakterin permütasyonundan doğrulanıp yedekle dönüştürülür");
        var pendingPath=Path.Combine(folder,"legacy-pending.json");var pendingLegacy=Legacy("DepositPending");pendingLegacy.InventorySlot=2;pendingLegacy.StorageSlot=7;File.WriteAllText(pendingPath,JsonData.Encode(pendingLegacy));
        var legacyPendingGame=new Game{RawOrder=[4,3,2,1,0],Stored=60005,GlassOpen=false};legacyPendingGame.Glass[0]=0;
        new CharacterCycle(pendingPath,identityOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait).EnsureReady(legacyPendingGame,CancellationToken.None).GetAwaiter().GetResult();
        Check(legacyPendingGame.DepositSends==0&&legacyPendingGame.WithdrawSends==1&&legacyPendingGame.Slot==1,"Eski aktarım günlüğü indeksler değişse de camı yeniden bırakmadan uzlaştırılır");
        var unknownPath=Path.Combine(folder,"legacy-unknown.json");var unknown=Legacy("DepositPending");unknown.RosterHash=new string('F',64);unknown.InventorySlot=2;unknown.StorageSlot=7;var unknownText=JsonData.Encode(unknown);File.WriteAllText(unknownPath,unknownText);
        var unknownGame=new Game();Reject(()=>new CharacterCycle(unknownPath,identityOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait).EnsureReady(unknownGame,CancellationToken.None).GetAwaiter().GetResult(),"Hesabı doğrulanamayan bekleyen aktarım otomatik sıfırlanmaz");
        Check(File.ReadAllText(unknownPath)==unknownText&&unknownGame.DepositSends==0,"Doğrulanamayan aktarım kaydı aynen korunur");
        unknown.Phase="Depositing";unknown.InventorySlot=-1;unknown.StorageSlot=-1;File.WriteAllText(unknownPath,JsonData.Encode(unknown));
        new CharacterCycle(unknownPath,identityOptions,()=>"test-secret",_=>{},_=>{},()=>now,Wait).EnsureReady(unknownGame,CancellationToken.None).GetAwaiter().GetResult();
        Check(JsonData.Decode<CycleJournal>(File.ReadAllText(unknownPath)).SchemaVersion==2&&unknownGame.DepositSends==0,"Aktarım hiç başlamamış eski kayıt eldeki cam ve ilk karakterle güvenle yeniden başlar");
        var warming=new Game{IdentityWarmupReads=2};new SessionGuard(warming,"Safir, CH1",()=>true,()=>0,delay:Wait,expectedCharacter:()=>"C0").RecoverAsync(_=>{},CancellationToken.None).GetAwaiter().GetResult();
        Check(warming.Commands.Count(c=>c=="gamestate")==3,"Yükleme sırasında boş karakter adı yanlış hesap hatası üretmeden beklenir");
        var wrongIdentity=new Game();Reject(()=>new SessionGuard(wrongIdentity,"Safir, CH1",()=>true,()=>0,delay:Wait,expectedCharacter:()=>"C4").RecoverAsync(_=>{},CancellationToken.None).GetAwaiter().GetResult(),"Yanlış karakterle giriş cam/depo kullanılmadan reddedilir");

        var scheduler=new ScanScheduler(()=>now){FullOnStart=true,HasCompleteRead=_=>true};scheduler.SetPlan(["0:2:0"]);
        Check(scheduler.Next().Kind==ScanKind.Full&&scheduler.Next().Kind==ScanKind.Hot,"Her yeni karakter ilk tam turdan sonra hızlı geçişe döner");
        var pages=new Pages();var write=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var audits=new List<TradeAudit>();var checkpoints=0;var moved=false;
        var scan=TradeScanner.Run(pages,_=>write.Task,_=>{},CancellationToken.None,audit:audits.Add,checkpoint:_=>{
            checkpoints++;if(checkpoints==3){moved=true;throw new CharacterCycleChangedException();}return Task.CompletedTask;
        });
        Check(pages.Page==2&&!scan.IsCompleted&&!moved,"Karakter değişimi önceki sayfanın arka plan yazımı sürerken başlayamaz");
        write.SetResult();try{scan.GetAwaiter().GetResult();}catch(CharacterCycleChangedException){}
        Check(moved&&!audits.Any(a=>a.Kind is "Complete" or "RunEnd"),"Süre nedeniyle kesilen kategori/tur tamamlanmış gibi yayınlanmaz");
        var open=new NavigationSnapshot(new(200,200),new(600,200),9005,5,100,new(0,0),new string('0',25));
        var p=NavigationPlanner.Waypoint(open);Check(p is not null&&p.Value.Distance(open.Position)<=150,"Yürüyüş kısa normal hareket adımlarına ayrılır");
        var blocked=open with{Grid="1111111111110111111111111"};Check(NavigationPlanner.Waypoint(blocked) is null,"Kapalı/yüklenmemiş ızgarada yürüyüş yolu uydurulmaz");
        var corner=new NavigationSnapshot(new(0,0),new(200,200),9005,3,100,new(0,0),"011101110");Check(NavigationPlanner.Waypoint(corner) is null,"Çapraz harekette engel köşesi kesilmez");

        CheckColdStart(results,Wait);
        var identity=GameforgeLauncher.Identity(Environment.ProcessId);
        Check(identity.Pid==Environment.ProcessId&&string.Equals(identity.Executable,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)&&identity.StartedUtcTicks>0,"Süreç kimliği VM_READ ve MainModule olmadan sınırlı sorgu izniyle okunur");
        var elevated=ScannerElevation.StartInfo("C:\\Scanner Folder\\scanner.exe",new[]{"--data-dir","C:\\Data Folder\\scanner-data","--start-client","123"});
        Check(elevated.UseShellExecute&&elevated.Verb=="runas"&&elevated.ArgumentList.SequenceEqual(new[]{"--data-dir","C:\\Data Folder\\scanner-data","--start-client","123"}),"Yönetici yeniden açılışı veri yolu ve istemci PID argümanlarını korur");
        Check(GameforgePlayGeometry.Point(0,0,1000,690)==new System.Drawing.Point(370,104),"Gameforge sabit düzende Oyna merkezini hedefler");
        Check(GameforgePlayGeometry.Point(0,0,1250,863)==new System.Drawing.Point(462,130),"Gameforge Oyna konumu DPI ölçeğiyle eşleşir");
        Reject(()=>GameforgePlayGeometry.Point(292,117,1000,690),"Gameforge 0,0 konumuna yerleşmeden koordinat tıklaması yapılamaz");
        Reject(()=>GameforgePlayGeometry.Point(0,0,1400,690),"Boyutu değişmiş Gameforge düzeninde sabit koordinat kullanılmaz");
        Check(GameforgePlayGeometry.OwnsClick(20,new HashSet<int>{20,21},20,21),"Gameforge ayrı Spark render penceresi Oyna tıklamasını engellemez");
        Check(!GameforgePlayGeometry.OwnsClick(20,new HashSet<int>{20,21},20,99),"Başka uygulama Oyna noktasını kapatırsa tıklanmaz");
        Check(!GameforgePlayGeometry.OwnsClick(20,new HashSet<int>{20,21},99,21),"Odak başka uygulamadayken Oyna tıklanmaz");
        var launcher=new Launcher();var client=new ClientRecovery(launcher,Wait).Restart("game",_=>{},CancellationToken.None).GetAwaiter().GetResult();
        Check(client.Pid==10&&launcher.Clicks==1,"Kapanan istemci için tek Oyna isteğiyle doğru EXE'nin yeni süreci seçilir");
        var multiple=new Launcher{Ambiguous=true};Reject(()=>new ClientRecovery(multiple,Wait).Restart("game",_=>{},CancellationToken.None).GetAwaiter().GetResult(),"Birden fazla yeni istemci varsa rastgele bağlanılmaz");
        var never=new Launcher{NeverReady=true};Reject(()=>new ClientRecovery(never,Wait).Restart("game",_=>{},CancellationToken.None).GetAwaiter().GetResult(),"Launcher zaman aşımı belirsiz Oyna isteğini tekrar etmez");Check(never.Clicks==1,"Launcher zaman aşımında tek Oyna sınırı korunur");
    }
    private static void CheckColdStart(List<string> results,Func<TimeSpan,CancellationToken,Task> wait)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add("PASS: "+name);}
        var launcher=new Launcher{KeepExisting=true};
        var client=new ClientRecovery(launcher,wait).Start(_=>{},CancellationToken.None).GetAwaiter().GetResult();
        Check(client.Pid==10&&launcher.Clicks==1,"Kapalı oyunu başlatma eski EXE yolu istemez ve mevcut süreçleri seçmez");
        var multiple=new Launcher{Ambiguous=true};var ambiguous=false;
        try{new ClientRecovery(multiple,wait).Start(_=>{},CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){ambiguous=true;}
        Check(ambiguous&&multiple.Clicks==1,"İlk açılışta birden fazla yeni istemci varsa seçim durur");
        var never=new Launcher{NeverReady=true};var timedOut=false;
        try{new ClientRecovery(never,wait).Start(_=>{},CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){timedOut=true;}
        Check(timedOut&&never.Clicks==1,"İlk açılış zaman aşımında Oyna tekrar gönderilmez");
        var canceled=new Launcher();using var cts=new CancellationTokenSource();cts.Cancel();var stopped=false;
        try{new ClientRecovery(canceled,wait).Start(_=>{},cts.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}
        Check(stopped&&canceled.Clicks==0&&canceled.Polls==0,"İptal edilmiş başlangıç Gameforge veya istemcilere dokunmaz");
    }
    public static int RunIsolated(string root)
    {
        Directory.CreateDirectory(root);var results=new List<string>();
        try{Run(Path.Combine(root,"run-"+Guid.NewGuid().ToString("N")),results);File.WriteAllText(Path.Combine(root,"last-run.json"),JsonData.Encode(new{success=true,checks=results}));return 0;}
        catch(Exception error){File.WriteAllText(Path.Combine(root,"last-run.json"),JsonData.Encode(new{success=false,checks=results,error=error.ToString()}));return 1;}
    }
}
