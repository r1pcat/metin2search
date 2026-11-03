using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Metin2Search.Local;

public sealed class UiSettings
{
    public int CharacterSlot { get; set; }
    public bool CharacterCycle {get;set;}
    public int CharacterMinutes {get;set;}=288;
    public string CharacterOrder {get;set;}="1,2,3,4,5";
    public bool LauncherRecovery {get;set;}=true;
    public bool FullOnly {get;set;}
}

public sealed partial class MainWindow
{
    private readonly ComboBox tradeProcesses = new() { MinWidth = 210 };
    private readonly ComboBox savedServers=new(){Width=210};
    private sealed record SavedServer(string Label,string? Info){public override string ToString()=>Label;}
    private readonly Button tradeStart = Button("Başlat"), tradeStop = Button("Durdur");
    private readonly Button saleWatch=Button("Satış hatasını izle");
    private readonly TextBox saleDetails=new(){IsReadOnly=true,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock tradeProgress = new() { TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 };
    private readonly TextBlock fakeStatus = new(){Text="FAKE: tarama bekleniyor",TextWrapping=TextWrapping.Wrap,Foreground=Muted,FontSize=12,Margin=new Thickness(0,0,0,6)};
    private readonly CheckBox fakeChecks=new(){Content="FAKE kontrolü",IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0)};
    private readonly CheckBox fullOnly=new(){Content="Yalnız tam tarama",Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Center};
    private volatile bool fullOnlyOn;
    private readonly ComboBox characterSlot=new(){Width=64,ToolTip="Yeni döngüde ilk ekran sırası; döngü başladıktan sonra sabit karakter numarası. Değişen ekran indeksi kimlikten bulunur."};
    private volatile bool fakeChecksOn = true;
    private volatile int characterSlotIndex;
    private SerializedBridge? activeBridge;
    private volatile bool sessionWaiting;
    private readonly Stopwatch tradeUiTimer=Stopwatch.StartNew();
    private bool tradeRefreshBusy;
    private bool tradeRefreshOnLoad;
    private int tradeViewRequest;
    private readonly TextBlock runTimeInfo=new(){Foreground=Muted,FontSize=12,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock serverLabel=new(){Foreground=Text,FontSize=15,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly TextBlock stateText=new(){Foreground=Text,FontSize=13,VerticalAlignment=VerticalAlignment.Center};
    private readonly System.Windows.Shapes.Ellipse stateDot=new(){Width=10,Height=10,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,Fill=Color("#5B6B80")};
    private readonly TextBlock elapsedText=new(){Foreground=Text,FontSize=13,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock statListings=StatValue(),statVersions=StatValue(),statSize=StatValue();
    private readonly DataGrid recentScans=Grid();
    private readonly TextBlock uplinkText=new(){Foreground=Muted,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)};
    private readonly Stopwatch currentRunClock=new();
    private readonly Stopwatch sessionClock=new();
    private readonly System.Windows.Threading.DispatcherTimer runClockTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private int completedCategories,totalCategories;
    private readonly HashSet<string> finishedPlanQueries=[];
    private string? tradeRunId;
    private static TextBlock StatValue()=>new(){Foreground=Text,FontSize=17,FontWeight=FontWeights.SemiBold};
    private string SettingsFile=>Path.Combine(rootDirectory,"arayuz-ayarlari.json");

    private UIElement BuildTrade()
    {
        UiSettings settings;
        try{settings=File.Exists(SettingsFile)?JsonData.Decode<UiSettings>(File.ReadAllText(SettingsFile)):new UiSettings();}
        catch(Exception error)when(error is System.Text.Json.JsonException or IOException or InvalidDataException){settings=new UiSettings();}
        for(var i=1;i<=5;i++)characterSlot.Items.Add(i.ToString());
        characterSlot.SelectedIndex=Math.Clamp(settings.CharacterSlot,0,4);characterSlotIndex=characterSlot.SelectedIndex;
        fullOnlyOn=settings.FullOnly||string.Equals(Environment.GetEnvironmentVariable("METIN2_SCAN_MODE"),"full",StringComparison.OrdinalIgnoreCase);fullOnly.IsChecked=fullOnlyOn;
        var panel = new StackPanel();

        var stateRow=new DockPanel{Margin=new Thickness(0,0,0,4)};
        elapsedText.SetValue(DockPanel.DockProperty,Dock.Right);stateRow.Children.Add(elapsedText);
        var stateLeft=new StackPanel{Orientation=Orientation.Horizontal};stateLeft.Children.Add(stateDot);stateLeft.Children.Add(stateText);stateRow.Children.Add(stateLeft);
        panel.Children.Add(stateRow);
        serverLabel.Text="Sunucu: henüz seçilmedi";panel.Children.Add(serverLabel);

        var controls=new WrapPanel{Margin=new Thickness(0,12,0,8)};
        controls.Children.Add(tradeProcesses);
        var refresh=Button("⟳");refresh.Padding=new Thickness(9,7,9,7);refresh.ToolTip="İstemcileri yenile";refresh.Margin=new Thickness(6,0,14,5);refresh.Click+=(_,_)=>Safe(RefreshTradeProcesses);controls.Children.Add(refresh);
        tradeStart.Click += (_, _) => Safe(StartTrade);tradeStart.MinWidth=84;controls.Children.Add(tradeStart);
        tradeStop.Click += (_, _) => scanCts?.Cancel();tradeStop.MinWidth=84;controls.Children.Add(tradeStop);
        saleWatch.Click+=(_,_)=>Safe(StartSaleDiagnostic);
        var menu=new ContextMenu();
        void Option(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>Safe(action);menu.Items.Add(item);}
        Option("Veri klasörünü aç",()=>Process.Start(new ProcessStartInfo("explorer.exe",directory){UseShellExecute=true}));
        Option("Satış tanısını aç",StartSaleDiagnostic);
        Option("JSON dışa aktar",()=>{var dialog=new Microsoft.Win32.SaveFileDialog{Filter="JSON|*.json",FileName="pazar-gecmisi.json"};if(dialog.ShowDialog(this)==true)store.ExportTrade(dialog.FileName);});
        Option("Aktif pazar JSON dışa aktar",()=>{var dialog=new Microsoft.Win32.SaveFileDialog{Filter="JSON|*.json",FileName="aktif-pazar.json"};if(dialog.ShowDialog(this)==true)store.ExportActiveTrade(dialog.FileName);});
        Option("CSV dışa aktar",()=>{var dialog=new Microsoft.Win32.SaveFileDialog{Filter="CSV|*.csv",FileName="pazar-gecmisi.csv"};if(dialog.ShowDialog(this)==true)store.ExportTradeCsv(dialog.FileName);});
        Option("Yedek al",Backup);
        var more=Button("⋯");more.Padding=new Thickness(10,7,10,7);more.ToolTip="Diğer işlemler";more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};controls.Children.Add(more);
        panel.Children.Add(controls);

        var options=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        options.Children.Add(fakeChecks);options.Children.Add(fullOnly);options.Children.Add(characterSlot);
        fullOnly.Checked+=(_,_)=>{fullOnlyOn=true;SaveUiSettings();SetStatus("Yalnız tam tarama • sonraki turda uygulanır.");};
        fullOnly.Unchecked+=(_,_)=>{fullOnlyOn=false;SaveUiSettings();SetStatus("Normal planlayıcı • sonraki turda uygulanır.");};
        fakeChecks.Checked+=(_,_)=>FakeChecksChanged();fakeChecks.Unchecked+=(_,_)=>FakeChecksChanged();
        characterSlot.SelectionChanged+=(_,_)=>SessionSettingsChanged();
        panel.Children.Add(options);
        panel.Children.Add(fakeStatus);
        BuildCycleOptions(panel,settings);

        var stats=new System.Windows.Controls.Grid{Margin=new Thickness(0,0,0,6)};
        for(var i=0;i<4;i++)stats.ColumnDefinitions.Add(new ColumnDefinition{Width=i==3?GridLength.Auto:new GridLength(1,GridUnitType.Star)});
        void Stat(int column,string title,TextBlock value){var cell=new StackPanel{Margin=new Thickness(0,0,10,0)};cell.Children.Add(Label(title,Muted,11));cell.Children.Add(value);System.Windows.Controls.Grid.SetColumn(cell,column);stats.Children.Add(cell);}
        Stat(0,"İzlenen ilan",statListings);Stat(1,"Kayıtlı sürüm",statVersions);Stat(2,"Veri boyutu",statSize);
        var open=Button("Dosyayı aç");open.Margin=new Thickness(0);open.VerticalAlignment=VerticalAlignment.Bottom;open.Click+=(_,_)=>Safe(OpenDataFile);
        System.Windows.Controls.Grid.SetColumn(open,3);stats.Children.Add(open);
        panel.Children.Add(new Border{Background=PanelBg,CornerRadius=new CornerRadius(8),Padding=new Thickness(14,10,14,12),Margin=new Thickness(0,0,0,10),Child=stats});

        runTimeInfo.Text="Tarama bekliyor";panel.Children.Add(runTimeInfo);
        uplinkText.Text=UplinkConfig.TryLoad(rootDirectory,out var uplinkProblem)!=null?"Sunucuya aktarım: tarama başlayınca bağlanır":uplinkProblem??"";
        if(uplinkText.Text.Length>0)panel.Children.Add(uplinkText);
        panel.Children.Add(tradeProgress);
        panel.Children.Add(Label("Son taramalar",Muted,12));
        ((FrameworkElement)panel.Children[^1]).Margin=new Thickness(0,10,0,4);

        var shell=new DockPanel();
        DockPanel.SetDock(panel,Dock.Top);shell.Children.Add(panel);
        recentScans.Height=double.NaN;recentScans.MinRowHeight=28;recentScans.RowHeight=28;
        Column(recentScans,"Başlangıç","Started",105);Column(recentScans,"Tür","Kind",60);Column(recentScans,"Süre","Duration",85);Column(recentScans,"Sonuç","Status",100);Column(recentScans,"Kategori","Categories",-1);
        shell.Children.Add(recentScans);

        runClockTimer.Tick+=(_,_)=>UpdateClockUi();
        Loaded+=(_,_)=>runClockTimer.Start();Closed+=(_,_)=>runClockTimer.Stop();
        RefreshTradeProcesses();RefreshSavedServers();RefreshTrade();UpdateClockUi();
        return shell;
    }
    private void UpdateClockUi()
    {
        UpdateCycleDisplay();
        var running=scanTask is {IsCompleted:false};
        var elapsed=sessionClock.Elapsed;
        elapsedText.Text=running||elapsed>TimeSpan.Zero?$"Geçen süre  {(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}":"";
        stateText.Text=running?(characterCycle?.Busy==true?"Karakter devri":sessionWaiting?"Bekliyor • oyuna dönülüyor":"Çalışıyor"):"Durdu";
        stateDot.Fill=Color(running?(sessionWaiting?"#F5C451":"#67E8BC"):"#5B6B80");
        if(running&&currentRunClock.IsRunning)runTimeInfo.Text=$"Bu tur: {LocalStore.FormatScanDuration(currentRunClock.Elapsed)} · {completedCategories}/{totalCategories} kategori";
    }
    private void OpenDataFile()
    {
        var file=store.FilePath;
        Process.Start(new ProcessStartInfo("explorer.exe",File.Exists(file)?$"/select,\"{file}\"":$"\"{directory}\""){UseShellExecute=true});
    }
    private void SaveUiSettings()
    {
        try{File.WriteAllText(SettingsFile,JsonData.Encode(new UiSettings{CharacterSlot=Math.Max(0,characterSlot.SelectedIndex),CharacterCycle=cycleEnabled.IsChecked==true,CharacterMinutes=int.TryParse(cycleMinutes.Text,out var mins)?Math.Clamp(mins,1,1440):288,CharacterOrder=cycleOrder.Text,LauncherRecovery=launcherRecovery.IsChecked==true,FullOnly=fullOnlyOn}));}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
    }
    private void SessionSettingsChanged()
    {
        characterSlotIndex=Math.Max(0,characterSlot.SelectedIndex);SaveUiSettings();
    }
    private HashSet<int> knownClientIds = new();
    private void RefreshTradeProcesses()
    {
        var selected = (tradeProcesses.SelectedItem as ProcessOption)?.Id;
        var options = Process.GetProcessesByName("metin2client").Select(p =>
        {
            using (p)
            {
                DateTime? started = null;
                try { started = p.StartTime; } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                return new ProcessOption(p.Id, "metin2client", started);
            }
        }).OrderBy(p => p.Started ?? DateTime.MinValue).ToList();
        var appeared = options.Where(p => !knownClientIds.Contains(p.Id)).ToList();
        var first = knownClientIds.Count == 0;
        knownClientIds = options.Select(p => p.Id).ToHashSet();
        tradeProcesses.ItemsSource = options;
        tradeProcesses.SelectedItem = !first && appeared.Count > 0 ? appeared[^1]
            : options.FirstOrDefault(p => p.Id == selected) ?? options.LastOrDefault();
    }
    private void RefreshSavedServers()
    {
        var options=new List<SavedServer>{new("Sunucu henüz seçilmedi",null)};
        var servers=Path.Combine(rootDirectory,"Sunucular");
        if(Directory.Exists(servers))foreach(var folder in Directory.EnumerateDirectories(servers)){
            var metadata=Path.Combine(folder,"sunucu.json");if(!File.Exists(metadata))continue;
            try{using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(metadata));var info=json.RootElement.GetProperty("clientServerInfo").GetString()!;options.Add(new(ServerStorage.Name(info),info));}
            catch(Exception error)when(error is System.Text.Json.JsonException or KeyNotFoundException or InvalidDataException){}
        }
        savedServers.ItemsSource=options;
        savedServers.SelectedItem=options.FirstOrDefault(o=>(o.Info is null?rootDirectory:ServerStorage.DirectoryFor(rootDirectory,o.Info))==directory)??options[0];
        if(savedServers.SelectedItem is SavedServer {Info:not null} current&&!(scanTask is {IsCompleted:false}))serverLabel.Text="Sunucu: "+current.Label;
    }
    public void PreviewTrade()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var raw=new byte[187];
        void U(int at,uint value)=>System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(at,4),value);
        U(9,11000000);U(13,1);U(0x11,189);U(0x8b,2);
        var encoding=System.Text.Encoding.GetEncoding(1254);encoding.GetBytes("Örnek silah+9").CopyTo(raw,0x15);encoding.GetBytes("ÖrnekSatıcı").CopyTo(raw,0x2e);
        store.SaveTrade(new TradePage(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,new TradeCategory(0,2,0,"Demo / Silahlar"),1,1,1,raw));
        serverLabel.Text="Sunucu: [ÖRNEK] Demo, CH1";sessionClock.Restart();sessionClock.Stop();
        RefreshTrade();Refresh();SetStatus("ÖRNEK VERİ • Arayüz önizlemesi; canlı tarama yapılmadı.");
    }
    private async void RefreshTrade()
    {
        if(!Dispatcher.CheckAccess()){_ = Dispatcher.BeginInvoke(new Action(RefreshTrade));return;}
        if(!IsLoaded){
            if(!tradeRefreshOnLoad){
                tradeRefreshOnLoad=true;
                Loaded+=(_,_)=>{tradeRefreshOnLoad=false;RefreshTrade();};
            }
            return;
        }
        tradeViewRequest++;if(tradeRefreshBusy||closed)return;tradeRefreshBusy=true;
        try{
            while(!closed){
                var request=tradeViewRequest;var file=store.FilePath;
                var readTimer=Stopwatch.StartNew();
                var view=await Task.Run(()=>{
                    using var reader=LocalStore.OpenReadOnly(file);
                    return (Runs:reader.RecentScans(8),Versions:reader.TradeObservationCount,Listings:reader.CompactListingCount);
                });
                if(closed)return;if(request!=tradeViewRequest)continue;
                store.Metrics.Record("ui.read",readTimer.Elapsed);
                recentScans.ItemsSource=view.Runs;
                statListings.Text=view.Listings.ToString("N0");statVersions.Text=view.Versions.ToString("N0");
                long bytes=0;foreach(var suffix in new[]{"","-wal"})try{bytes+=new FileInfo(file+suffix).Length;}catch(IOException){}
                statSize.Text=bytes>=1073741824?$"{bytes/1073741824d:F2} GB":$"{bytes/1048576d:F1} MB";
                break;
            }
        }catch(Exception e){if(!closed)SetStatus("Görünüm okunamadı: "+e.Message,true);}
        finally{tradeRefreshBusy=false;}
    }
    private string? LastServerInfo()
    {
        try{var file=Path.Combine(rootDirectory,"son-sunucu.json");return File.Exists(file)?JsonData.Decode<string>(File.ReadAllText(file)):null;}
        catch(Exception error)when(error is System.Text.Json.JsonException or IOException or InvalidDataException){return null;}
    }
    private void FakeChecksChanged()
    {
        fakeChecksOn=fakeChecks.IsChecked==true;
        fakeStatus.Text=fakeChecksOn?"FAKE: açık • tarama bekleniyor":"FAKE: kapalı";
        try{store.Event("FakeChecks",fakeChecksOn?"Açık (tam tur ilk 2 sayfa + hızlı geçiş)":"Kapalı");}catch(Exception error)when(error is IOException or InvalidOperationException){}
        SetStatus(!fakeChecksOn?"FAKE kontrolü kapalı.":"FAKE kontrolü açık (cüzdan 0 Yang / 0 Won olmalı): tam turda her kategorinin ilk 2 sayfası, hızlı geçişte yeni/değişen ve bilinen FAKE satıcılar kontrol edilir.");
    }
    private void StartTrade()
    {
        if (busy || scanTask is { IsCompleted: false }) return;
        RefreshTradeProcesses();
        var pid=(tradeProcesses.SelectedItem as ProcessOption)?.Id??0;
        if(pid==0&&launcherRecovery.IsChecked!=true)throw new InvalidOperationException("Oyun kapalı. Gameforge'dan aç seçeneğini işaretleyin ya da istemciyi elle açın.");
        scanCts?.Dispose(); scanCts = new CancellationTokenSource();
        sessionClock.Restart();sessionWaiting=false;
        scanTask = RunTrade(pid, scanCts.Token); UpdateButtons();
    }
    internal void BeginTradeClient(int pid)
    {
        RefreshTradeProcesses();
        var options=tradeProcesses.ItemsSource as IEnumerable<ProcessOption>;
        tradeProcesses.SelectedItem=options?.SingleOrDefault(p=>p.Id==pid)??throw new InvalidOperationException("Başlatılacak istemci artık çalışmıyor.");
        StartTrade();
    }
    private async Task RunTradeConnection(int pid, CancellationToken token)
    {
        IDisposable? writer=null;
        ScannerPurchaseChecks? purchaseChecks=null;var haltedNotified=false;
        try
        {
            store.Event("TradeStartRequested", $"PID {pid}; Kanal 1; native x86 mesaj köprüsü");
            SetStatus("Ticaret Camı köprüsü bağlanıyor; adresler ve kategori planı doğrulanıyor...");
            await using var native = new NativeTradeBridge(pid,store.Metrics);
            var bridge=new SerializedBridge(native);
            if(await SessionGuard.NeedsRecoveryAsync(bridge,token))
            {
                var now=GameState.From(await bridge.Command("gamestate",token));
                string expected="";
                if(now.Phase!="game")
                {
                    if(characterCycle is null)throw new InvalidOperationException("Oyun dünyada değil (giriş ekranı). Elle girip Ticaret Camını açın, sonra Başlat'a basın.");
                    expected=LastServerInfo()??throw new InvalidOperationException("Son sunucu bilinmiyor: bir kez elle girip taramayı başlatın, sonra karakter döngüsü girişi yapabilir.");
                    await SessionGuard.PrepareLoginTargetAsync(bridge,expected,token);
                }
                var starter=new SessionGuard(bridge,expected,()=>characterCycle is not null,()=>characterCycle?.LoginSlot??characterSlotIndex,log:(kind,message)=>{try{store.Event(kind,message);}catch(Exception error)when(error is IOException or InvalidOperationException){}} ,requireGlass:()=>characterCycle?.NeedsStorage!=true,
                    resolveCharacter:characterCycle is null?null:characterCycle.ResolveLoginSlot,expectedCharacter:()=>characterCycle?.NeedsStorage==true?"":characterCycle?.LoginCharacter??"");
                sessionWaiting=true;
                try{await starter.RecoverAsync(message=>SetStatus(message),token);}finally{sessionWaiting=false;}
            }
            if(characterCycle is not null)await characterCycle.EnsureReady(bridge,token);
            else{
                var actual=GameState.From(await bridge.Command("gamestate",token));
                if(actual.CharacterSlot is >=0 and <5){characterSlotIndex=actual.CharacterSlot;characterSlot.SelectedIndex=actual.CharacterSlot;SaveUiSettings();}
            }
            JsonElement inspection;
            for(var attempt=0;;attempt++)
            {
                try{inspection=await bridge.Command("inspect",token);break;}
                catch(IOException) when(attempt<8&&!token.IsCancellationRequested){SetStatus("Oyun verisi yükleniyor; tekrar deneniyor");await Task.Delay(1000,token);}
            }
            var serverInfo=inspection.GetProperty("serverInfo").GetString()!;
            serverLabel.Text="Sunucu: "+serverInfo;
            await SelectTradeServer(serverInfo,token);
            serverLabel.Text="Sunucu: "+serverInfo;
            activeBridge=bridge;
            try
            {
                writer=store.AcquireScannerWriter();
                StartUplink();
                var purchaseBudget=Environment.GetEnvironmentVariable("METIN2_PURCHASE_CHECK_BUDGET_YANG")??"0";
                {
                    if(!long.TryParse(purchaseBudget,out var authorizedBudget))throw new InvalidDataException("Satın alma test bütçesi geçersiz.");
                    var stateFile=Environment.GetEnvironmentVariable("METIN2_PURCHASE_CHECK_STATE")??Path.Combine(Path.GetDirectoryName(store.FilePath)!,"purchase-check-budget.json");
                    purchaseChecks=new ScannerPurchaseChecks(store,bridge,authorizedBudget,stateFile,inspection.TryGetProperty("purchaseTiming500",out var timing500)&&timing500.ValueKind==JsonValueKind.True?500:1000){Notify=message=>{SetStatus(message);fakeStatus.Text=fakeChecksOn?purchaseChecks?.Status??message:"FAKE: kapalı";}};
                    fakeStatus.Text=fakeChecksOn?purchaseChecks.Status:"FAKE: kapalı";
                    store.Event("PurchaseChecksEnabled",$"Satın alma bütçesi tanımlı ({authorizedBudget} Yang, Won harcaması 0); FAKE kontrolü yalnız onay kutusu işaretliyse çalışır");
                }
                var incremental=!fullOnlyOn;
                store.Event("ScanMode",incremental?"Artımlı: En yeni sıralamasıyla bilinen ilana kadar okur; tam tur yalnız İstanbul 00:00, 06:00, 12:00, 18:00; hiç tam okunmamış kategori ilk ziyarette baştan sona okunur":"Yalnız tam tur");
                var session=new SessionGuard(bridge,serverInfo,()=>characterCycle is not null,()=>characterSlotIndex,
                    log:(kind,message)=>{
                        if(kind=="SessionLost")sessionWaiting=true;else if(kind=="SessionRestored")sessionWaiting=false;
                        try{store.Event(kind,message);}catch(Exception error)when(error is IOException or InvalidOperationException){}
                    },resolveCharacter:characterCycle is null?null:characterCycle.ResolveLoginSlot,expectedCharacter:()=>characterCycle?.LoginCharacter??"");
                try
                {
                    await TradeScanner.Run(bridge, page =>
                    {
                        return SaveTradePage(page,token);
                    }, message => SetStatus(message), token,auditBatch:async batch=>{
                        var data=batch[^1];
                        if(data.Kind=="RunStart"){purchaseChecks?.StartCycle();tradeRunId=data.RunId;completedCategories=0;totalCategories=data.PlannedQueries??data.Expected;finishedPlanQueries.Clear();currentRunClock.Restart();}
                        if(purchaseChecks is not null)fakeStatus.Text=!fakeChecksOn?"FAKE: kapalı":purchaseChecks.IsHalted?purchaseChecks.Status:data.Mode is null or "Hot"?purchaseChecks.Status:"FAKE: doğrulama taramasında bekliyor";
                        if(data.Kind is "Complete" or "Failed" or "HotDone" && finishedPlanQueries.Add(data.PlanQueryId??data.QueryId))completedCategories++;
                        if(data.Kind=="RunEnd")currentRunClock.Stop();
                        await Task.Run(()=>store.TradeAuditBatch(batch));
                        if(data.Kind=="RunEnd"&&data.Mode is null&&data.Pages==data.PlannedQueries)characterCycle?.FullTourCompleted();
                        if(data.Kind is "Complete" or "Failed" or "RunEnd")RefreshTrade();
                    },marketCheck:null,metrics:store.Metrics,
                        purchasePage:async (page,cancellation)=>
                        {
                            if(!fakeChecksOn||purchaseChecks is null)return;
                            if(purchaseChecks.IsHalted){fakeStatus.Text=purchaseChecks.Status;if(!haltedNotified){haltedNotified=true;SetStatus(purchaseChecks.Status);}return;}
                            await purchaseChecks.Page(page,message=>SetStatus(message),cancellation);
                            fakeStatus.Text=purchaseChecks.Status;
                        },scheduler:new ScanScheduler{HasCompleteRead=key=>store.HasCompleteScopeRead("ch1:"+key),FullOnStart=characterCycle?.ForceFull==true},fullOnly:()=>fullOnlyOn,
                        fakeLookups:purchaseChecks is null?null:new GatedFakeLookups(purchaseChecks,()=>fakeChecksOn),session:session,
                        checkpoint:async cancellation=>{
                            if(characterCycle is not null)await characterCycle.Checkpoint(bridge,cancellation);
                            if(characterCycle is not null)await characterCycle.Checkpoint(bridge,cancellation);
                        });
                }
                finally
                {
                    await StopUplinkAsync();
                    activeBridge=null;
                }
            }
            finally{activeBridge=null;}
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { SetStatus("Ticaret Camı taraması durduruldu. Kaydedilen sayfalar korundu."); }
        catch (CharacterCycleChangedException){connectionRestart=true;SetCycleStatus("Karakter devri doğrulandı • yeni karakterde tam tarama başlıyor");}
        catch (Exception) when(runningClient is {} identity&&!GameforgeLauncher.Alive(identity)&&recoverLauncherOn&&!token.IsCancellationRequested){connectionRestart=true;SetCycleStatus("İstemci tamamen kapandı • tarama durumu korundu");}
        catch (Exception error) { Fail(error); }
        finally {
            currentRunClock.Stop();sessionWaiting=false;runTimeInfo.Text=$"Son çalışma: {LocalStore.FormatScanDuration(currentRunClock.Elapsed)} · Durduruldu";
            try{if(tradeRunId is {} id)store.StopTradeRun(id,"Tarama görevi kapandı");store.Event("TradeStopped", "Ticaret Camı tarama görevi kapandı.");}
            catch(IOException error){SetStatus("Tarama durdu; kapanış kaydı yazılamadı: "+error.Message,true);}
            writer?.Dispose();
            purchaseChecks?.Dispose();
            if(!closed)_ = Dispatcher.BeginInvoke(() => { if(closed)return;UpdateButtons();UpdateClockUi(); Safe(()=>{Refresh();RefreshTrade();}); });
        }
    }
    private CancellationTokenSource? uplinkCts;
    private Task? uplinkTask;
    private UplinkWorker? uplinkWorker;
    private void StartUplink()
    {
        var config=UplinkConfig.TryLoad(rootDirectory,out var problem);
        if(config is null){if(problem is not null)SetStatus(problem,true);return;}
        var http=new HttpClient(new SocketsHttpHandler{ConnectTimeout=TimeSpan.FromSeconds(15)}){Timeout=TimeSpan.FromMinutes(3)};
        uplinkWorker=new UplinkWorker(store,new UplinkClient(http,config),status=>Dispatcher.BeginInvoke(()=>{if(!closed)uplinkText.Text=status.Line;}));
        uplinkCts=new CancellationTokenSource();
        var worker=uplinkWorker;var cts=uplinkCts;
        uplinkTask=Task.Run(()=>worker.RunAsync(cts.Token));
        store.Event("Uplink","Sunucuya aktarım başladı: "+config.BaseUrl.Host);
    }
    private async Task StopUplinkAsync()
    {
        if(uplinkWorker is null||uplinkCts is null)return;
        try
        {
            using var last=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            uplinkCts.Cancel();if(uplinkTask is not null)await uplinkTask;
            await uplinkWorker.DrainOnceAsync(last.Token);
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or InvalidOperationException){}
        finally{uplinkWorker=null;uplinkCts.Dispose();uplinkCts=null;uplinkTask=null;}
    }
    private async Task SaveTradePage(TradePage page,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(()=>store.SaveTrade(page),token);
        if(tradeUiTimer.ElapsedMilliseconds>5000){RefreshTrade();tradeUiTimer.Restart();}
        tradeProgress.Text=$"{page.Category.Label} • Sayfa {page.Page} • {store.LastTradeChanged} yeni/değişen • {store.LastTradeUnchanged} değişmedi";
    }
}

public sealed class SerializedBridge(ITradeBridge inner) : ITradeBridge
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<System.Text.Json.JsonElement> Command(string command, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await inner.Command(command, token); }
        finally { gate.Release(); }
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
