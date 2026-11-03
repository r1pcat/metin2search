using System.Text.Json;

namespace Metin2Search.Local;

public sealed record GameState(string Phase, bool PlayerReady, bool InventoryReady, bool GlassOpen, int GlassSlot, string ServerInfo, bool LoginTarget, int Channel = 1,int CharacterSlot=-1,string Character="")
{
    public bool InWorld => Phase == "game" && PlayerReady && Channel == 1;
    public static GameState From(JsonElement json)
    {
        bool Flag(string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        return new GameState(
            json.TryGetProperty("phase", out var phase) ? phase.GetString() ?? "unknown" : "unknown",
            Flag("playerReady"), Flag("inventoryReady"), Flag("glassOpen"),
            json.TryGetProperty("glassSlot", out var slot) && slot.ValueKind == JsonValueKind.Number ? slot.GetInt32() : -1,
            json.TryGetProperty("serverInfo", out var info) ? info.GetString() ?? "" : "", Flag("loginTarget"),
            json.TryGetProperty("channel", out var channel) && channel.ValueKind == JsonValueKind.Number ? channel.GetInt32() : 1,
            json.TryGetProperty("characterSlot",out var characterSlot)&&characterSlot.ValueKind==JsonValueKind.Number?characterSlot.GetInt32():-1,
            json.TryGetProperty("character",out var character)?character.GetString()??"":"");
    }
}

public sealed record SessionTiming
{
    public TimeSpan ServerSettle { get; init; } = TimeSpan.FromMilliseconds(1800);
    public TimeSpan ChannelSettle { get; init; } = TimeSpan.FromMilliseconds(2200);
    public TimeSpan CharacterSettle { get; init; } = TimeSpan.FromMilliseconds(3500);
    public TimeSpan WorldSettle { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan PhaseTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan GlassTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan[] AttemptBackoff { get; init; } = [TimeSpan.Zero, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(120)];
    public int GlassTries { get; init; } = 3;
}

public interface ISessionGuard
{
    Task<bool> IsLostAsync(CancellationToken token);
    Task RecoverAsync(Action<string> progress, CancellationToken token);
}

public sealed class SessionException(string message) : IOException(message);

public sealed class SessionGuard : ISessionGuard
{
    private readonly ITradeBridge bridge;
    private readonly Func<bool> autoLogin;
    private readonly Func<int> characterSlot;
    private readonly string expectedServer;
    private readonly SessionTiming timing;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Action<string, string>? log;
    private readonly Func<bool> requireGlass;
    private readonly Func<JsonElement,int>? resolveCharacter;
    private readonly Func<string>? expectedCharacter;

    public SessionGuard(ITradeBridge bridge, string expectedServer, Func<bool> autoLogin, Func<int> characterSlot,
        SessionTiming? timing = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string, string>? log = null,Func<bool>? requireGlass=null,Func<JsonElement,int>? resolveCharacter=null,Func<string>? expectedCharacter=null)
    {
        this.bridge = bridge; this.expectedServer = expectedServer.Trim(); this.autoLogin = autoLogin; this.characterSlot = characterSlot;
        this.timing = timing ?? new SessionTiming(); this.delay = delay ?? Task.Delay; this.log = log;
        this.requireGlass=requireGlass??(()=>true);
        this.resolveCharacter=resolveCharacter;this.expectedCharacter=expectedCharacter;
    }

    public int Drops { get; private set; }

    public static async Task<bool> NeedsRecoveryAsync(ITradeBridge bridge, CancellationToken token) =>
        !(GameState.From(await bridge.Command("gamestate", token)) is { InWorld: true, GlassOpen: true });

    private TimeSpan now;
    private async Task Wait(TimeSpan span, CancellationToken token) { await delay(span, token); now += span; }

    private async Task<GameState> State(CancellationToken token) => GameState.From(await bridge.Command("gamestate", token));

    internal static async Task PrepareLoginTargetAsync(ITradeBridge bridge,string server,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(server))throw new SessionException("Otomatik giriş için son sunucu bilgisi yok.");
        var target=await bridge.Command("loginprepare "+Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(server)),token);
        if(!target.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True)
            throw new SessionException("Giriş hedefi çözülemedi: "+(target.TryGetProperty("reason",out var reason)?reason.GetString():"sunucu/kanal eşleşmedi"));
    }

    public async Task<bool> IsLostAsync(CancellationToken token)
    {
        try
        {
            var state = await State(token);
            return !(state.InWorld && state.GlassOpen);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    public async Task RecoverAsync(Action<string> progress, CancellationToken token)
    {
        Drops++;
        log?.Invoke("SessionLost", "Oyun bağlantısı veya Ticaret Camı kaybı algılandı; tarama bekletiliyor");
        var attempts = 0; var glassTries = 0; var lastPhase = ""; var phaseSince = TimeSpan.Zero;
        var nextAttemptAt = TimeSpan.Zero; var worldSeen = false; now = TimeSpan.Zero;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            GameState state;
            try { state = await State(token); }
            catch (Exception error) when (error is TradeQueryException or JsonException) { progress("Oyun durumu okunamadı: " + error.Message); await Wait(timing.Poll, token); continue; }
            if (state.Phase != lastPhase) { lastPhase = state.Phase; phaseSince = now; worldSeen = false; }
            if(state.InWorld&&!string.IsNullOrEmpty(expectedCharacter?.Invoke())&&(!state.InventoryReady||state.Character.Length==0)){
                if(now-phaseSince>=timing.PhaseTimeout)throw new SessionException("Karakter kimliği/envanter yüklenmesi zaman aşımına uğradı; eşya işlemi yapılmadı.");
                progress("Karakter kimliği ve envanterin yüklenmesi bekleniyor");await Wait(timing.Poll,token);continue;
            }
            if (state.InWorld && (state.GlassOpen||!requireGlass()))
            {
                CheckServer(state);
                log?.Invoke("SessionRestored", $"Ticaret Camı açık; tarama kaldığı yerden devam ediyor (deneme {attempts}, cam {glassTries})");
                return;
            }
            switch (state.Phase)
            {
                case "game" when state.PlayerReady && state.Channel != 1:
                    progress("Kanal bilgisi bekleniyor"); await Wait(timing.Poll, token);
                    break;
                case "game" when state.PlayerReady:
                    CheckServer(state);
                    if (!worldSeen) { worldSeen = true; phaseSince = now; }
                    if ((now - phaseSince) < timing.WorldSettle || !state.InventoryReady) { progress("Dünya yükleniyor; Ticaret Camı için bekleniyor"); await Wait(timing.Poll, token); break; }
                    if (state.GlassSlot < 0) throw new SessionException("Envanterde Ticaret Camı bulunamadı; tarama devam edemiyor.");
                    if (glassTries >= timing.GlassTries) throw new SessionException("Ticaret Camı birkaç denemede de açılmadı; elle açıp yeniden Başlat.");
                    glassTries++;
                    progress($"Ticaret Camı kullanılıyor ({glassTries}/{timing.GlassTries})");
                    try { await bridge.Command("useglass", token); }
                    catch (TradeQueryException error) { progress("Ticaret Camı kullanılamadı: " + error.Message); await Wait(timing.Poll, token); break; }
                    for (var waited = TimeSpan.Zero; waited < timing.GlassTimeout; waited += TimeSpan.FromMilliseconds(500))
                    {
                        await Wait(TimeSpan.FromMilliseconds(500), token);
                        if ((await State(token)) is { InWorld: true, GlassOpen: true }) break;
                    }
                    break;
                case "login":
                    if (!autoLogin()) { progress("Giriş ekranı: otomatik giriş kapalı, elle giriş bekleniyor"); await Wait(timing.Poll * 3, token); break; }
                    if (!state.LoginTarget) await PrepareLoginTargetAsync(bridge,expectedServer,token);
                    if (attempts >= timing.AttemptBackoff.Length) { progress("Otomatik giriş denemeleri bitti; elle giriş bekleniyor"); await Wait(timing.Poll * 3, token); break; }
                    if (now < nextAttemptAt) { progress($"Giriş denemesi {attempts + 1}/{timing.AttemptBackoff.Length} için bekleniyor"); await Wait(timing.Poll, token); break; }
                    attempts++;
                    nextAttemptAt = now + (attempts < timing.AttemptBackoff.Length ? timing.AttemptBackoff[attempts] : TimeSpan.Zero);
                    progress($"Otomatik giriş {attempts}/{timing.AttemptBackoff.Length}: sunucu seçiliyor");
                    await bridge.Command("loginserver", token); await Wait(timing.ServerSettle, token);
                    progress($"Otomatik giriş {attempts}: kanal seçiliyor");
                    await bridge.Command("loginchannel", token); await Wait(timing.ChannelSettle, token);
                    progress($"Otomatik giriş {attempts}: bağlanılıyor");
                    await bridge.Command("loginconnect", token);
                    await WaitPhaseChange("login", progress, token);
                    break;
                case "select":
                    if (!autoLogin()) { progress("Karakter ekranı: elle giriş bekleniyor"); await Wait(timing.Poll * 3, token); break; }
                    if ((now - phaseSince) < timing.CharacterSettle) { progress("Karakter listesi yerleşiyor"); await Wait(timing.Poll, token); break; }
                    var target=Math.Clamp(characterSlot(),0,4);var displayTarget=target;
                    if(resolveCharacter is not null){
                        var list=await bridge.Command("characterlist",token);target=resolveCharacter(list);displayTarget=target;
                        if(list.TryGetProperty("displaySlots",out var display))displayTarget=Array.IndexOf(display.EnumerateArray().Select(v=>v.GetInt32()).ToArray(),target);
                    }
                    if(target<0||target>=5)throw new SessionException("Hedef karakterin gerçek yuvası bulunamadı.");
                    progress($"Döngü karakteri {characterSlot()+1} seçiliyor • ekrandaki sıra {displayTarget+1}, gerçek yuva {target+1}");
                    await bridge.Command($"loginenter {target}", token);
                    await WaitPhaseChange("select", progress, token);
                    break;
                default:
                    progress(state.Phase == "loading" || state.Phase == "game" ? "Oyun yükleniyor" : $"Oyun ekranı bekleniyor ({state.Phase})");
                    await Wait(timing.Poll * 2, token);
                    break;
            }
        }
    }

    private void CheckServer(GameState state)
    {
        if (expectedServer.Length > 0 && state.ServerInfo.Trim().Length > 0 && !string.Equals(state.ServerInfo.Trim(), expectedServer, StringComparison.OrdinalIgnoreCase))
            throw new SessionException($"Girilen sunucu/kanal farklı ({state.ServerInfo.Trim()} ≠ {expectedServer}); veriler karışmaması için tarama durdu.");
        var name=expectedCharacter?.Invoke()??"";
        if(name.Length>0&&state.Character!=name)throw new SessionException("Giriş yapılan karakter hedef kimlikle eşleşmedi; cam/depo işlemi yapılmadı.");
    }

    private async Task WaitPhaseChange(string phase, Action<string> progress, CancellationToken token)
    {
        var waited = TimeSpan.Zero;
        while (waited < timing.PhaseTimeout)
        {
            await Wait(timing.Poll, token); waited += timing.Poll;
            var state = await State(token);
            if (state.Phase != phase) return;
        }
        progress("Ekran değişmedi; yeniden denenecek");
    }
}
