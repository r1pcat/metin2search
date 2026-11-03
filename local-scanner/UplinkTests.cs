using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class UplinkTests
{
    private const string Secret = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private static readonly TradeCategory Shared = new(0, 3, 2, "Ortak / Kalkan") { CoveredCategories = [new(0, 3, 2, "Savaşçı / Kalkan"), new(1, 3, 2, "Ninja / Kalkan")] };
    private static readonly TradeCategory Other = new(2, 2, 0, "Sura / Kılıç");
    private static byte[] Raw(uint won = 1, uint marker = 1)
    {
        var b = new byte[187]; b[46] = (byte)'A';
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5), marker); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13), 1); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(17), 19); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x8b), won);
        return b;
    }
    private sealed class Producer(string directory) : IDisposable
    {
        internal readonly LocalStore Db = Create(directory);
        private string run = "";
        private static LocalStore Create(string dir) { var db = new LocalStore(dir); db.ConfigureServer("Safir, CH1"); return db; }
        private static string Q(string r, TradeCategory c) => $"{r}:{c.Job}:{c.Category}:{c.Sub}";
        private void Audit(string kind, TradeCategory c, int pages = 0, int expected = 0) { foreach (var alias in TradePlan.Covered(c)) Db.TradeAudit(new(kind, run, Q(run, alias), alias, pages, expected, kind) { PlanQueryId = Q(run, c), PlanCategory = c, PlanLabel = c.Label }); }
        internal void Round(byte[] raw)
        {
            run = Guid.NewGuid().ToString("N");
            Db.TradeAudit(new("RunStart", run, "", null, 0, 3, "{\"categoryTree\":{}}") { PlannedQueries = 2 });
            Audit("Pending", Shared); Audit("Pending", Other);
            Audit("Running", Shared);
            Db.SaveTrade(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, Shared, 1, 1u, (uint)(raw.Length / 187), raw) { RunId = run, QueryId = Q(run, Shared) });
            Audit("Complete", Shared, 1, 1); Audit("Failed", Other);
            Db.TradeAudit(new("RunEnd", run, "", null, 0, 3, "fixture"));
        }
        public void Dispose() => Db.Dispose();
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public Dictionary<(string Category, long Revision), (string BatchId, string BodyHash)> Batches { get; } = [];
        public HashSet<(string Category, long Revision)> Committed { get; } = [];
        public int PurchaseRows, PresenceRows, RunRows, BadSignatures;
        public string? Dataset;
        public int Fail; public HttpStatusCode FailStatus = HttpStatusCode.ServiceUnavailable; public string FailCode = "";
        public long ServerClockOffset; public long LastTimestamp;
        private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(token);
            string H(string n) => request.Headers.TryGetValues(n, out var v) ? v.First() : "";
            var timestamp = long.Parse(H("X-M2-Timestamp")); LastTimestamp = timestamp;
            var expected = UplinkSigner.Sign(Secret, request.Method.Method, request.RequestUri!.PathAndQuery, timestamp, H("X-M2-Nonce"), body);
            Requests.Add(request.Method.Method + " " + request.RequestUri.AbsolutePath);
            var serverNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ServerClockOffset;
            if (expected != H("X-M2-Signature")) { BadSignatures++; return Json(HttpStatusCode.Unauthorized, new { error = "İmza geçersiz", code = "auth", serverTime = serverNow }); }
            if (Math.Abs(serverNow - timestamp) > 300) return Json(HttpStatusCode.Unauthorized, new { error = "Zaman", code = "clock", serverTime = serverNow });
            if (Fail > 0) { Fail--; return Json(FailStatus, new { error = "test", code = FailCode, serverTime = serverNow }); }
            var path = request.RequestUri.AbsolutePath["/ingest/v1/".Length..];
            if (path == "ping") return Json(HttpStatusCode.OK, new { ok = true });
            if (path == "state")
            {
                var categories = Batches.Keys.GroupBy(k => k.Category).ToDictionary(g => g.Key, g => new { received = g.Max(k => k.Revision), acked = Committed.Where(c => c.Category == g.Key).Select(c => c.Revision).DefaultIfEmpty(0).Max() });
                return Json(HttpStatusCode.OK, new { server = "Safir", datasetId = Dataset, categories, purchaseChecksCursor = 0, presenceCursor = (object?)null, latestRun = (string?)null, serverTime = serverNow });
            }
            if (path == "batch")
            {
                Dataset ??= H("X-M2-Dataset");
                if (Dataset != H("X-M2-Dataset")) return Json(HttpStatusCode.Conflict, new { error = "dataset", code = "dataset" });
                var key = (H("X-M2-Category"), long.Parse(H("X-M2-Revision")));
                if (Batches.ContainsKey(key)) return Json(HttpStatusCode.OK, new { status = "duplicate" });
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant() != H("X-M2-Body-Hash")) return Json(HttpStatusCode.UnprocessableEntity, new { error = "hash", code = "invalid-batch" });
                Batches[key] = (H("X-M2-Batch"), H("X-M2-Body-Hash"));
                return Json(HttpStatusCode.OK, new { status = "stored" });
            }
            if (path == "rows")
            {
                using var gzip = new System.IO.Compression.GZipStream(new MemoryStream(body), System.IO.Compression.CompressionMode.Decompress);
                using var doc = JsonDocument.Parse(gzip); var rows = doc.RootElement.GetProperty("rows").GetArrayLength();
                switch (doc.RootElement.GetProperty("table").GetString()) { case "purchase_checks": PurchaseRows += rows; break; case "compact_presence": PresenceRows += rows; break; case "trade_runs": RunRows += rows; break; }
                return Json(HttpStatusCode.OK, new { accepted = rows });
            }
            if (path == "acks")
            {
                var serverName = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["server"];
                var acks = Committed.Select(c => new { schemaVersion = 1, datasetId = Dataset, server = serverName, categoryKey = c.Category, revision = c.Revision, batchId = Batches[c].BatchId, bodyHash = Batches[c].BodyHash });
                return Json(HttpStatusCode.OK, new { acks });
            }
            return Json(HttpStatusCode.NotFound, new { error = "yok" });
        }
    }
    private static Task NoDelay(TimeSpan span, CancellationToken token) => Task.CompletedTask;
    private static UplinkClient Client(FakeServer server, Func<DateTimeOffset>? clock = null) =>
        new(new HttpClient(server) { BaseAddress = null }, new UplinkConfig(new Uri("https://metin2search.test"), "k1", Secret), clock);

    internal static void Run(string directory, List<string> results)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAILED: " + label); results.Add("PASS: " + label); }

        Check(UplinkSigner.Sign(Secret, "POST", "/ingest/v1/batch?x=1", 1760000000, "0123456789abcdef", Encoding.UTF8.GetBytes("hello")) == "c020387e493c671f27271a1677d88cf79c703e0b609a13caef472f22b6d8ed69",
            "İmza Node alıcısıyla birebir aynı test vektörünü üretir (HMAC-SHA256)");
        Check(!UplinkConfig.Validate(new Uri("http://example.com"), "k1", Secret, out _) && UplinkConfig.Validate(new Uri("http://127.0.0.1:4181"), "k1", Secret, out _) && UplinkConfig.Validate(new Uri("https://example.com"), "k1", Secret, out _),
            "Sunucu adresi https olmak zorunda; yalnız yerel test adresi http olabilir");
        Check(!UplinkConfig.Validate(new Uri("https://example.com"), "k1", "abcd", out _) && !UplinkConfig.Validate(new Uri("https://example.com"), "k 1", Secret, out _), "Kısa gizli anahtar ve geçersiz anahtar kimliği reddedilir");

        using var producer = new Producer(Path.Combine(directory, "uplink-producer"));
        producer.Round(Raw()); producer.Round(Raw(2));
        var store = producer.Db;
        Check(store.PendingCategoryBatches().Count == 2, "Test kaynağında iki kalıcı kategori paketi var");
        store.Execute("INSERT INTO purchase_checks(payload_json) VALUES(?)", JsonSerializer.Serialize(new PurchaseCheckRecord(1, "safir", "s1", 22, "Satıcı", "req-1", "key-1", DateTimeOffset.UtcNow.ToString("O"), "Preparing", "request-and-shop", "shop", "hazırlanıyor", "fixture", true, false), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var fake = new FakeServer();
        var worker = new UplinkWorker(store, Client(fake), delay: NoDelay);
        worker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(fake.Batches.Count == 2 && fake.Batches.Keys.OrderBy(k => k.Revision).Select(k => k.Revision).SequenceEqual([1, 2]), "Bekleyen paketler revision sırasıyla gönderilir");
        Check(fake.BadSignatures == 0 && fake.PurchaseRows == 1 && fake.PresenceRows >= 1 && fake.RunRows == 1, $"Kontrol kayıtları, varlık kayıtları ve tur planı da imzalı gönderilir ({fake.PurchaseRows}/{fake.PresenceRows}/{fake.RunRows})");
        Check(store.PendingCategoryBatches().Count == 2, "Sunucu onaylamadan hiçbir paket kuyruktan çıkmaz");

        var before = fake.Requests.Count(r => r == "POST /ingest/v1/batch");
        worker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(fake.Requests.Count(r => r == "POST /ingest/v1/batch") == before && store.PendingCategoryBatches().Count == 2, "Sunucuda zaten kayıtlı paket tekrar gönderilmez; onay gelene dek kuyrukta kalır");

        store.RequireUplinkAck = true;
        var first = store.PendingCategoryBatches().First();
        var ackFile = store.CategoryAckFile(first.CategoryKey); Directory.CreateDirectory(Path.GetDirectoryName(ackFile)!);
        File.WriteAllText(ackFile, JsonSerializer.Serialize(new { schemaVersion = 1, datasetId = store.DatasetIdValue, server = store.ServerName, categoryKey = first.CategoryKey, revision = first.Revision, batchId = first.BatchId, bodyHash = first.BodyHash }));
        store.ProcessCategoryAcknowledgements();
        Check(store.PendingCategoryBatches().Count == 2, "Uplink açıkken yerel alıcının ACK dosyası paketi serbest bırakmaz");
        store.RequireUplinkAck = false; File.Delete(ackFile);

        fake.Committed.Add((first.CategoryKey, 1));
        worker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(store.PendingCategoryBatches().Count == 1 && store.Query("SELECT COUNT(*) FROM category_outbox WHERE body IS NULL")[0][0] is 1L, "Sunucu işlediğini bildirince paket kuyruktan çıkar ve gövde boşaltılır");
        fake.Committed.Add((first.CategoryKey, 2));
        worker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(store.PendingCategoryBatches().Count == 0 && worker.Status.State == "idle" && worker.Status.PendingBatches == 0, "Kümülatif onayla kalan paket de serbest kalır; durum 'güncel'");

        producer.Round(Raw(3));
        var restarted = new UplinkWorker(store, Client(fake), delay: NoDelay);
        var sentBefore = fake.Requests.Count(r => r == "POST /ingest/v1/batch");
        restarted.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(fake.Requests.Count(r => r == "POST /ingest/v1/batch") == sentBefore + 1 && fake.Batches.Keys.Any(k => k.Revision == 3), "Yeniden başlatmada sunucunun durumu sorulur; yalnız eksik paket gönderilir");

        var flaky = new FakeServer { Fail = 3 }; flaky.Dataset = null;
        var waits = new List<TimeSpan>(); using var stop = new CancellationTokenSource();
        var flakyWorker = new UplinkWorker(store, Client(flaky), delay: (span, token) => { waits.Add(span); if (waits.Count >= 6) stop.Cancel(); return Task.CompletedTask; });
        flakyWorker.RunAsync(stop.Token).GetAwaiter().GetResult();
        Check(waits.Take(3).SequenceEqual([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)]) && flaky.Batches.Count >= 1, $"Geçici hatalarda 5→10→20 sn bekleyip yeniden dener ve sonra başarır ({string.Join(",", waits.Take(4).Select(w => w.TotalSeconds))})");
        Check(!store.RequireUplinkAck, "Uplink durunca yerel ACK yolu yeniden açılır");
        var denied = new FakeServer { Fail = 100, FailStatus = HttpStatusCode.Unauthorized, FailCode = "auth" };
        var deniedWorker = new UplinkWorker(store, Client(denied), delay: NoDelay);
        try { deniedWorker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Reddedilen anahtar"); }
        catch (UplinkException error) { Check(error.Permanent && error.Status == HttpStatusCode.Unauthorized, "Kimlik reddedilirse kalıcı hata sayılır (sürekli yeniden denenmez)"); }
        var foreign = new FakeServer { Dataset = "baska-veri-kumesi" };
        try { new UplinkWorker(store, Client(foreign), delay: NoDelay).DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult(); Check(false, "Farklı veri kümesi"); }
        catch (UplinkException error) { Check(error.Permanent && foreign.Batches.Count == 0, "Sunucudaki veri kümesi farklıysa hiçbir şey gönderilmez"); }

        var skewed = new FakeServer { ServerClockOffset = 3600 };
        new UplinkWorker(store, Client(skewed), delay: NoDelay).DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(skewed.Batches.Count >= 1 && Math.Abs(skewed.LastTimestamp - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600)) < 30, "Bilgisayar saati kaymışsa sunucu saatine uyum sağlanır ve istek tekrarlanır");

        LoopbackAgainstNode(directory, results);
    }

    private static string? FindMarketDraft()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "market-draft");
            if (File.Exists(Path.Combine(candidate, "ingest-server.cjs"))) return candidate;
        }
        return null;
    }
    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port; }
    private static (int Code, string Output) Node(string workDir, string script, params string[] args)
    {
        var start = new ProcessStartInfo("node") { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(script); foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(60000); return (process.ExitCode, output);
    }
    private static void LoopbackAgainstNode(string directory, List<string> results)
    {
        var market = FindMarketDraft();
        if (market is null || Node(market, "-e", "0").Code != 0) { results.Add("SKIP: Node/market-draft bulunamadı; yerel uçtan uca sınama atlandı"); return; }
        var work = Path.GetFullPath(Path.Combine(directory, "uplink-e2e-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(work);
        var sourceRoot = Path.Combine(work, "source"); var keys = Path.Combine(work, "keys.json"); var webRoot = Path.Combine(work, "web");
        File.WriteAllText(keys, JsonSerializer.Serialize(new { keys = new[] { new { id = "k1", secret = Secret, status = "active" } } }));
        var port = FreePort();
        var start = new ProcessStartInfo("node") { WorkingDirectory = market, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("ingest-server.cjs");
        start.Environment["INGEST_KEYS_FILE"] = keys; start.Environment["SOURCE_ROOT"] = sourceRoot; start.Environment["INGEST_PORT"] = port.ToString(); start.Environment["INGEST_STATE_DIR"] = Path.Combine(work, "state");
        using var server = Process.Start(start)!;
        try
        {
            using var http = new HttpClient();
            var client = new UplinkClient(http, new UplinkConfig(new Uri($"http://127.0.0.1:{port}"), "k1", Secret));
            for (var i = 0; ; i++)
            {
                try { client.Ping(CancellationToken.None).GetAwaiter().GetResult(); break; }
                catch (UplinkException error) when (i < 150 && !server.HasExited) { Thread.Sleep(100); _ = error; }
                catch (UplinkException error) { throw new Exception("Alıcı süreci başlamadı (çıkış: " + (server.HasExited ? server.ExitCode.ToString() : "çalışıyor") + "): " + (server.HasExited ? server.StandardError.ReadToEnd() : error.Message)); }
            }
            using var producer = new Producer(Path.Combine(work, "scanner"));
            producer.Round(Raw()); producer.Round(Raw(2));
            var fixedTime=DateTimeOffset.UtcNow;
            var worker = new UplinkWorker(producer.Db, client, delay: NoDelay,now:()=>fixedTime);
            worker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            void Check(bool ok, string label) { if (!ok) throw new Exception("FAILED: " + label); results.Add("PASS: " + label); }
            Check(producer.Db.PendingCategoryBatches().Count == 2, "Gerçek alıcı: paketler sunucuya gitti ama onay gelmeden kuyrukta duruyor");
            var tick = Node(market, "ingest-importer-tick.cjs", sourceRoot, webRoot);
            Check(tick.Code == 0, "Gerçek alıcı: değiştirilmemiş içe aktarıcı aynalanmış kaynağı hatasız işledi " + tick.Output.Trim());
            worker.DrainOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(producer.Db.PendingCategoryBatches().Count == 0 && producer.Db.PendingCategoryStats().Count == 0, "Gerçek alıcı: içe aktarıcı onayı sunucudan alındı, paketler serbest bırakıldı");
        }
        finally { try { server.Kill(true); } catch (InvalidOperationException) { } }
    }
}
