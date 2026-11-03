using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Metin2Search.Local;

public interface IMarketClient
{
    bool NativeLayoutValidated { get; }
    ReaderProfile Profile { get; }
    NativeShopState ReadNativeState();
    List<ShopActor> ReadShops();
    Vector3 PlayerPosition();
    ProjectionState Projection();
    Task<MarketCapture> CaptureAsync(string server, string map, string label, bool monitored, CancellationToken token);
}

public interface IGameInteraction
{
    void Focus();
    void CheckActive();
    void Click(Vector2 clientPoint, ProjectionState projection);
    void Escape();
}

public sealed class GameInteraction(IntPtr window) : IGameInteraction
{
    public void Focus()
    {
        if (window == IntPtr.Zero || !IsWindow(window)) throw new InvalidOperationException("Metin2 oyun penceresi bulunamadı.");
        ShowWindow(window, 9);
        if (!SetForegroundWindow(window) && GetForegroundWindow() != window)
            throw new InvalidOperationException("Oyun penceresi etkinleştirilemedi; otomatik tarama başlamadı.");
    }
    public void CheckActive()
    {
        if ((GetAsyncKeyState(0x77) & 0x8000) != 0) throw new OperationCanceledException("F8 ile durduruldu.");
        if (GetForegroundWindow() != window) throw new OperationCanceledException("Oyun odağı değişti; otomatik tarama durdu.");
    }
    public void Click(Vector2 point, ProjectionState projection)
    {
        CheckActive();
        if (!GetClientRect(window, out var rect) || Math.Abs(rect.Right - projection.Width) > 2 || Math.Abs(rect.Bottom - projection.Height) > 2)
            throw new InvalidOperationException("Oyun penceresi ile görüntü alanı boyutları uyuşmuyor. Tıklama yapılmadı.");
        if (point.X < 90 || point.X > rect.Right - 90 || point.Y < 90 || point.Y > rect.Bottom - 150)
            throw new InvalidOperationException("Pazar/rota noktası güvenilir tıklama alanının dışında.");
        var screen = new Point { X = (int)point.X, Y = (int)point.Y };
        if (!ClientToScreen(window, ref screen) || !SetCursorPos(screen.X, screen.Y)) throw new InvalidOperationException("Oyun koordinatına gidilemedi.");
        CheckActive();
        var input = new[] { new Input { Type = 0, Data = new InputUnion { Mouse = new Mouse { Flags = 0x0002 } } },
                            new Input { Type = 0, Data = new InputUnion { Mouse = new Mouse { Flags = 0x0004 } } } };
        if (SendInput(2, input, Marshal.SizeOf<Input>()) != 2) throw new InvalidOperationException("Oyun tıklaması gönderilemedi; tarama durdu.");
    }
    public void Escape()
    {
        CheckActive();
        var input = new[] { new Input { Type = 1, Data = new InputUnion { Keyboard = new Keyboard { VirtualKey = 0x1b } } },
                            new Input { Type = 1, Data = new InputUnion { Keyboard = new Keyboard { VirtualKey = 0x1b, Flags = 2 } } } };
        if (SendInput(2, input, Marshal.SizeOf<Input>()) != 2) throw new InvalidOperationException("Pazar kapatma tuşu gönderilemedi.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
}

public sealed class AutomaticScanner(IMarketClient reader, IGameInteraction input, Action<MarketCapture> save,
    Action<string, string> log, Action<string> status, Action<MarketReadException> diagnostic)
{
    private readonly Dictionary<(uint, string), DateTimeOffset> visits = [];
    private int routeIndex;
    private string? lastDiscoveryHash;
    public async Task Run(string server, string map, CancellationToken token)
    {
        if (!reader.NativeLayoutValidated) throw new InvalidOperationException("İstemci sürümü doğrulanmadan otomatik tarama açılamaz.");
        _ = reader.ReadNativeState();
        input.Focus();
        await Wait(350, token);
        _ = reader.ReadShops();
        var ownScreen = MarketGeometry.Project(reader.PlayerPosition(), reader.Projection());
        if (ownScreen == null) throw new InvalidOperationException("Ana karakterin ekran izdüşümü doğrulanamadı; otomatik tıklama başlamadı.");
        status("Otomatik tarama başladı. F8 veya oyun odağını değiştirmek durdurur.");
        log("AutoStart", "Kanal 1; yüklenmiş pazarların keşfi ve normal fare tıklamasıyla ziyaret.");
        while (true)
        {
            await Wait(100, token); input.CheckActive();
            _ = reader.ReadNativeState();
            var shops = await Discover(token);
            var discoveryJson = JsonData.Encode(shops.OrderBy(s => s.Vid).ToArray());
            var discoveryHash = ItemDecoder.Hash(System.Text.Encoding.UTF8.GetBytes(discoveryJson));
            if (discoveryHash != lastDiscoveryHash)
            {
                log("AutoDiscovery", discoveryJson); lastDiscoveryHash = discoveryHash;
            }
            var now = DateTimeOffset.UtcNow; var position = reader.PlayerPosition();
            var eligible = shops.Where(s => !visits.TryGetValue((s.Vid, s.Name), out var last) || now - last > TimeSpan.FromSeconds(reader.Profile.RevisitSeconds))
                .OrderBy(s => Vector3.DistanceSquared(s.Position, position)).ToList();
            ShopActor? target = null;
            var projection = reader.Projection();
            foreach (var shop in eligible)
            {
                var point = MarketGeometry.Project(shop.Position + new Vector3(0, 0, 30), projection);
                if (point is { } p && p.X >= 90 && p.Y >= 90 && p.X <= projection.Width - 90 && p.Y <= projection.Height - 150) { target = shop; break; }
            }
            if (target == null)
            {
                if (reader.Profile.ExplorationRoute.Count > 0)
                {
                    await CloseShop(token);
                    var waypoint = reader.Profile.ExplorationRoute[routeIndex++ % reader.Profile.ExplorationRoute.Count];
                    status($"Keşif rotası: {waypoint.X:N0}, {waypoint.Y:N0}");
                    await MoveTo(waypoint, token);
                }
                else { status($"{shops.Count} yüklenmiş pazar. Yeniden ziyaret bekleniyor; görünmeyen bölgeler için keşif rotası gerekli."); await Wait(2000, token); }
                continue;
            }
            status($"Pazar açılıyor: {target.DisplayName} • VID {target.Vid}");
            log("Visit", $"{target.DisplayName} | VID {target.Vid}");
            await CloseShop(token);
            var opened = await OpenShop(target, token);
            visits[(target.Vid, target.Name)] = DateTimeOffset.UtcNow;
            if (!opened) { log("Deferred", $"{target.Name} | VID {target.Vid} | Pazar açılamadı veya hedef eşleşmedi."); continue; }
            var capture = await CaptureExpected(target, server, map, token);
            save(capture);
            await CloseShop(token);
        }
    }
    private async Task<List<ShopActor>> Discover(CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return reader.ReadShops(); }
            catch (InvalidDataException) when (attempt < 2) { await Wait(250, token); }
        }
    }
    private async Task<bool> OpenShop(ShopActor original, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Wait(200, token);
            var state = reader.ReadNativeState();
            if (state.Open == 1) return state.Private == 1 && state.TargetVid == original.Vid;
            var target = (await Discover(token)).SingleOrDefault(s => s.Vid == original.Vid && s.Instance == original.Instance &&
                (!original.NameKnown || !s.NameKnown || s.Name == original.Name));
            if (target == null) return false;
            var projection = reader.Projection();
            var point = MarketGeometry.Project(target.Position + new Vector3(0, 0, 30), projection);
            if (point == null) return false;
            if (point.Value.X < 90 || point.Value.X > projection.Width - 90 || point.Value.Y < 90 || point.Value.Y > projection.Height - 150) return false;
            if (reader.ReadNativeState().Open != 0) continue;
            input.Click(point.Value, projection);
            await Wait(900, token);
        }
        return false;
    }
    private async Task<MarketCapture> CaptureExpected(ShopActor target, string server, string map, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Wait(350, token);
                var capture = await reader.CaptureAsync(server, map, $"Pazar VID {target.Vid}", true, token);
                if (capture.ShopVid != target.Vid.ToString() ||
                    (target.NameKnown && capture.OwnerVerified && capture.OwnerName != target.Name))
                    throw new InvalidDataException("Açılan pazarın VID/sahip bilgisi hedefle eşleşmedi; kayıt durduruldu.");
                return capture;
            }
            catch (MarketReadException error) when (attempt < 2) { diagnostic(error); log("Retry", error.Message); }
        }
    }
    private async Task CloseShop(CancellationToken token)
    {
        if (reader.ReadNativeState().Open == 0) return;
        input.Escape();
        for (var i = 0; i < 20; i++) { await Wait(100, token); if (reader.ReadNativeState().Open == 0) return; }
        throw new InvalidOperationException("Pazar kapanmadı. Yeni tıklama yapılmadan tarama durdu.");
    }
    private async Task MoveTo(RoutePoint waypoint, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Wait(100, token);
            if (reader.ReadNativeState().Open != 0) throw new InvalidOperationException("Keşif yürüyüşünde beklenmedik pazar açıldı; tıklama durdu.");
            var position = reader.PlayerPosition();
            var delta = new Vector2(waypoint.X - position.X, waypoint.Y - position.Y);
            if (delta.Length() < 150) return;
            var step = Vector2.Normalize(delta) * Math.Min(350, delta.Length());
            var projection = reader.Projection();
            var screen = MarketGeometry.Project(new Vector3(position.X + step.X, position.Y + step.Y, position.Z), projection);
            if (screen == null) { log("RouteDeferred", "Rota noktası izdüşümü görünmüyor."); return; }
            input.Click(screen.Value, projection); await Wait(900, token);
        }
        log("RouteDeferred", "Rota noktasına 30 saniyede ulaşılamadı; engel olabilir.");
    }
    private async Task Wait(int milliseconds, CancellationToken token)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 50)
        {
            token.ThrowIfCancellationRequested(); input.CheckActive();
            await Task.Delay(Math.Min(50, milliseconds - elapsed), token);
        }
    }
}
