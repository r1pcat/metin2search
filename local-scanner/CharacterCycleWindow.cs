using System.Windows;
using System.Windows.Controls;

namespace Metin2Search.Local;

public sealed partial class MainWindow
{
    private readonly CheckBox cycleEnabled=new(){Content="Karakter döngüsü",Margin=new Thickness(0,0,12,5),VerticalAlignment=VerticalAlignment.Center};
    private readonly CheckBox launcherRecovery=new(){Content="Oyun kapalıysa Gameforge'dan aç",IsChecked=true,Margin=new Thickness(0,0,0,5)};
    private readonly TextBox cycleMinutes=new(){Text="288",Width=62,ToolTip="Her karakterde en fazla kalınacak dakika"};
    private readonly TextBox cycleOrder=new(){Text="1,2,3,4,5",Width=110,ToolTip="Başlangıçta kaydedilen sabit karakter sırası. Son girilen başa taşınsa da karakter kimliğinden bulunur."};
    private readonly PasswordBox storagePassword=new(){Width=112,MaxLength=16,ToolTip="Depo şifresi bu Windows kullanıcısına bağlı şifreli kaydedilir. Alanı boşaltmak kayıtlı şifreyi siler.",Background=PanelBg,Foreground=Text};
    private readonly TextBlock cycleInfo=new(){Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,8)};
    private CharacterCycle? characterCycle;
    private bool recoverLauncherOn;
    private bool connectionRestart;
    private GameClientIdentity? runningClient;
    private void BuildCycleOptions(StackPanel panel,UiSettings settings)
    {
        var passwordFile=Path.Combine(rootDirectory,"depo-parolasi.dpapi");
        try{storagePassword.Password=StoredStoragePassword.Read(passwordFile);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException){cycleInfo.Text="Kayıtlı depo şifresi açılamadı; şifreyi yeniden girin.";}
        storagePassword.PasswordChanged+=(_,_)=>{
            try{StoredStoragePassword.Save(passwordFile,storagePassword.Password);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException){SetStatus("Depo şifresi kaydedilemedi; dosya erişimini kontrol edin.",true);}
        };
        cycleEnabled.IsChecked=settings.CharacterCycle;
        cycleMinutes.Text=Math.Clamp(settings.CharacterMinutes,1,1440).ToString();cycleOrder.Text=settings.CharacterOrder;
        launcherRecovery.IsChecked=settings.LauncherRecovery;
        var row=new WrapPanel();row.Children.Add(cycleEnabled);row.Children.Add(Label("Süre (dk) ",Muted));row.Children.Add(cycleMinutes);
        row.Children.Add(Label("  Sıra ",Muted));row.Children.Add(cycleOrder);panel.Children.Add(row);
        var reset=Button("Yeni döngü");reset.ToolTip="Tarama durmuşken tamamlanmış veya henüz eşya aktarımı başlamamış döngüyü arşivler. Bekleyen cam aktarımı silinmez.";
        reset.Click+=(_,_)=>Safe(()=>{
            if(scanTask is {IsCompleted:false})throw new InvalidOperationException("Önce taramayı durdurun.");
            var path=Path.Combine(rootDirectory,"karakter-dongusu.json");
            using var resetLease=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(File.Exists(path)){
                var saved=JsonData.Decode<CycleJournal>(File.ReadAllText(path));
                if(saved.Phase!="Scanning"&&!(saved.Phase=="Depositing"&&saved.InventorySlot<0&&saved.StorageSlot<0))throw new InvalidOperationException("Cam devri tamamlanmamış; günlüğü silmeden depo/karakter durumunu doğrulayın.");
                File.Move(path,path+".previous-"+Guid.NewGuid().ToString("N"));
            }
            SaveUiSettings();SetStatus("Yeni döngü hazır: başlangıç yuvasındaki karakteri ve camı elle hazırlayıp Başlat'a basın.");
        });row.Children.Add(reset);
        var storageRow=new WrapPanel{Margin=new Thickness(0,5,0,5)};storageRow.Children.Add(Label("Depo şifresi ",Muted));storageRow.Children.Add(storagePassword);panel.Children.Add(storageRow);
        panel.Children.Add(launcherRecovery);panel.Children.Add(cycleInfo);
        cycleEnabled.Checked+=(_,_)=>SaveUiSettings();cycleEnabled.Unchecked+=(_,_)=>SaveUiSettings();
        cycleMinutes.LostFocus+=(_,_)=>SaveUiSettings();cycleOrder.LostFocus+=(_,_)=>SaveUiSettings();
        launcherRecovery.Checked+=(_,_)=>SaveUiSettings();launcherRecovery.Unchecked+=(_,_)=>SaveUiSettings();
    }
    private void UpdateCycleControls(bool running)
    {
        cycleEnabled.IsEnabled=!running;cycleMinutes.IsEnabled=!running;cycleOrder.IsEnabled=!running;launcherRecovery.IsEnabled=!running;
        storagePassword.IsEnabled=!running;characterSlot.IsEnabled=!running;
    }
    private void UpdateCycleDisplay(){cycleInfo.Text=characterCycle?.Display??(cycleEnabled.IsChecked==true?"İlk karakteri elle hazırlayın • başlangıç yuvası oyundaki karakterle aynı olmalı":"");}
    private async Task RunTrade(int pid,CancellationToken token)
    {
        FileStream? cycleLease=null;
        try{
            recoverLauncherOn=launcherRecovery.IsChecked==true;
            var secret=storagePassword.Password;
            if(cycleEnabled.IsChecked==true)cycleLease=new FileStream(Path.Combine(rootDirectory,"karakter-dongusu.json.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            characterCycle=cycleEnabled.IsChecked==true?new CharacterCycle(Path.Combine(rootDirectory,"karakter-dongusu.json"),CharacterCycleOptions.Parse(cycleMinutes.Text,cycleOrder.Text,characterSlotIndex),()=>secret,SetCycleStatus,
                slot=>{characterSlotIndex=slot;characterSlot.SelectedIndex=slot;},audit:(kind,message)=>store.Event(kind,message),
                recoverPrematureDeposit:journal=>CycleTimerRecovery.IsPremature(rootDirectory,journal,CharacterCycleOptions.Parse(cycleMinutes.Text,cycleOrder.Text,characterSlotIndex).Minutes)):null;
            if(characterCycle is not null&&secret.Length==0)throw new InvalidDataException("Karakter döngüsü için depo şifresini girin.");
            if(characterCycle is not null)characterSlotIndex=characterCycle.LoginSlot;
            if(pid==0){
                if(!recoverLauncherOn)throw new InvalidOperationException("Oyun kapalı; Gameforge'dan aç seçeneği kapalı.");
                if(string.IsNullOrWhiteSpace(LastServerInfo()))throw new InvalidOperationException("Karakter döngüsü için son sunucu bilinmiyor. İlk kurulumda oyuna elle girip bir kez taramayı başlatın.");
                sessionWaiting=true;
                var opened=await new ClientRecovery(new GameforgeLauncher()).Start(SetCycleStatus,token);
                pid=opened.Pid;runningClient=opened;
                SetCycleStatus($"İstemci hazır (PID {pid}) • {characterSlotIndex+1}. karaktere giriş yapılıyor");
            }
            var crashes=new Queue<DateTimeOffset>();
            while(true){
                token.ThrowIfCancellationRequested();connectionRestart=false;runningClient=GameforgeLauncher.Identity(pid);
                if(characterCycle is not null)characterSlotIndex=characterCycle.LoginSlot;
                await RunTradeConnection(pid,token);
                if(!connectionRestart)break;
                token.ThrowIfCancellationRequested();
                if(GameforgeLauncher.Alive(runningClient))continue;
                if(!recoverLauncherOn)break;
                var now=DateTimeOffset.UtcNow;while(crashes.TryPeek(out var old)&&old<now.AddHours(-1))crashes.Dequeue();
                if(crashes.Count>=5)throw new IOException("İstemci son bir saatte beş kez kapandı; yeniden açma döngüsü durduruldu.");crashes.Enqueue(now);
                sessionWaiting=true;
                var next=await new ClientRecovery(new GameforgeLauncher()).Restart(runningClient.Executable,SetCycleStatus,token);
                pid=next.Pid;runningClient=next;
                SetCycleStatus($"Yeni istemci hazır (PID {pid}) • {characterSlotIndex+1}. karaktere dönülüyor");
            }
        }catch(OperationCanceledException)when(token.IsCancellationRequested){SetStatus("Tarama ve otomatik kurtarma durduruldu.");}
        catch(Exception error){Fail(error);}
        finally{cycleLease?.Dispose();characterCycle=null;runningClient=null;sessionWaiting=false;sessionClock.Stop();if(!closed){UpdateButtons();UpdateCycleDisplay();_ = Dispatcher.BeginInvoke(()=>{if(!closed)UpdateButtons();});}}
    }
    private void SetCycleStatus(string text){SetStatus(text);UpdateCycleDisplay();}
}
