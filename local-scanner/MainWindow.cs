using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Metin2Search.Local;

public sealed partial class MainWindow : Window
{
    private static readonly Brush Bg = Color("#10151F"), PanelBg = Color("#182130"), Text = Color("#E8EEF5"), Muted = Color("#A2B2C8"), Accent = Color("#67E8BC");
    private LocalStore store;
    private string directory;
    private readonly string rootDirectory;
    private readonly TextBlock storagePath=new();
    private readonly TextBlock status = new(), summary = new(), sourceNotice = new();
    private readonly TextBox filter = new(), details = new(), profileEditor = new(), server = new(), map = new(), shop = new();
    private readonly ComboBox processes = new();
    private readonly DataGrid products = Grid(), captures = Grid(), events = Grid();
    private readonly DataGrid discoveredShops = Grid();
    private readonly Button attach = Button("Bağlan"), once = Button("Bir kez oku ve kaydet"), start = Button("Sürekli oku"), stop = Button("Durdur");
    private readonly Button automatic = Button("Pazarları otomatik tara"), discover = Button("Pazarları bul");
    private readonly DispatcherTimer inboxTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, string> importedFiles = new(StringComparer.OrdinalIgnoreCase);
    private ExternalReader? reader;
    private ReaderProfile profile = new();
    private MarketCapture? current;
    private List<ItemRow> itemRows = [];
    private CancellationTokenSource? scanCts;
    private Task? scanTask;
    private string? inbox;
    private bool busy, closed;

    public MainWindow(string directory)
    {
        rootDirectory=directory;
        var lastServer=Path.Combine(rootDirectory,"son-sunucu.json");
        if(File.Exists(lastServer))try{var previousServerInfo=JsonData.Decode<string>(File.ReadAllText(lastServer));directory=ServerStorage.DirectoryFor(rootDirectory,previousServerInfo);}catch(Exception error)when(error is System.Text.Json.JsonException or InvalidDataException){}
        this.directory = directory;
        store = new LocalStore(directory,false);
        Title = "Metin2Search • Ticaret Camı v39";
        Width = 560; Height = 760; MinWidth = 500; MinHeight = 640;
        Background = Bg; Foreground = Text; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AddStyles();
        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 12) };
        Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Label("Metin2Search", Text, 18, FontWeights.SemiBold));

        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        status.Text = "Ticaret Camını aç, istemciyi seç ve Başlat'a bas.";
        status.Foreground = Accent; status.TextWrapping = TextWrapping.Wrap; status.FontSize = 12; footer.Children.Add(status);
        storagePath.Text=store.FilePath;storagePath.Foreground=Muted;storagePath.FontSize=10;storagePath.TextTrimming=TextTrimming.CharacterEllipsis;storagePath.Margin=new Thickness(0,4,0,0);footer.Children.Add(storagePath);

        root.Children.Add(BuildTrade());
        Column(events, "Zaman", "Time", 130); Column(events, "Tür", "Kind", 120); Column(events, "Açıklama", "Message", -1);
        products.SelectionChanged += (_, _) => ShowDetails();
        captures.SelectionChanged += (_, _) => { if (captures.SelectedItem is CaptureRow row) Safe(() => ShowCapture(store.Load(row.Id))); };
        inboxTimer.Tick += (_, _) => PollInbox();
        Closing += ClosingAsync;
        Refresh();
        UpdateButtons();
        Loaded+=async(_,_)=>{
            if(!store.HasLegacyTrade)return;
            busy=true;UpdateButtons();root.IsEnabled=false;SetStatus("Eski kayıtlar yedeklenip sıkıştırılıyor. Büyük veritabanında bu işlem birkaç dakika sürebilir...");
            try{await Task.Run(()=>store.CompactLegacyTrade());RefreshTrade();Refresh();SetStatus("Eski kayıtlar yedeklenerek kayıpsız sıkıştırıldı.");}
            catch(Exception error){Fail(error);}
            finally{busy=false;root.IsEnabled=true;UpdateButtons();}
        };
    }

    private UIElement BuildProducts()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(tools, Dock.Top); panel.Children.Add(tools);
        tools.Children.Add(Action("JSON içe aktar", ImportDialog));
        tools.Children.Add(Action("Örnek veri ekle", AddDemo));
        tools.Children.Add(Action("JSON dışa aktar", ExportJson));
        tools.Children.Add(Action("CSV dışa aktar", ExportCsv));
        tools.Children.Add(Action("Yerel yedek al", Backup));
        tools.Children.Add(Action("Veri klasörü", () => Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true })));
        var notices = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(notices, Dock.Top); panel.Children.Add(notices);
        sourceNotice.Foreground = Accent; sourceNotice.TextWrapping = TextWrapping.Wrap; sourceNotice.Text = "Kayıt yok. JSON içe aktarabilir veya açıkça işaretlenmiş örnek veriyle yerel kaydı test edebilirsin.";
        notices.Children.Add(sourceNotice);
        var search = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        search.Children.Add(Label("Vnum / efsun / pazar filtresi   ", Muted));
        filter.Width = 330; filter.HorizontalAlignment = HorizontalAlignment.Left; filter.TextChanged += (_, _) => ApplyFilter(); search.Children.Add(filter);
        notices.Children.Add(search);

        var split = new System.Windows.Controls.Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        panel.Children.Add(split); split.Children.Add(products);
        Column(products, "Vnum", "Vnum", 90); Column(products, "Adet", "Count", 70); Column(products, "Won", "Won", 130); Column(products, "Yang", "Yang", 140);
        Column(products, "Pazar sahibi", "Owner", 155); Column(products, "Sekme / yuva", "Location", 100); Column(products, "Efsun türü : değer", "BonusSummary", -1); Column(products, "Kaynak", "Source", 120);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Bg };
        System.Windows.Controls.Grid.SetRow(splitter, 1); split.Children.Add(splitter);
        details.IsReadOnly = true; details.AcceptsReturn = true; details.TextWrapping = TextWrapping.NoWrap;
        details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        details.FontFamily = new FontFamily("Consolas"); details.FontSize = 12;
        System.Windows.Controls.Grid.SetRow(details, 2); split.Children.Add(details);
        return panel;
    }

    private UIElement BuildHistory()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var toolbar = new WrapPanel(); toolbar.Children.Add(Action("Yenile", Refresh));
        toolbar.Children.Add(Action("JSON gelen klasörünü izle", ChooseInbox));
        toolbar.Children.Add(Action("Klasör izlemeyi durdur", () => { inboxTimer.Stop(); inbox = null; SetStatus("Klasör izleme durdu."); }));
        DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        var note = Label("Son 1.000 okuma gösterilir. Tüm geçmiş veritabanında korunur. Satıra tıklayınca ürünler yüklenir.", Muted);
        note.Margin = new Thickness(0, 8, 0, 10); DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        Column(captures, "Okuma zamanı", "Observed", 175); Column(captures, "Kaynak", "Source", 130);
        Column(captures, "Pazar / açıklama", "Shop", -1); Column(captures, "Ürün", "Items", 65); Column(captures, "Yuva", "Slots", 65); Column(captures, "Durum", "Status", 180);
        Column(captures, "Pazar sahibi", "Owner", 150);
        panel.Children.Add(captures); return panel;
    }

    private UIElement BuildConnection()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; scroll.Content = panel;
        var connectionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        connectionStatus.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = status });
        connectionStatus.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = status });
        panel.Children.Add(new Border { Background = PanelBg, Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 12), Child = connectionStatus });
        panel.Children.Add(Label("Salt okunur bağlantı testi", Text, 20, FontWeights.SemiBold));
        var note = Label("Otomatik tarama: yüklenmiş pazarları bulur, sırayla normal fare tıklamasıyla açar ve kaydeder.\nDeneysel canlı test: oyun önde kalmalı; F8 veya odağı değiştirmek durdurur. Tam harita kapsamı için keşif rotası gerekir.", Muted);
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 8, 0, 14); panel.Children.Add(note);
        var bar = new WrapPanel(); processes.Width = 330; processes.Margin = new Thickness(0, 0, 8, 0); bar.Children.Add(processes);
        bar.Children.Add(Action("Süreçleri yenile", RefreshProcesses));
        attach.Click += async (_, _) => await Connect(); bar.Children.Add(attach);
        once.Click += async (_, _) => await CaptureOnce(); bar.Children.Add(once);
        start.Click += (_, _) => StartMonitor(); bar.Children.Add(start);
        stop.Click += (_, _) => scanCts?.Cancel(); bar.Children.Add(stop); panel.Children.Add(bar);
        var autoBar = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        discover.Click += (_, _) => Safe(DiscoverShops); autoBar.Children.Add(discover);
        automatic.Click += (_, _) => Safe(StartAutomatic); autoBar.Children.Add(automatic); panel.Children.Add(autoBar);
        discoveredShops.Height = 140; discoveredShops.Margin = new Thickness(0, 8, 0, 8);
        Column(discoveredShops, "Pazar VID", "Vid", 100); Column(discoveredShops, "Oyuncu adı", "DisplayName", 170);
        Column(discoveredShops, "Konum", "Position", 230); Column(discoveredShops, "Ad okuma durumu", "NameDiagnostic", -1);
        panel.Children.Add(discoveredShops);
        var context = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        ContextField(context, "Sunucu", server, "Belirtilmedi"); ContextField(context, "Harita", map, "Belirtilmedi");
        ContextField(context, "Pazar etiketi (manuel)", shop, "Manuel test"); panel.Children.Add(context);
        var actions = new WrapPanel(); actions.Children.Add(Action("Profil yükle", LoadProfile)); actions.Children.Add(Action("Profili kaydet", SaveProfile)); panel.Children.Add(actions);
        panel.Children.Add(Label("Varsayılan v0.2 profil, metot tablosunu doğrular ve kanal/pazar/oyuncu alanlarını yerel nesnelerden okur.\nExplorationRoute isteğe bağlı dünya koordinatı listesidir: [{\"X\": 1000, \"Y\": -2000}]. Örnek değerleri gerçek rota gibi kullanma.", Muted, 12));
        profileEditor.Text = JsonData.Encode(profile); profileEditor.AcceptsReturn = true; profileEditor.AcceptsTab = true;
        profileEditor.FontFamily = new FontFamily("Consolas"); profileEditor.Height = 310; profileEditor.Margin = new Thickness(0, 10, 0, 0);
        profileEditor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; panel.Children.Add(profileEditor);
        return scroll;
    }

    private async Task Connect()
    {
        if (busy || scanTask is { IsCompleted: false }) return;
        if (processes.SelectedItem is not ProcessOption option) { SetStatus("Önce istemci sürecini seç.", true); return; }
        busy = true; UpdateButtons();
        try
        {
            var candidate = JsonData.Decode<ReaderProfile>(profileEditor.Text); candidate.Validate();
            reader?.Dispose(); reader = null;
            reader = await Task.Run(() => new ExternalReader(option.Id, candidate)); profile = candidate;
            store.Event("Connected", $"PID {option.Id}; profil {profile.Version}; salt okunur");
            if (reader.NativeLayoutValidated)
            {
                try { store.Event("NativeState", JsonData.Encode(reader.ReadNativeState())); }
                catch (Exception stateError) { store.Event("NativeStateError", stateError.Message); }
                try
                {
                    var discovered = reader.ReadShops(); discoveredShops.ItemsSource = discovered;
                    store.Event("ConnectedDiscovery", JsonData.Encode(discovered));
                    LogProjectionDiagnostics();
                }
                catch (Exception discoveryError) { store.Event("DiscoveryDiagnosticError", discoveryError.Message); }
            }
            SetStatus($"PID {option.Id} bağlı • " + (reader.NativeLayoutValidated ? "Fiyat/adet metot tablosu eşleşti. Otomatik pazar keşfi kullanılabilir." : "İstemci sürümü eşleşmedi; otomatik tarama kapalı."));
        }
        catch (Exception error) { Fail(error); }
        finally { busy = false; UpdateButtons(); Refresh(); }
    }

    private async Task CaptureOnce()
    {
        if (reader == null || busy) return;
        busy = true; UpdateButtons();
        try { SaveAndShow(await reader.CaptureAsync(server.Text, map.Text, shop.Text, false, CancellationToken.None)); }
        catch (Exception error) { Fail(error); }
        finally { busy = false; UpdateButtons(); }
    }
    private void StartMonitor()
    {
        if (reader == null || busy || scanTask is { IsCompleted: false }) return;
        if (!reader.CanMonitor) { SetStatus("İstemci metot tablosu eşleşmedi. Sürekli okuma açılmadı.", true); return; }
        scanCts?.Dispose(); scanCts = new CancellationTokenSource();
        scanTask = Monitor(scanCts.Token); UpdateButtons();
    }

    private void DiscoverShops()
    {
        if (reader == null) throw new InvalidOperationException("Önce client'e bağlan.");
        var shops = reader.ReadShops();
        store.Event("Discovery", JsonData.Encode(shops));
        discoveredShops.ItemsSource = shops;
        details.Text = string.Join("\n", shops.Select(s => $"{s.DisplayName} | VID {s.Vid} | X {s.Position.X:N0}, Y {s.Position.Y:N0}, Z {s.Position.Z:N0}"));
        SetStatus($"{shops.Count} pazar bulundu • {shops.Count(s => s.NameKnown)} oyuncu adı okundu • {shops.Count(s => !s.NameKnown)} ad bilinmiyor. Sonuçlar aşağıdaki tabloda."); Refresh();
    }
    private void StartAutomatic()
    {
        if (reader == null) { SetStatus("Önce client'e Bağlan düğmesiyle bağlan.", true); return; }
        if (busy || scanTask is { IsCompleted: false }) { SetStatus("Bir işlem zaten sürüyor. Yeniden başlatmak için önce Durdur.", true); return; }
        if (!reader.NativeLayoutValidated) { SetStatus("İstemci sürümü eşleşmedi; otomatik tıklama başlamadı.", true); return; }
        store.Event("AutoRequested", "Otomatik tarama düğmesine basıldı.");
        SetStatus("Otomatik tarama kontrolleri yapılıyor…");
        scanCts?.Dispose(); scanCts = new CancellationTokenSource();
        scanTask = RunAutomatic(scanCts.Token); UpdateButtons();
    }
    private void LogProjectionDiagnostics()
    {
        if (reader == null) return;
        var projection = reader.Projection(); var position = reader.PlayerPosition();
        store.Event("ProjectionDiagnostic", JsonData.Encode(new
        {
            PlayerX = position.X, PlayerY = position.Y, PlayerZ = position.Z,
            Screen = MarketGeometry.Project(position, projection)?.ToString(),
            Viewport = new { projection.X, projection.Y, projection.Width, projection.Height },
            World = projection.World.ToString(), View = projection.View.ToString(), Projection = projection.Projection.ToString()
        }));
    }
    private async Task RunAutomatic(CancellationToken token)
    {
        await Task.Yield(); UpdateButtons();
        try
        {
            LogProjectionDiagnostics();
            var scanner = new AutomaticScanner(reader!, new GameInteraction(reader!.WindowHandle), SaveAndShow,
                (kind, message) => store.Event(kind, message), message => SetStatus(message), store.Diagnostic);
            await scanner.Run(server.Text, map.Text, token);
        }
        catch (OperationCanceledException error) { SetStatus("Otomatik tarama durdu; yeni tıklama gönderilmiyor. " + error.Message); }
        catch (Exception error) { Fail(error); }
        finally { scanTask = null; UpdateButtons(); }
    }
    private async Task Monitor(CancellationToken token)
    {
        await Task.Yield();
        UpdateButtons();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                SaveAndShow(await reader!.CaptureAsync(server.Text, map.Text, shop.Text, true, token));
                await Task.Delay(TimeSpan.FromSeconds(profile.PollSeconds), token);
            }
        }
        catch (OperationCanceledException) { SetStatus("Sürekli okuma durdu. Kaydedilmiş geçmiş korundu."); }
        catch (Exception error) { Fail(error); }
        finally { scanTask = null; UpdateButtons(); }
    }

    public void AddDemo() => Safe(() => SaveAndShow(SampleData.Create()));
    public void PreviewConnection() => ((DockPanel)Content).Children.OfType<TabControl>().Single().SelectedIndex = 3;
    private void SaveAndShow(MarketCapture capture)
    {
        var saved = store.Save(capture); ShowCapture(capture); Refresh();
        SetStatus(saved ? $"Yerelde kaydedildi • {capture.Slots.Count} yuva • {capture.ObservedAt.ToLocalTime():HH:mm:ss} • {capture.Source}" : "Aynı içe aktarma daha önce kaydedilmiş; yeni kopya oluşturulmadı.");
    }
    private void ShowCapture(MarketCapture capture)
    {
        current = capture; itemRows = capture.Slots.Select(s => ItemDecoder.Decode(capture, s)).Where(r => r.Vnum != 0).ToList();
        sourceNotice.Text = $"{capture.Source}  •  {capture.ShopLabel}  •  {capture.ObservedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}\n" +
            (capture.Source == "Demo" ? "ÖRNEK VERİ — gerçek pazar değildir. " : "") +
            $"Pazar sahibi: {capture.OwnerName ?? "Bilinmiyor"}. " + string.Join(" ", capture.Warnings.Where(w => !w.Contains("+0x0A / +0x0E")));
        ApplyFilter(); ShowDetails();
    }
    private void ApplyFilter()
    {
        var text = filter.Text.Trim();
        products.ItemsSource = itemRows.Where(r => text.Length == 0 || $"{r.Vnum} {r.Shop} {r.Owner} {r.BonusSummary} {r.Source}".Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    private void ShowDetails()
    {
        if (products.SelectedItem is ItemRow item)
            details.Text = $"Vnum {item.Vnum} | Adet {item.Count} | Sekme {item.Tab + 1}, yuva {item.Slot + 1}\n" +
                $"Yang (+0x04): {item.RawPriceA} | Won (+0x0A): {item.RawPriceB} | Adet (+0x08): {item.Count}\nPazar sahibi: {item.Owner}\n" +
                $"Görünüm vnum: {item.ChangeLookVnum} | Set: {item.SetValue} | Sungma: {item.SungmaScore} | Yin/Yang: {item.YinYangType}/{item.YinYangValue}\nElement ham alanı: {Convert.ToHexString(item.RefineElementRaw)}\n" +
                $"Soketler (ham): {string.Join(", ", item.Sockets)}\n" +
                $"Efsunlar (tür/değer/5. bayt): {string.Join("; ", item.Bonuses.Select(b => $"{b.Type}/{b.Value}/0x{b.Unknown:X2}"))}\n" +
                $"Ek alanlar (tür/ham değer): {string.Join("; ", item.Extras.Select(b => $"{b.Type}/{b.RawValue}"))}\n" +
                $"Ham SHA-256: {ItemDecoder.Hash(item.Raw)}\nHam 128 bayt:\n{Convert.ToHexString(item.Raw)}";
        else details.Text = current == null ? "Bir kayıt ve ürün seç." :
            $"Kayıt: {current.Id}\nKaynak: {current.Source} | Sunucu: {current.Server} | Harita: {current.Map} | Kanal: {current.Channel} (doğrulandı: {current.ChannelVerified})\n" +
            $"Pazar VID: {current.ShopVid ?? "Bilinmiyor"} | Sahip: {current.OwnerName ?? "Bilinmiyor"} | Profil: {current.ProfileVersion}\n" +
            $"{current.Slots.Count} yuva, {itemRows.Count} dolu ürün. Boş yuvalar da veritabanında saklandı.\n" +
            "Ayrıntılar için ürün seç. Eşya adı/temel özellik tanım tablosu henüz bulunmuyor; vnum gösteriliyor.";
    }
    private void Refresh()
    {
        summary.Text = $"{store.CompactListingCount:N0} izlenen ilan • {store.TradeObservationCount:N0} kayıt sürümü • {store.TradePageCount:N0} sayfa • {new FileInfo(store.FilePath).Length/1048576d:F1} MB";
        captures.ItemsSource = store.Captures(); events.ItemsSource = store.Events();
    }
    private void RefreshProcesses()
    {
        var previous = (processes.SelectedItem as ProcessOption)?.Id;
        var list = new List<ProcessOption>();
        foreach (var p in Process.GetProcesses())
        {
            using (p) try
            {
                if (p.Id != Environment.ProcessId && !p.ProcessName.StartsWith("Metin2Search", StringComparison.OrdinalIgnoreCase)
                    && p.ProcessName.Contains("metin", StringComparison.OrdinalIgnoreCase)) list.Add(new(p.Id, p.ProcessName));
            }
            catch (Win32Exception) { }
        }
        processes.ItemsSource = list.OrderBy(p => p.Name).ToList();
        processes.SelectedItem = list.FirstOrDefault(p => p.Id == previous) ?? list.FirstOrDefault();
    }
    private void ImportDialog()
    {
        var dialog = new OpenFileDialog { Filter = "Pazar görüntüsü (*.json)|*.json", Multiselect = true };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) Safe(() => Import(path));
    }
    private void Import(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("JSON dosyası en fazla 16 MB olabilir.");
        var raw = File.ReadAllText(path);
        var capture = JsonData.Decode<MarketCapture>(raw); capture.Validate();
        capture = capture with { Id = "import-" + ItemDecoder.Hash(Encoding.UTF8.GetBytes(raw)), Source = "Import", ImportedJson = raw };
        SaveAndShow(capture);
    }
    private void ChooseInbox()
    {
        var dialog = new OpenFolderDialog { Title = "Pazar JSON kayıtlarının geleceği yerel klasör" };
        if (dialog.ShowDialog(this) != true) return;
        inbox = dialog.FolderName; importedFiles.Clear(); inboxTimer.Start(); SetStatus($"Gelen JSON klasörü izleniyor: {inbox}"); PollInbox();
    }
    private void PollInbox()
    {
        if (inbox == null || closed) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(inbox, "*.json"))
            {
                var info = new FileInfo(path);
                if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromSeconds(2)) continue;
                var stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                if (importedFiles.TryGetValue(path, out var old) && old == stamp) continue;
                try { Import(path); importedFiles[path] = stamp; }
                catch (InvalidDataException error) { importedFiles[path] = stamp; Fail(error); }
                catch (System.Text.Json.JsonException error) { importedFiles[path] = stamp; Fail(error); }
                break;
            }
        }
        catch (Exception error) { inboxTimer.Stop(); Fail(error); }
    }
    private void ExportJson()
    {
        if (current == null) throw new InvalidOperationException("Önce bir kayıt seç.");
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = $"market-{current.Id}.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, JsonData.Encode(current));
    }
    private void ExportCsv()
    {
        if (current == null) throw new InvalidOperationException("Önce bir kayıt seç.");
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"market-{current.Id}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var output = new StringBuilder("capture_id,source,observed_at,server,channel,map,shop,owner,tab,slot,vnum,count,prices_verified,won,yang,raw_price_a,raw_price_b,sockets,bonuses,extras,raw_hex\r\n");
        foreach (var row in current.Slots.Select(s => ItemDecoder.Decode(current, s)))
        {
            var values = new object?[] { current.Id, current.Source, current.ObservedAt.ToString("O"), current.Server, current.Channel, current.Map, current.ShopLabel, current.OwnerName,
                row.Tab, row.Slot, row.Vnum, row.Count, row.PricesVerified, row.PricesVerified ? row.RawPriceB : null, row.PricesVerified ? row.RawPriceA : null,
                row.RawPriceA, row.RawPriceB, JsonData.Encode(row.Sockets), JsonData.Encode(row.Bonuses), JsonData.Encode(row.Extras), Convert.ToHexString(row.Raw) };
            output.AppendLine(string.Join(",", values.Select(v => Csv(v?.ToString() ?? ""))));
        }
        File.WriteAllText(dialog.FileName, output.ToString(), new UTF8Encoding(true));
    }
    private static string Csv(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private void Backup()
    {
        var dialog = new SaveFileDialog { Filter = "SQLite yedek|*.sqlite", FileName = $"market-backup-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite" };
        if (dialog.ShowDialog(this) == true) { store.Backup(dialog.FileName); SetStatus("Tutarlı yerel yedek oluşturuldu."); }
    }
    private void LoadProfile()
    {
        if (busy || scanTask is { IsCompleted: false }) throw new InvalidOperationException("Önce okumayı durdur.");
        var dialog = new OpenFileDialog { Filter = "Okuyucu profili|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var candidate = JsonData.Decode<ReaderProfile>(File.ReadAllText(dialog.FileName)); candidate.Validate();
        profileEditor.Text = JsonData.Encode(candidate); reader?.Dispose(); reader = null; UpdateButtons();
    }
    private void SaveProfile()
    {
        var candidate = JsonData.Decode<ReaderProfile>(profileEditor.Text); candidate.Validate();
        var dialog = new SaveFileDialog { Filter = "Okuyucu profili|*.json", FileName = "reader-profile.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, JsonData.Encode(candidate));
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { Fail(error); } }
    private void Fail(Exception error)
    {
        if(closed)return;
        try { scanCts?.Cancel(); } catch (ObjectDisposedException) { }
        SetStatus(error.Message, true);
        try { if (error is MarketReadException readError) store.Diagnostic(readError); store.Event("Error", error.Message); Refresh(); }
        catch (Exception diskError) { status.Text += $" | Yerel günlük de yazılamadı: {diskError.Message}"; }
    }
    private void SetStatus(string text, bool error = false) { status.Text = text; status.Foreground = error ? Color("#FFBA99") : Accent; }
    private void UpdateButtons()
    {
        var running = scanTask is { IsCompleted: false };
        attach.IsEnabled = !busy && !running; once.IsEnabled = reader != null && !busy && !running;
        start.IsEnabled = reader?.CanMonitor == true && !busy && !running; stop.IsEnabled = running;
        automatic.IsEnabled = !busy && !running; discover.IsEnabled = !busy && !running;
        profileEditor.IsEnabled = !busy && !running; processes.IsEnabled = !busy && !running;
        tradeStart.IsEnabled = !busy && !running; tradeStop.IsEnabled = running; tradeProcesses.IsEnabled = !running;
        saleWatch.IsEnabled=!busy&&!running;
        savedServers.IsEnabled=!busy&&!running;
        UpdateCycleControls(running);
    }
    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        if (busy) { e.Cancel = true; SetStatus("Devam eden bağlantı/okuma işleminin bitmesini bekle."); return; }
        if (scanTask is { IsCompleted: false })
        {
            e.Cancel = true; scanCts?.Cancel(); await scanTask; Close(); return;
        }
        closed = true; inboxTimer.Stop(); reader?.Dispose(); scanCts?.Dispose(); store.Dispose();
    }

    private void AddStyles()
    {
        var textbox = new Style(typeof(TextBox)); textbox.Setters.Add(new Setter(Control.BackgroundProperty, PanelBg)); textbox.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        textbox.Setters.Add(new Setter(Control.BorderBrushProperty, Color("#334155"))); textbox.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))); Resources.Add(typeof(TextBox), textbox);
        var button = new Style(typeof(Button)); button.Setters.Add(new Setter(Control.BackgroundProperty, PanelBg)); button.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, Color("#3A4D63"))); Resources.Add(typeof(Button), button);
        var tab = new Style(typeof(TabItem)); tab.Setters.Add(new Setter(Control.BackgroundProperty, PanelBg)); tab.Setters.Add(new Setter(Control.ForegroundProperty, Text)); tab.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 9, 16, 9)));
        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.ForegroundProperty, Accent)); tab.Triggers.Add(selected); Resources.Add(typeof(TabItem), tab);
        var header = new Style(typeof(DataGridColumnHeader)); header.Setters.Add(new Setter(Control.BackgroundProperty, Color("#223045"))); header.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 10, 10, 10))); header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); Resources.Add(typeof(DataGridColumnHeader), header);
        var cell = new Style(typeof(DataGridCell)); cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 8, 9, 8))); cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); Resources.Add(typeof(DataGridCell), cell);
        cell.Setters.Add(new Setter(Control.ForegroundProperty,Text));cell.Setters.Add(new Setter(Control.BackgroundProperty,PanelBg));
        var cellSelected=new Trigger{Property=DataGridCell.IsSelectedProperty,Value=true};cellSelected.Setters.Add(new Setter(Control.BackgroundProperty,Color("#30445C")));cellSelected.Setters.Add(new Setter(Control.ForegroundProperty,Text));cell.Triggers.Add(cellSelected);
        var checkStyle=new Style(typeof(CheckBox));checkStyle.Setters.Add(new Setter(Control.ForegroundProperty,Text));Resources.Add(typeof(CheckBox),checkStyle);
        var dark=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="ComboBox">
                <Setter Property="Background" Value="#182130"/><Setter Property="Foreground" Value="#E8EEF5"/><Setter Property="MinHeight" Value="34"/>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
                  <Grid><ToggleButton Foreground="{TemplateBinding Foreground}" Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}">
                    <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#223045" BorderBrush="#3A4D63" BorderThickness="1" CornerRadius="5" Padding="10,7"><DockPanel><TextBlock DockPanel.Dock="Right" Text="▾" Foreground="#A2B2C8"/><ContentPresenter Content="{Binding SelectionBoxItem,RelativeSource={RelativeSource AncestorType=ComboBox}}" Margin="0,0,12,0"/></DockPanel></Border></ControlTemplate></ToggleButton.Template>
                  </ToggleButton><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="#182130" BorderBrush="#3A4D63" BorderThickness="1" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="280"><ItemsPresenter/></ScrollViewer></Border></Popup></Grid>
                </ControlTemplate></Setter.Value></Setter>
              </Style>
              <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="#E8EEF5"/><Setter Property="Background" Value="#182130"/><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Row" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="#30445C"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType="TabControl"><Setter Property="Background" Value="#182130"/><Setter Property="BorderBrush" Value="#223045"/></Style>
              <Style TargetType="ContextMenu"><Setter Property="Background" Value="#182130"/><Setter Property="Foreground" Value="#E8EEF5"/></Style>
            </ResourceDictionary>
            """);
        Resources.MergedDictionaries.Add(dark);
        button.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button"><Border x:Name="Box" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="Background" Value="#30445C"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Box" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
            """)));
        tab.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TabItem"><Border x:Name="Box" Background="#182130" Padding="{TemplateBinding Padding}" BorderThickness="0,0,0,2" BorderBrush="Transparent"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="Box" Property="Background" Value="#223045"/><Setter TargetName="Box" Property="BorderBrush" Value="#67E8BC"/><Setter Property="Foreground" Value="#67E8BC"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
            """)));
    }
    private static Brush Color(string hex) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
    private static TextBlock Label(string text, Brush? brush = null, double size = 13, FontWeight? weight = null) => new() { Text = text, Foreground = brush ?? Text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 5), MinHeight = 32 };
    private Button Action(string text, Action action) { var b = Button(text); b.Click += (_, _) => Safe(action); return b; }
    private static TabItem Tab(string label, UIElement content) => new() { Header = label, Content = content };
    private static DataGrid Grid() => new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
        Background = PanelBg, Foreground = Text, RowBackground = PanelBg, AlternatingRowBackground = Color("#1B2738"), BorderThickness = new Thickness(0),
        GridLinesVisibility = DataGridGridLinesVisibility.None, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single,
        EnableRowVirtualization = true, EnableColumnVirtualization = true, MinRowHeight = 35 };
    private static void Column(DataGrid grid, string header, string binding, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding), Width = width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width) });
    private static void ContextField(Panel parent, string label, TextBox field, string value)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 15, 0) }; group.Children.Add(Label(label, Muted, 12));
        field.Width = 220; field.Text = value; field.Margin = new Thickness(0, 5, 0, 0); group.Children.Add(field); parent.Children.Add(group);
    }
    private sealed record ProcessOption(int Id, string Name, DateTime? Started = null) { public override string ToString() => Started is { } at ? $"{Name}  •  PID {Id}  •  {at:HH:mm}" : $"{Name}  •  PID {Id}"; }
}




