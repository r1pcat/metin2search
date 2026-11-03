using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

public sealed record CharacterCycleOptions(int Minutes,int[] Order,int InitialSlot)
{
    public static CharacterCycleOptions Parse(string minutes,string order,int initialSlot)
    {
        if(!int.TryParse(minutes,out var value)||value<1||value>1440)throw new InvalidDataException("Karakter süresi 1–1440 dakika olmalı.");
        var parts=order.Split([',',';', ' '],StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length<2||parts.Length>5||parts.Any(p=>!int.TryParse(p,out var n)||n<1||n>5))throw new InvalidDataException("Karakter sırasına 1–5 yuvalarını yazın; örnek: 1,2,3,4,5.");
        var slots=parts.Select(p=>int.Parse(p)-1).ToArray();
        if(slots.Distinct().Count()!=slots.Length||!slots.Contains(initialSlot))throw new InvalidDataException("Karakter sırası tekrarsız olmalı ve başlangıç yuvasını içermeli.");
        return new(value,slots,initialSlot);
    }
}
public sealed record StorageCell(int Slot,uint Vnum,uint Count);
public sealed record CycleSnapshot(string Server,string RosterHash,int CharacterSlot,StorageCell[] Inventory,bool StorageOpen,StorageCell[] Storage,int[] AvailableSlots,string[] Characters,string Character,int[] DisplaySlots)
{
    public static string Hash(IEnumerable<string> names,bool ordered=false)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ordered?names.ToArray():names.Order(StringComparer.Ordinal).ToArray()))));
    public static string[] Names(JsonElement json)
    {
        var names=json.GetProperty("roster").EnumerateArray().Select(v=>v.GetString()??"").ToArray();
        if(names.Length!=5||names.Where(n=>n.Length>0).Distinct(StringComparer.Ordinal).Count()!=names.Count(n=>n.Length>0))throw new InvalidDataException("Karakter listesi eksik veya tekrarlı.");
        return names;
    }
    public static CycleSnapshot From(JsonElement json)
    {
        static StorageCell[] Cells(JsonElement j,string key)=>j.GetProperty(key).EnumerateArray().Select(r=>new StorageCell(r[0].GetInt32(),r[1].GetUInt32(),r[2].GetUInt32())).ToArray();
        var roster=Names(json);var slot=json.GetProperty("characterSlot").GetInt32();
        var display=json.TryGetProperty("displaySlots",out var ds)?ds.EnumerateArray().Select(v=>v.GetInt32()).ToArray():Enumerable.Range(0,5).ToArray();
        if(slot<0||slot>=5||roster[slot].Length==0||!display.Order().SequenceEqual(Enumerable.Range(0,5)))throw new InvalidDataException("Karakter yuvası/sıralaması geçersiz.");
        var name=json.TryGetProperty("character",out var ch)?ch.GetString()??"":roster[slot];
        if(name!=roster[slot])throw new InvalidDataException("Aktif karakter adı ve gerçek yuvası eşleşmiyor.");
        return new(json.GetProperty("serverInfo").GetString()!.Trim(),Hash(roster),slot,Cells(json,"inventory"),json.GetProperty("storageOpen").GetBoolean(),Cells(json,"storage"),Enumerable.Range(0,5).Where(i=>!string.IsNullOrWhiteSpace(roster[display[i]])).ToArray(),roster,name,display);
    }
}
public sealed class CycleJournal
{
    public int SchemaVersion {get;set;}=2;
    public string[] Characters {get;set;}=[];
    public string Server {get;set;}="";
    public string RosterHash {get;set;}="";
    public int[] Order {get;set;}=[];
    public int CurrentSlot {get;set;}
    public int NextSlot {get;set;}
    public DateTimeOffset StartedAt {get;set;}
    public string Phase {get;set;}="Scanning";
    public int InventorySlot {get;set;}=-1;
    public int StorageSlot {get;set;}=-1;
    public bool ForceFull {get;set;}=true;
}
public sealed class CharacterCycleChangedException : Exception;
public sealed class CycleManualInterventionException(string message) : InvalidOperationException(message);

public sealed class CharacterCycle
{
    private readonly string path;
    private readonly CharacterCycleOptions options;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<TimeSpan,CancellationToken,Task> delay;
    private readonly Func<string> password;
    private readonly Action<string> progress;
    private readonly Action<int> select;
    private readonly TimeSpan responseTimeout;
    private readonly Action<string,string>? audit;
    private readonly Func<CycleJournal,bool>? recoverPrematureDeposit;
    private CycleJournal? journal;
    public bool Busy {get;private set;}
    public int LoginSlot=>journal?.Phase is "Stored" or "Selecting" or "Withdrawing" or "WithdrawPending" ? journal.NextSlot : journal?.CurrentSlot??options.InitialSlot;
    public string LoginCharacter=>journal is {SchemaVersion:2}?journal.Characters[LoginSlot]:"";
    public bool NeedsStorage=>journal is not null&&journal.Phase!="Scanning";
    public bool ForceFull=>journal?.ForceFull??true;
    public bool IsDue=>journal is not null&&journal.Phase=="Scanning"&&clock()>=journal.StartedAt.AddMinutes(options.Minutes);
    public string Display=>journal is null?"Döngü hazırlanıyor":journal.Phase=="Scanning"
        ?$"Karakter {journal.CurrentSlot+1} → {Next(journal.CurrentSlot)+1} · kalan {Math.Max(0,(int)Math.Ceiling((journal.StartedAt.AddMinutes(options.Minutes)-clock()).TotalMinutes))} dk"
        :$"Karakter {journal.CurrentSlot+1} → {journal.NextSlot+1} · "+(journal.Phase switch{"Depositing"=>"depoya gidiliyor","DepositPending"=>"camın depoya geçtiği doğrulanıyor","Stored" or "Selecting"=>"karakter değiştiriliyor",_=>"cam depodan alınıyor"});
    public CharacterCycle(string path,CharacterCycleOptions options,Func<string> password,Action<string> progress,Action<int> select,
        Func<DateTimeOffset>? clock=null,Func<TimeSpan,CancellationToken,Task>? delay=null,TimeSpan? responseTimeout=null,Action<string,string>? audit=null,Func<CycleJournal,bool>? recoverPrematureDeposit=null)
    {
        this.path=path;this.options=options;this.password=password;this.progress=progress;this.select=select;
        this.clock=clock??(()=>DateTimeOffset.UtcNow);this.delay=delay??Task.Delay;this.responseTimeout=responseTimeout??TimeSpan.FromSeconds(15);
        this.audit=audit;
        this.recoverPrematureDeposit=recoverPrematureDeposit;
        if(File.Exists(path)){
            journal=JsonData.Decode<CycleJournal>(File.ReadAllText(path));
            if(journal.SchemaVersion is not (1 or 2)||!journal.Order.SequenceEqual(options.Order)||!options.Order.Contains(journal.CurrentSlot)||!options.Order.Contains(journal.NextSlot)
                ||journal.SchemaVersion==2&&(journal.Characters.Length!=5||CycleSnapshot.Hash(journal.Characters)!=journal.RosterHash)
                ||journal.Phase is not ("Scanning" or "Depositing" or "DepositPending" or "Stored" or "Selecting" or "Withdrawing" or "WithdrawPending"))
                throw new InvalidDataException("Karakter devir günlüğü/sırası uyuşmuyor. Devam eden devri elle doğrulamadan sıfırlamayın.");
        }
    }
    private int Next(int slot)=>options.Order[(Array.IndexOf(options.Order,slot)+1)%options.Order.Length];
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp=path+".tmp";File.WriteAllText(temp,JsonData.Encode(journal));File.Move(temp,path,true);
        Audit("CycleState",new{journal!.Phase,journal.CurrentSlot,journal.NextSlot,journal.InventorySlot,journal.StorageSlot,journal.ForceFull,journal.StartedAt});
    }
    private void Audit(string kind,object data)
    {
        try{audit?.Invoke(kind,JsonData.Encode(data));}
        catch(Exception error)when(error is IOException or InvalidOperationException){}
    }
    private static void Guard(CycleSnapshot state,CycleJournal journal,int slot)
    {
        if(!string.Equals(state.Server,journal.Server,StringComparison.OrdinalIgnoreCase))throw new CycleManualInterventionException("Döngünün kayıtlı sunucu/kanalı ile açık sunucu/kanal farklı; depo işlemi yapılmadı.");
        if(journal.SchemaVersion!=2||state.RosterHash!=journal.RosterHash)throw new CycleManualInterventionException("Hesabın karakter listesi kayıtlı döngüyle eşleşmedi; depo işlemi yapılmadı.");
        if(state.Character!=journal.Characters[slot])throw new CycleManualInterventionException($"Açık karakter döngüde beklenen {slot+1}. karakter değil (güncel gerçek yuva {state.CharacterSlot+1}); depo işlemi yapılmadı.");
    }
    public int ResolveLoginSlot(JsonElement list)=>ResolveSlot(list,LoginSlot);
    private int ResolveSlot(JsonElement list,int logicalSlot)
    {
        var names=CycleSnapshot.Names(list);
        if(journal is null)throw new CycleManualInterventionException("Karakter döngüsü kimliği henüz kaydedilmedi; ilk giriş elle yapılmalı.");
        if(journal.SchemaVersion==1)MigrateLegacy(names);
        if(CycleSnapshot.Hash(names)!=journal!.RosterHash)throw new CycleManualInterventionException("Giriş ekranındaki hesap karakter listesi farklı; karaktere giriş yapılmadı.");
        var raw=Array.IndexOf(names,journal.Characters[logicalSlot]);
        if(raw<0||journal.Characters[logicalSlot].Length==0)throw new CycleManualInterventionException("Hedef karakter listede bulunamadı.");
        return raw;
    }
    private void MigrateLegacy(string[] names)
    {
        if(journal?.SchemaVersion!=1)return;
        string[]? original=null;var work=names.ToArray();
        void Permute(int at){if(original is not null)return;if(at==5){if(CycleSnapshot.Hash(work,true)==journal.RosterHash)original=work.ToArray();return;}for(var i=at;i<5;i++){(work[at],work[i])=(work[i],work[at]);Permute(at+1);(work[at],work[i])=(work[i],work[at]);}}
        Permute(0);
        if(original is null)throw new CycleManualInterventionException("Eski döngü hesabın karakter listesiyle doğrulanamadı; aktarım kaydı korunuyor.");
        Archive();journal.Characters=original;journal.RosterHash=CycleSnapshot.Hash(original);journal.SchemaVersion=2;Save();
        progress("Eski döngü kaydı karakter kimliklerine dönüştürüldü; değişen indeksler otomatik bulunacak");
    }
    private void Archive(){if(File.Exists(path))File.Copy(path,path+".previous-"+Guid.NewGuid().ToString("N"));}
    private bool CanStartFresh(CycleSnapshot state)=>journal is not null&&string.Equals(state.Server,journal.Server,StringComparison.OrdinalIgnoreCase)
        &&(journal.Phase=="Scanning"||journal.Phase=="Depositing"&&journal.InventorySlot<0&&journal.StorageSlot<0)
        &&state.CharacterSlot==state.DisplaySlots[options.InitialSlot]&&Glass(state.Inventory) is not null;
    private static StorageCell? Glass(StorageCell[] cells)
    {
        var found=cells.Where(c=>c.Vnum==60005).ToArray();
        if(found.Length>1||found.Any(c=>c.Count!=1))throw new CycleManualInterventionException("Birden fazla veya yığın Ticaret Camı var; devredilecek tek eşya belirsiz.");
        return found.SingleOrDefault();
    }
    private Task Wait(CancellationToken token)=>delay(TimeSpan.FromMilliseconds(500),token);
    private static async Task<CycleSnapshot> Read(ITradeBridge bridge,CancellationToken token)=>CycleSnapshot.From(await bridge.Command("rotationstatus",token));
    public async Task EnsureReady(ITradeBridge bridge,CancellationToken token)
    {
        var first=await Read(bridge,token);
        if(journal?.SchemaVersion==1){
            try{MigrateLegacy(first.Characters);}
            catch(CycleManualInterventionException)when(CanStartFresh(first)){Archive();journal=null;progress("Aktarım başlamamış eski döngü kaydı arşivlendi; eldeki camla yeni başlangıç");}
        }
        if(journal is not null&&CanStartFresh(first)&&(first.RosterHash!=journal.RosterHash||first.Character!=journal.Characters[journal.CurrentSlot])){
            Archive();journal=null;progress("Tamamlanmış/aktarım başlamamış döngü arşivlendi; seçili ilk karakterle yeni başlangıç");
        }
        if(journal is null){
            var state=first;
            if(state.AvailableSlots is not null&&options.Order.Any(slot=>!state.AvailableSlots.Contains(slot)))throw new CycleManualInterventionException("Sırada boş karakter yuvası var; GUI sırasından çıkarın.");
            if(state.CharacterSlot!=state.DisplaySlots[options.InitialSlot]||Glass(state.Inventory) is null)throw new CycleManualInterventionException("İlk karakteri ve Ticaret Camını elle hazırlayın; GUI yuvası oyundaki karakterle aynı olmalı.");
            journal=new(){Server=state.Server,RosterHash=state.RosterHash,Characters=state.DisplaySlots.Select(i=>state.Characters[i]).ToArray(),Order=options.Order,CurrentSlot=options.InitialSlot,NextSlot=Next(options.InitialSlot),StartedAt=clock()};Save();
        }
        if(journal.Phase=="Depositing"&&journal.ForceFull&&recoverPrematureDeposit?.Invoke(journal)==true){
            Guard(first,journal,journal.CurrentSlot);
            var glass=Glass(first.Inventory);
            if(glass is not null&&glass.Slot==journal.InventorySlot){
                if(first.StorageOpen)await CloseStorage(bridge,journal.CurrentSlot,token);
                File.Copy(path,path+".timer-before-"+Guid.NewGuid().ToString("N"));
                journal.Phase="Scanning";journal.StartedAt=clock();journal.InventorySlot=-1;journal.StorageSlot=-1;Save();
                progress("Önceki sürümün erken devir kaydı doğrulandı • eldeki camla yeni karakter süresi ve tam tarama başlıyor");
                await new SessionGuard(bridge,journal.Server,()=>true,()=>journal.CurrentSlot,delay:delay,resolveCharacter:list=>ResolveSlot(list,journal.CurrentSlot),expectedCharacter:()=>journal.Characters[journal.CurrentSlot]).RecoverAsync(progress,token);
            }
        }
        if(journal.Phase!="Scanning"){await ContinueTransfer(bridge,token);return;}
        var current=await Read(bridge,token);Guard(current,journal,journal.CurrentSlot);
        if(Glass(current.Inventory) is null)throw new CycleManualInterventionException("Ticaret Camı mevcut karakterde değil; eski devir günlüğü elle kontrol edilmeli.");
        select(journal.CurrentSlot);
    }
    public void FullTourCompleted(){if(journal is {ForceFull:true}){journal.ForceFull=false;Save();}}
    public async Task Checkpoint(ITradeBridge bridge,CancellationToken token)
    {
        if(!IsDue)return;
        journal!.Phase="Depositing";journal.NextSlot=Next(journal.CurrentSlot);journal.InventorySlot=-1;journal.StorageSlot=-1;Save();
        await ContinueTransfer(bridge,token);
        throw new CharacterCycleChangedException();
    }
    private async Task ContinueTransfer(ITradeBridge bridge,CancellationToken token)
    {
        Busy=true;
        try{
            await SessionGuard.PrepareLoginTargetAsync(bridge,journal!.Server,token);
            if(journal!.Phase is "Depositing" or "DepositPending"){
                var pending=journal.Phase=="DepositPending";
                progress($"Süre doldu: {journal.CurrentSlot+1}. karakter camı depoya bırakıyor");
                await OpenStorage(bridge,journal.CurrentSlot,token);
                var state=await Read(bridge,token);Guard(state,journal,journal.CurrentSlot);
                if(pending){await ConfirmTransfer(bridge,true,token);}else{
                    var item=Glass(state.Inventory)??throw new CycleManualInterventionException("Devredilecek cam envanterde yok.");
                    if(Glass(state.Storage) is not null)throw new CycleManualInterventionException("Depoda başka Ticaret Camı var; hedef belirsiz.");
                    var empty=state.Storage.Where(c=>c.Vnum==0).OrderBy(c=>c.Slot).FirstOrDefault()??throw new CycleManualInterventionException("Depoda boş hücre yok.");
                    journal.InventorySlot=item.Slot;journal.StorageSlot=empty.Slot;journal.Phase="DepositPending";Save();
                    await bridge.Command($"storagedeposit {item.Slot} {empty.Slot}",token);
                    await ConfirmTransfer(bridge,true,token);
                }
                journal.Phase="Stored";Save();
            }
            if(journal.Phase is "Stored" or "Selecting"){
                var phase=GameState.From(await bridge.Command("gamestate",token));
                if(phase.InWorld){
                    var actual=await Read(bridge,token);
                    if(actual.Character==journal.Characters[journal.CurrentSlot]){
                        await OpenStorage(bridge,journal.CurrentSlot,token);
                        actual=await Read(bridge,token);Guard(actual,journal,journal.CurrentSlot);
                        if(Glass(actual.Inventory) is not null||actual.Storage.SingleOrDefault(c=>c.Slot==journal.StorageSlot)?.Vnum!=60005)
                            throw new CycleManualInterventionException("Camın depoda olduğu doğrulanamadı; karakter değiştirilmedi.");
                        await CloseStorage(bridge,journal.CurrentSlot,token);
                        progress("Depo kapandı • karakter değişiminden önce 11 saniye bekleniyor");
                        await delay(TimeSpan.FromSeconds(11),token);
                        token.ThrowIfCancellationRequested();
                        var closedState=await Read(bridge,token);Guard(closedState,journal,journal.CurrentSlot);
                        if(closedState.StorageOpen)throw new CycleManualInterventionException("Bekleme sırasında depo yeniden açıldı; karakter değiştirilmedi.");
                        journal.Phase="Selecting";Save();select(journal.NextSlot);
                        await bridge.Command("rotationlogout",token);
                        var left=false;
                        for(var i=0;i<120;i++){await Wait(token);if(!GameState.From(await bridge.Command("gamestate",token)).InWorld){left=true;break;}}
                        if(!left)throw new CycleManualInterventionException("Çıkış 60 saniyede gerçekleşmedi; yeniden çıkış isteği gönderilmedi.");
                    }else if(actual.Character!=journal.Characters[journal.NextSlot])throw new CycleManualInterventionException("Beklenmeyen karakter açık.");
                }
                select(journal.NextSlot);
                var guard=new SessionGuard(bridge,journal.Server,()=>true,()=>journal.NextSlot,delay:delay,requireGlass:()=>false,resolveCharacter:list=>ResolveSlot(list,journal.NextSlot),expectedCharacter:()=>journal.Characters[journal.NextSlot]);
                await guard.RecoverAsync(progress,token);
                var next=await Read(bridge,token);Guard(next,journal,journal.NextSlot);
                journal.Phase="Withdrawing";Save();
            }
            if(journal.Phase is "Withdrawing" or "WithdrawPending"){
                var pending=journal.Phase=="WithdrawPending";
                progress($"{journal.NextSlot+1}. karakter camı depodan alıyor");
                await OpenStorage(bridge,journal.NextSlot,token);
                var state=pending?await Read(bridge,token):await WaitForStorageGlass(bridge,journal.NextSlot,token);Guard(state,journal,journal.NextSlot);
                if(pending){await ConfirmTransfer(bridge,false,token);}else{
                    var glass=Glass(state.Storage)??throw new CycleManualInterventionException("Devredilen cam depoda yok.");
                    if(glass.Slot!=journal.StorageSlot||Glass(state.Inventory) is not null)throw new CycleManualInterventionException("Depo/envanter cam konumu değişmiş; işlem durdu.");
                    var empty=state.Inventory.FirstOrDefault(c=>c.Vnum==0)??throw new CycleManualInterventionException("Envanterde boş hücre yok.");
                    journal.InventorySlot=empty.Slot;journal.Phase="WithdrawPending";Save();
                    await bridge.Command($"storagewithdraw {empty.Slot} {glass.Slot}",token);
                    await ConfirmTransfer(bridge,false,token);
                }
                await CloseStorage(bridge,journal.NextSlot,token);
                journal.CurrentSlot=journal.NextSlot;journal.NextSlot=Next(journal.CurrentSlot);journal.Phase="Scanning";journal.ForceFull=true;journal.StartedAt=clock();journal.InventorySlot=-1;journal.StorageSlot=-1;Save();select(journal.CurrentSlot);
                await new SessionGuard(bridge,journal.Server,()=>true,()=>journal.CurrentSlot,delay:delay,resolveCharacter:list=>ResolveSlot(list,journal.CurrentSlot),expectedCharacter:()=>journal.Characters[journal.CurrentSlot]).RecoverAsync(progress,token);
                progress($"{journal.CurrentSlot+1}. karakter hazır • ilk geçiş tam tarama");
            }
        }finally{
            Busy=false;
            try{await bridge.Command("navstop",CancellationToken.None);}catch(Exception error)when(error is IOException or OperationCanceledException or TimeoutException){}
        }
    }
    private async Task ConfirmTransfer(ITradeBridge bridge,bool depositing,CancellationToken token)
    {
        var until=clock()+responseTimeout;
        for(var poll=0;poll<120;poll++){
            var state=await Read(bridge,token);Guard(state,journal!,depositing?journal!.CurrentSlot:journal!.NextSlot);
            var inv=state.Inventory.SingleOrDefault(c=>c.Slot==journal!.InventorySlot);
            var stored=state.Storage.SingleOrDefault(c=>c.Slot==journal!.StorageSlot);
            if(!state.StorageOpen)throw new CycleManualInterventionException("Depo aktarımı sırasında depo kapandı; isteği tekrarlamayın.");
            if(depositing?inv?.Vnum==0&&stored is {Vnum:60005,Count:1}:inv is {Vnum:60005,Count:1}&&stored?.Vnum==0)return;
            if(clock()>=until)break;
            await Wait(token);
        }
        throw new CycleManualInterventionException("Depo aktarımının sonucu kesinleşmedi. Yeniden gönderilmedi; camın yerini elle doğrulayın.");
    }
    private async Task OpenStorage(ITradeBridge bridge,int slot,CancellationToken token)
    {
        var before=await Read(bridge,token);Guard(before,journal!,slot);
        progress("Tarama bekletildi • pazar ve Ticaret Camı kapatılıyor");
        var prepared=await bridge.Command("rotationprepare",token);
        if(!prepared.GetProperty("prepared").GetBoolean()||!prepared.GetProperty("glassClosed").GetBoolean()||!prepared.GetProperty("shopClosed").GetBoolean())
            throw new CycleManualInterventionException("Pazar/Ticaret Camı kapanmadı; depo işlemi başlatılmadı.");
        var initial=await Read(bridge,token);Guard(initial,journal!,slot);
        if(initial.StorageOpen){await WaitStorageReady(bridge,slot,token);return;}
        await WalkToStorage(bridge,token);
        var sent=false;
        for(var i=0;i<40;i++){
            var response=await bridge.Command("storageopen",token);
            if(response.GetProperty("open").GetBoolean()){await WaitStorageReady(bridge,slot,token);return;}
            if(response.GetProperty("passwordReady").GetBoolean()&&!sent){
                var secret=password();
                if(secret.Length<1||secret.Length>16||secret.Any(c=>c<33||c>126))throw new CycleManualInterventionException("Depo şifresini GUI'de doldurun (1–16 ASCII karakter).");
                sent=true;await bridge.Command("storagepassword "+Convert.ToHexString(Encoding.ASCII.GetBytes(secret)),token);
            }
            await Wait(token);
        }
        throw new CycleManualInterventionException("Depo açılmadı. NPC menüsü, mesafe ve şifreyi kontrol edin; parola tekrar gönderilmedi.");
    }
    private async Task WaitStorageReady(ITradeBridge bridge,int slot,CancellationToken token)
    {
        StorageCell[]? last=null;var stable=0;
        for(var poll=0;poll<40;poll++){
            var state=await Read(bridge,token);Guard(state,journal!,slot);
            if(state.StorageOpen&&state.Storage.Length>0){
                var cells=state.Storage.OrderBy(c=>c.Slot).ToArray();
                stable=last is not null&&last.SequenceEqual(cells)?stable+1:0;last=cells;
                if(stable>=4)return;
            }else{last=null;stable=0;}
            progress("Depo hücrelerinin yüklenmesi bekleniyor");await Wait(token);
        }
        throw new CycleManualInterventionException("Depo verisi 20 saniyede hazır olmadı; eşya isteği gönderilmedi.");
    }
    private async Task<CycleSnapshot> WaitForStorageGlass(ITradeBridge bridge,int slot,CancellationToken token)
    {
        for(var poll=0;poll<=60;poll++){
            var state=await Read(bridge,token);Guard(state,journal!,slot);
            if(!state.StorageOpen)throw new CycleManualInterventionException("Cam beklenirken depo kapandı; alma isteği gönderilmedi.");
            var glass=Glass(state.Storage);var inventoryGlass=Glass(state.Inventory);
            var cell=state.Storage.SingleOrDefault(c=>c.Slot==journal!.StorageSlot);
            if(poll is 0 or 60||glass is not null||inventoryGlass is not null)
                Audit("CycleStorageRead",new{journal!.Phase,ExpectedSlot=journal.StorageSlot,Cells=state.Storage.Length,SlotVnum=cell?.Vnum,SlotCount=cell?.Count,StorageGlassSlot=glass?.Slot,InventoryGlassSlot=inventoryGlass?.Slot,Poll=poll});
            if(glass is not null){
                if(glass.Slot!=journal!.StorageSlot||inventoryGlass is not null)throw new CycleManualInterventionException("Camın depo/envanter konumu kayıttan farklı; alma isteği gönderilmedi.");
                return state;
            }
            if(inventoryGlass is not null)throw new CycleManualInterventionException("Cam hedef karakterin envanterinde görünüyor; devir kaydıyla doğrulama gerekiyor. Yeni alma isteği gönderilmedi.");
            if(poll==60)break;
            progress($"Depodaki camın yüklenmesi bekleniyor • {poll/2}/30 sn");await Wait(token);
        }
        throw new CycleManualInterventionException("Cam 30 saniye boyunca depoda ve hedef envanterde görünmedi; alma isteği gönderilmedi. Devir kaydı korundu.");
    }
    private async Task CloseStorage(ITradeBridge bridge,int slot,CancellationToken token)
    {
        await bridge.Command("storageclose",token);
        for(var poll=0;poll<40;poll++){
            var state=await Read(bridge,token);Guard(state,journal!,slot);
            if(!state.StorageOpen)return;
            await Wait(token);
        }
        throw new CycleManualInterventionException("Depo kapanmadı; karakter değişimi veya tarama başlatılmadı.");
    }
    private async Task WalkToStorage(ITradeBridge bridge,CancellationToken token)
    {
        var began=clock();var lastProgress=clock();double best=double.PositiveInfinity;
        try{
            for(var step=0;step<1200&&clock()-began<TimeSpan.FromMinutes(10);step++){
                var nav=NavigationSnapshot.From(await bridge.Command("navgrid",token));
                var distance=nav.Position.Distance(nav.Target);
                if(nav.NpcVid>0&&distance<=300){await bridge.Command("navstop",token);return;}
                if(distance<best-40){best=distance;lastProgress=clock();}
                if(clock()-lastProgress>TimeSpan.FromSeconds(20))throw new CycleManualInterventionException("Depoya yürüyüş takıldı; engel atlanmadı.");
                var waypoint=NavigationPlanner.Waypoint(nav)??throw new CycleManualInterventionException("Yüklenmiş arazide güvenli depo yolu bulunamadı.");
                await bridge.Command($"navwalk {(int)Math.Round(waypoint.X)} {(int)Math.Round(waypoint.Y)}",token);
                progress($"Depocuya yürünüyor • {Math.Round(distance/100)} m");
                await Wait(token);
            }
            throw new CycleManualInterventionException("Depoya yürüyüş zaman aşımı; karakter değiştirilmedi.");
        }finally{try{await bridge.Command("navstop",CancellationToken.None);}catch(Exception error)when(error is IOException or OperationCanceledException or TimeoutException){}}
    }
}

public readonly record struct NavPoint(double X,double Y)
{
    public double Distance(NavPoint other)=>Math.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y));
}
public sealed record NavigationSnapshot(NavPoint Position,NavPoint Target,uint NpcVid,int Side,int Cell,NavPoint Origin,string Grid)
{
    public static NavigationSnapshot From(JsonElement j)
    {
        var n=new NavigationSnapshot(new(j.GetProperty("x").GetDouble(),j.GetProperty("y").GetDouble()),new(j.GetProperty("npcX").GetDouble(),j.GetProperty("npcY").GetDouble()),j.GetProperty("npcVid").GetUInt32(),j.GetProperty("side").GetInt32(),j.GetProperty("cell").GetInt32(),new(j.GetProperty("originX").GetDouble(),j.GetProperty("originY").GetDouble()),j.GetProperty("grid").GetString()!);
        if(n.Side<3||n.Side>101||n.Cell!=100||n.Grid.Length!=n.Side*n.Side||n.Grid.Any(c=>c is not ('0' or '1'))||!double.IsFinite(n.Position.X+n.Position.Y+n.Target.X+n.Target.Y+n.Origin.X+n.Origin.Y))throw new InvalidDataException("Navigasyon ızgarası geçersiz.");
        return n;
    }
}
public static class NavigationPlanner
{
    public static NavPoint? Waypoint(NavigationSnapshot n)
    {
        int Index(NavPoint p)=>(int)Math.Round((p.Y-n.Origin.Y)/n.Cell)*n.Side+(int)Math.Round((p.X-n.Origin.X)/n.Cell);
        NavPoint Point(int i)=>new(n.Origin.X+i%n.Side*n.Cell,n.Origin.Y+i/n.Side*n.Cell);
        var start=Index(n.Position);if(start<0||start>=n.Grid.Length)return null;
        var costs=new Dictionary<int,double>{{start,0}};var previous=new Dictionary<int,int>();var queue=new PriorityQueue<int,double>();queue.Enqueue(start,0);
        var best=start;var score=n.Position.Distance(n.Target);var found=false;
        while(queue.TryDequeue(out var current,out var popped)){
            if(popped>costs[current])continue;
            var distance=Point(current).Distance(n.Target);
            if(distance<score-1){score=distance;best=current;found=true;}
            if(distance<=250)break;
            foreach(var (dx,dy) in new[]{(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)}){
                int x=current%n.Side+dx,y=current/n.Side+dy;if(x<0||y<0||x>=n.Side||y>=n.Side)continue;
                var next=y*n.Side+x;if(n.Grid[next]!='0')continue;
                if(dx!=0&&dy!=0&&(n.Grid[current/n.Side*n.Side+x]!='0'||n.Grid[y*n.Side+current%n.Side]!='0'))continue;
                var cost=costs[current]+(dx!=0&&dy!=0?1.41421356237:1);
                if(costs.TryGetValue(next,out var old)&&old<=cost)continue;
                costs[next]=cost;previous[next]=current;queue.Enqueue(next,cost);
            }
        }
        if(!found)return null;
        while(previous.TryGetValue(best,out var parent)&&parent!=start)best=parent;
        return Point(best);
    }
}
