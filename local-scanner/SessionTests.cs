using System.Buffers.Binary;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class SessionTests
{
    private sealed class Game : ITradeBridge
    {
        public string Phase = "login", ServerInfo = "Safir, CH1";
        public bool PlayerReady, GlassOpen, LoginTarget = true;
        public bool FailTarget;
        public int GlassSlot = 5, LoadPolls;
        public List<string> Commands { get; } = [];
        public Func<string, bool>? FailLogin;
        public Task<JsonElement> Command(string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Commands.Add(command); object result = new { ok = true };
            if (command == "gamestate")
            {
                if (Phase == "loading" && --LoadPolls <= 0) { Phase = "game"; PlayerReady = true; }
                result = new { phase = Phase, playerReady = PlayerReady && Phase == "game", inventoryReady = PlayerReady && Phase == "game", glassOpen = GlassOpen && Phase == "game", glassSlot = PlayerReady ? GlassSlot : -1, serverInfo = ServerInfo, channel = 1, loginTarget = LoginTarget };
            }
            else if(command.StartsWith("loginprepare ")){LoginTarget=!FailTarget;result=new{ok=LoginTarget,reason="Test target missing"};}
            else if (command is "loginserver" or "loginchannel") { if (Phase != "login") throw new TradeQueryException("Giris ekrani degil: " + Phase); }
            else if (command == "loginconnect") { if (Phase != "login") throw new TradeQueryException("Giris ekrani degil"); if (FailLogin?.Invoke(command) != true) Phase = "select"; }
            else if (command.StartsWith("loginenter ")) { if (Phase != "select") throw new TradeQueryException("Karakter secim ekrani degil"); Phase = "loading"; LoadPolls = 3; }
            else if (command == "useglass") { if (GlassSlot < 0) throw new TradeQueryException("Envanterde Ticaret Cami yok"); GlassOpen = true; }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static byte[] Item(uint marker)
    {
        var b = new byte[187]; b[46] = (byte)'A';
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5), marker); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13), 1); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(17), 19);
        return b;
    }
    private sealed class Market : ITradeBridge
    {
        public List<byte[]> Items = [];
        public List<string> Commands { get; } = [];
        public bool FailPage2Once = true;
        private long generation; private uint page = 1; private bool pending;
        public Task<JsonElement> Command(string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Commands.Add(command); object result;
            if (command == "inspect") result = new { generation, plan = new[] { new { job = 0, category = 1, sub = 0, label = "Savaşçı / Silahlar" } }, sort = new { recentIndex = 2, unitPriceIndex = 1, selected = 0 } };
            else if (command.StartsWith("setsort ")) result = new { sort = new { selected = int.Parse(command[8..]) } };
            else if (command.StartsWith("search ")) { page = 1; pending = true; result = new { requested = true, generation }; }
            else if (command.StartsWith("page "))
            {
                if (FailPage2Once && command == "page 2") { FailPage2Once = false; throw new TradeQueryException("Ticaret Cami kapandi"); }
                page = uint.Parse(command[5..]); pending = true; result = new { requested = true, generation };
            }
            else if (command == "snapshot")
            {
                if (pending) { pending = false; generation++; }
                var raw = Items.Skip((int)(page - 1) * 11).Take(11).SelectMany(x => x).ToArray();
                result = new { generation, count = raw.Length / 187, total = (uint)Items.Count, page, raw = Convert.ToHexString(raw) };
            }
            else if (command == "details") result = new { generation, count = 0, total = (uint)Items.Count, page, raw = "", details = Array.Empty<object>() };
            else throw new TradeQueryException("unexpected command " + command);
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeGuard(bool lost) : ISessionGuard
    {
        public bool Lost = lost; public int Recoveries;
        public Task<bool> IsLostAsync(CancellationToken token) => Task.FromResult(Lost);
        public Task RecoverAsync(Action<string> progress, CancellationToken token) { Recoveries++; Lost = false; return Task.CompletedTask; }
    }

    internal static void Run(string directory, List<string> results)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAILED: " + label); results.Add("PASS: " + label); }
        static Task NoDelay(TimeSpan span, CancellationToken token) => Task.CompletedTask;
        SessionGuard Guard(Game game, Func<bool>? auto = null, string server = "Safir, CH1", SessionTiming? timing = null) =>
            new(game, server, auto ?? (() => true), () => 2, timing, NoDelay);
        var notes = new List<string>();

        var game = new Game();
        Guard(game).RecoverAsync(notes.Add, CancellationToken.None).GetAwaiter().GetResult();
        var steps = game.Commands.Where(c => c != "gamestate").ToArray();
        Check(steps.SequenceEqual(["loginserver", "loginchannel", "loginconnect", "loginenter 2", "useglass"]) && game.GlassOpen,
            $"Düşme sonrası sıra: sunucu → kanal → bağlan → karakter (yuva 3) → Ticaret Camı kullan ({string.Join(" → ", steps)})");
        var firstUse = game.Commands.IndexOf("useglass");
        Check(game.Commands.Take(firstUse).Count(c => c == "gamestate") >= 6, "Dünya yerleşmeden Ticaret Camı kullanılmaz (yükleme + 4 sn bekleme)");
        var missingTarget=new Game{LoginTarget=false};Guard(missingTarget).RecoverAsync(_=>{},CancellationToken.None).GetAwaiter().GetResult();
        Check(missingTarget.GlassOpen&&missingTarget.Commands.Count(c=>c.StartsWith("loginprepare "))==1,"Köprü hedefi kaybolmuşsa kayıtlı sunucudan yeniden çözülüp giriş sürer");
        var badTarget=new Game{LoginTarget=false,FailTarget=true};
        try{Guard(badTarget).RecoverAsync(_=>{},CancellationToken.None).GetAwaiter().GetResult();Check(false,"Belirsiz giriş hedefi reddedilir");}
        catch(SessionException error){Check(error.Message.Contains("Test target missing")&&!badTarget.Commands.Contains("loginconnect"),"Hedef çözülemezse nedeni gösterilir ve bağlantı gönderilmez");}

        var closed = new Game { Phase = "game", PlayerReady = true, GlassOpen = false };
        Guard(closed).RecoverAsync(_ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check(closed.Commands.Count(c => c == "useglass") == 1 && !closed.Commands.Any(c => c.StartsWith("login")), "Yalnız Ticaret Camı kapanmışsa giriş adımları atlanır, eşya bir kez kullanılır");

        var manual = new Game(); var polls = 0;
        var manualGuard = new SessionGuard(new Func<Game>(() => manual)(), "Safir, CH1", () => false, () => 0, null, (_, _) => { if (++polls == 8) { manual.Phase = "game"; manual.PlayerReady = true; manual.GlassOpen = true; } return Task.CompletedTask; });
        manualGuard.RecoverAsync(_ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check(!manual.Commands.Any(c => c.StartsWith("login") || c == "useglass"), "Otomatik giriş kapalıyken hiçbir giriş adımı gönderilmez; elle giriş beklenir ve fark edilir");

        var stuck = new Game { FailLogin = _ => true }; var stuckPolls = 0;
        var stuckGuard = new SessionGuard(stuck, "Safir, CH1", () => true, () => 0, null, (_, _) => { if (++stuckPolls == 3000) { stuck.Phase = "game"; stuck.PlayerReady = true; stuck.GlassOpen = true; } return Task.CompletedTask; });
        stuckGuard.RecoverAsync(_ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check(stuck.Commands.Count(c => c == "loginconnect") == 5, "Giriş ekranından çıkılamazsa en çok 5 otomatik deneme yapılır; sonra yalnız beklenir");
        var wrongServer = new Game { Phase = "game", PlayerReady = true, GlassOpen = true, ServerInfo = "Baska, CH2" };
        try { Guard(wrongServer).RecoverAsync(_ => { }, CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Farklı sunucu reddedilir"); }
        catch (SessionException) { Check(true, "Girilen sunucu/kanal farklıysa tarama devam etmez (veriler karışmaz)"); }
        var noItem = new Game { Phase = "game", PlayerReady = true, GlassOpen = false, GlassSlot = -1 };
        try { Guard(noItem).RecoverAsync(_ => { }, CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Ticaret Camı yoksa durur"); }
        catch (SessionException error) { Check(error.Message.Contains("Ticaret Camı"), "Envanterde Ticaret Camı yoksa net hata ile durur"); }
        var cancel = new CancellationTokenSource(); var waiting = new Game();
        var cancelGuard = new SessionGuard(waiting, "Safir, CH1", () => false, () => 0, null, (_, _) => { cancel.Cancel(); return Task.CompletedTask; });
        try { cancelGuard.RecoverAsync(_ => { }, cancel.Token).GetAwaiter().GetResult(); Check(false, "Durdur bekleyen kurtarmayı keser"); }
        catch (OperationCanceledException) { Check(true, "Durdur'a basılınca bekleyen kurtarma hemen kesilir"); }
        var lostCheck = new Game { Phase = "game", PlayerReady = true, GlassOpen = true };
        Check(!Guard(lostCheck).IsLostAsync(CancellationToken.None).GetAwaiter().GetResult(), "Oyunda ve Ticaret Camı açıkken kayıp algılanmaz");
        lostCheck.GlassOpen = false;
        Check(Guard(lostCheck).IsLostAsync(CancellationToken.None).GetAwaiter().GetResult(), "Ticaret Camı kapanınca kayıp algılanır");

        var market = new Market(); for (uint i = 1; i <= 25; i++) market.Items.Add(Item(900 - i));
        using var store = new LocalStore(Path.Combine(directory, "session-resume")); store.ConfigureServer("Safir, CH1");
        var guard = new FakeGuard(true); using var stop = new CancellationTokenSource(); var ends = new List<string>();
        try
        {
            TradeScanner.Run(market, p => { store.SaveTrade(p); return Task.CompletedTask; }, _ => { }, stop.Token, a =>
            {
                store.TradeAudit(a);
                if (a.Kind == "RunEnd") { ends.Add(a.Message); stop.Cancel(); }
            }, session: guard).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        var sent = market.Commands.Where(c => c.StartsWith("search") || c.StartsWith("page") || c == "inspect" || c.StartsWith("setsort")).ToArray();
        Check(guard.Recoveries == 1 && sent.SequenceEqual(["inspect", "setsort 1", "search 0 1 0", "page 2", "inspect", "setsort 1", "search 0 1 0", "page 2", "page 3"]),
            $"Yarım kalan kategori kaybın ardından yeniden denenir: kurtarma → yeniden inceleme → sıralama → baştan okuma ({string.Join(" | ", sent)})");
        Check(ends.Count == 1 && ends[0].Contains("Tamamlanan 1, hatalı 0"), "Kesilen kategori hatalı sayılmaz; tur tamamlanmış bildirilir");
        var versionCount = (long)store.Query("SELECT COUNT(*) FROM compact_versions")[0][0]!;
        Check(versionCount == 25, $"Kesinti sonrası aynı ilanlar çift kaydedilmez (25 ilan; bulunan {versionCount})");

        var scopeStatus = store.Query("SELECT status,pages FROM scope_snapshots");
        Check(scopeStatus.Count == 1 && (string)scopeStatus[0][0]! == "Complete" && (long)scopeStatus[0][1]! == 3, $"Kesinti sonrası kapsam sayfa listesi tam kalır (Complete, 3 sayfa); silme kuralı bozulmaz ({(scopeStatus.Count > 0 ? scopeStatus[0][0] + "/" + scopeStatus[0][1] : "yok")})");

        var plain = new Market { FailPage2Once = true }; for (uint i = 1; i <= 25; i++) plain.Items.Add(Item(700 - i));
        var healthy = new FakeGuard(false);
        try
        {
            TradeScanner.Run(plain, p => Task.CompletedTask, _ => { }, CancellationToken.None, session: healthy).GetAwaiter().GetResult();
            Check(false, "Kayıp değilse kategori hatası eskisi gibi sayılır");
        }
        catch (IOException error) { Check(healthy.Recoveries == 0 && error.Message.Contains("Hiçbir kategori tamamlanamadı"), "Oyun sağlamken gelen kategori hatası kurtarma başlatmaz; eski davranış korunur"); }
    }
}
