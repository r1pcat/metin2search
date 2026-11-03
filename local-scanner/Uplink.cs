using System.IO.Compression;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

public sealed record UplinkConfig(Uri BaseUrl, string KeyId, string SecretHex)
{
    public static string DefaultPath(string rootDirectory) => Environment.GetEnvironmentVariable("METIN2_UPLINK_CONFIG")
        ?? Path.GetFullPath(Path.Combine(rootDirectory, "..", "config", "uplink.json"));

    public static UplinkConfig? TryLoad(string rootDirectory, out string? problem)
    {
        problem = null;
        var file = DefaultPath(rootDirectory);
        if (!File.Exists(file)) return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file).TrimStart('﻿'));
            var root = json.RootElement;
            var url = new Uri(root.GetProperty("url").GetString()!);
            var keyId = root.GetProperty("keyId").GetString()!;
            var keyFile = Path.Combine(Path.GetDirectoryName(file)!, root.GetProperty("keyFile").GetString()!);
            var secret = File.ReadAllText(keyFile).Trim();
            return Validate(url, keyId, secret, out problem) ? new UplinkConfig(url, keyId, secret) : null;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or IOException or UriFormatException or InvalidOperationException)
        {
            problem = "Sunucu aktarım ayarı okunamadı: " + error.Message;
            return null;
        }
    }
    public static bool Validate(Uri url, string keyId, string secret, out string? problem)
    {
        problem = null;
        var loopback = url.IsLoopback;
        if (url.Scheme != Uri.UriSchemeHttps && !(loopback && url.Scheme == Uri.UriSchemeHttp)) problem = "Sunucu adresi https olmalı (yalnız yerel test adresi http olabilir).";
        else if (keyId.Length is < 1 or > 32 || !keyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) problem = "Anahtar kimliği geçersiz.";
        else if (secret.Length < 64 || secret.Length % 2 != 0 || !secret.All(char.IsAsciiHexDigit)) problem = "Gizli anahtar en az 32 bayt (64 onaltılık karakter) olmalı.";
        return problem is null;
    }
}

public static class UplinkSigner
{
    public static string Canonical(string method, string target, long timestamp, string nonce, byte[] body) =>
        string.Join('\n', method.ToUpperInvariant(), target, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture), nonce,
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant());
    public static string Sign(string secretHex, string method, string target, long timestamp, string nonce, byte[] body) =>
        Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(secretHex), Encoding.UTF8.GetBytes(Canonical(method, target, timestamp, nonce, body)))).ToLowerInvariant();
}

public sealed class UplinkException(string message, HttpStatusCode? status = null, string? code = null, bool permanent = false) : IOException(message)
{
    public HttpStatusCode? Status { get; } = status;
    public string? Code { get; } = code;
    public bool Permanent { get; } = permanent;
}

public sealed record UplinkServerState(string? DatasetId, Dictionary<string, (long Received, long Acked)> Categories, long PurchaseChecksCursor, (string LastSeen, string Key)? PresenceCursor, string? LatestRun)
{
}

public sealed class UplinkClient(HttpClient http, UplinkConfig config, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private long offsetSeconds;

    private async Task<JsonElement> Send(HttpMethod method, string path, byte[]? body, IDictionary<string, string>? headers, string? contentEncoding, CancellationToken token, bool retried = false)
    {
        body ??= [];
        var target = "/ingest/v1/" + path;
        var timestamp = now().ToUnixTimeSeconds() + offsetSeconds;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        using var request = new HttpRequestMessage(method, new Uri(config.BaseUrl, target));
        request.Headers.TryAddWithoutValidation("X-M2-Key-Id", config.KeyId);
        request.Headers.TryAddWithoutValidation("X-M2-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-M2-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-M2-Signature", UplinkSigner.Sign(config.SecretHex, method.Method, request.RequestUri!.PathAndQuery, timestamp, nonce, body));
        if (headers is not null) foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        if (method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new("application/octet-stream");
            if (contentEncoding is not null) request.Content.Headers.ContentEncoding.Add(contentEncoding);
        }
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, token); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested) { throw new UplinkException("Sunucuya ulaşılamadı: " + error.Message); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(token);
            JsonElement json = default;
            try { json = JsonDocument.Parse(text.Length == 0 ? "{}" : text).RootElement.Clone(); } catch (JsonException) { }
            if (response.IsSuccessStatusCode) return json;
            var code = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var e) ? e.GetString() : response.ReasonPhrase;
            if (response.StatusCode == HttpStatusCode.Unauthorized && code == "clock" && !retried && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("serverTime", out var st) && st.TryGetInt64(out var serverTime))
            {
                var learned = serverTime - now().ToUnixTimeSeconds();
                if (Math.Abs(learned) < 86400) { offsetSeconds = learned; return await Send(method, path, body, headers, contentEncoding, token, true); }
            }
            var permanent = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest;
            throw new UplinkException($"Sunucu {(int)response.StatusCode}: {message}", response.StatusCode, code, permanent && code != "replay");
        }
    }

    public Task<JsonElement> Ping(CancellationToken token) => Send(HttpMethod.Get, "ping", null, null, null, token);
    public async Task<UplinkServerState> State(string server, CancellationToken token)
    {
        var json = await Send(HttpMethod.Get, "state?server=" + Uri.EscapeDataString(server), null, null, null, token);
        var categories = new Dictionary<string, (long, long)>();
        foreach (var entry in json.GetProperty("categories").EnumerateObject()) categories[entry.Name] = (entry.Value.GetProperty("received").GetInt64(), entry.Value.GetProperty("acked").GetInt64());
        (string, string)? presence = json.TryGetProperty("presenceCursor", out var p) && p.ValueKind == JsonValueKind.Object ? (p.GetProperty("lastSeen").GetString()!, p.GetProperty("key").GetString()!) : null;
        return new(json.GetProperty("datasetId").ValueKind == JsonValueKind.String ? json.GetProperty("datasetId").GetString() : null, categories,
            json.GetProperty("purchaseChecksCursor").GetInt64(), presence, json.TryGetProperty("latestRun", out var run) && run.ValueKind == JsonValueKind.String ? run.GetString() : null);
    }
    public async Task<string> PostBatch(string server, string datasetId, CategoryBatch batch, CancellationToken token)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-M2-Server"] = server, ["X-M2-Dataset"] = datasetId, ["X-M2-Category"] = batch.CategoryKey, ["X-M2-Revision"] = batch.Revision.ToString(),
            ["X-M2-Batch"] = batch.BatchId, ["X-M2-Scan"] = batch.ScanId, ["X-M2-Completed"] = batch.CompletedAt, ["X-M2-Body-Hash"] = batch.BodyHash,
        };
        var json = await Send(HttpMethod.Post, "batch", batch.Body, headers, null, token);
        return json.GetProperty("status").GetString() ?? "";
    }
    public Task<JsonElement> PostRows(string server, string table, object rows, CancellationToken token)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(new { server, table, rows });
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(raw);
        return Send(HttpMethod.Post, "rows", output.ToArray(), null, "gzip", token);
    }
    public async Task<List<CategoryAck>> Acks(string server, CancellationToken token)
    {
        var json = await Send(HttpMethod.Get, "acks?server=" + Uri.EscapeDataString(server), null, null, null, token);
        var result = new List<CategoryAck>();
        foreach (var a in json.GetProperty("acks").EnumerateArray())
            result.Add(new CategoryAck(a.GetProperty("schemaVersion").GetInt32(), a.GetProperty("datasetId").GetString()!, a.GetProperty("server").GetString()!, a.GetProperty("categoryKey").GetString()!,
                a.GetProperty("revision").GetInt64(), a.GetProperty("batchId").GetString()!, a.GetProperty("bodyHash").GetString()!));
        return result;
    }
}

public sealed record UplinkStatus(string State, string Message, int PendingBatches, long PendingBytes, DateTimeOffset? LastSuccess, int Failures)
{
    public string Line => State switch
    {
        "idle" => PendingBatches == 0 ? "Sunucuya aktarım: güncel" : $"Sunucuya aktarım: {PendingBatches} paket onay bekliyor ({PendingBytes / 1048576d:F1} MB)",
        "sending" => $"Sunucuya aktarım: gönderiliyor ({PendingBatches} paket)",
        "waiting" => $"Sunucuya aktarım: bekleniyor — {Message} (bekleyen {PendingBatches} paket, kayıp yok)",
        "blocked" => $"Sunucuya aktarım: DURDU — {Message}",
        _ => "Sunucuya aktarım: kapalı",
    };
}

public sealed class UplinkWorker
{
    private readonly LocalStore store;
    private readonly UplinkClient client;
    private readonly Action<UplinkStatus> report;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<DateTimeOffset> now;
    private int failures;
    private DateTimeOffset? lastSuccess;
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);
    public int PageRows { get; init; } = 1000;
    public UplinkStatus Status { get; private set; } = new("off", "", 0, 0, null, 0);

    public UplinkWorker(LocalStore store, UplinkClient client, Action<UplinkStatus>? report = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? now = null)
    {
        this.store = store; this.client = client; this.report = report ?? (_ => { }); this.delay = delay ?? Task.Delay; this.now = now ?? (() => DateTimeOffset.UtcNow);
    }
    private void Publish(string state, string message = "")
    {
        var (count, bytes) = store.PendingCategoryStats();
        Status = new(state, message, count, bytes, lastSuccess, failures);
        report(Status);
    }

    public async Task RunAsync(CancellationToken token)
    {
        store.RequireUplinkAck = true;
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    await DrainOnceAsync(token);
                    failures = 0; wait = Interval;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (UplinkException error)
                {
                    failures++;
                    wait = error.Permanent ? MaxBackoff : TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 5 * Math.Pow(2, Math.Min(failures - 1, 6))));
                    Publish(error.Permanent ? "blocked" : "waiting", error.Message);
                }
                try { await delay(wait, token); } catch (OperationCanceledException) { break; }
            }
        }
        finally { store.RequireUplinkAck = false; }
    }

    public async Task DrainOnceAsync(CancellationToken token)
    {
        var server = store.ServerName ?? throw new UplinkException("Sunucu adı henüz belli değil.", permanent: true);
        var dataset = store.DatasetIdValue;
        if (dataset.Length == 0) throw new UplinkException("Kaynak veri kümesi kimliği yok.", permanent: true);
        Publish("sending");
        var state = await client.State(server, token);
        if (state.DatasetId is not null && state.DatasetId != dataset)
            throw new UplinkException("Sunucudaki veri kümesi bu tarayıcınınkinden farklı; kontrollü yeniden eşitleme gerekli.", HttpStatusCode.Conflict, "dataset", true);

        if (store.LatestTradeRun() is { } run && run.Started != state.LatestRun)
            await client.PostRows(server, "trade_runs", new[] { new { started = run.Started, planJson = run.PlanJson } }, token);

        foreach (var batch in store.PendingCategoryBatches(100))
        {
            token.ThrowIfCancellationRequested();
            if (state.Categories.TryGetValue(batch.CategoryKey, out var known) && known.Received >= batch.Revision) continue;
            await client.PostBatch(server, dataset, batch, token);
        }

        var checksCursor = state.PurchaseChecksCursor;
        for (;;)
        {
            var rows = store.PurchaseChecksSince(checksCursor, PageRows);
            if (rows.Count == 0) break;
            await client.PostRows(server, "purchase_checks", rows.Select(r => new { id = r.Id, payload = r.Payload }), token);
            checksCursor = rows[^1].Id;
            if (rows.Count < PageRows) break;
        }

        var presence = state.PresenceCursor ?? ("", "");
        for (;;)
        {
            var rows = store.PresenceSince(presence.Item1, presence.Item2, PageRows * 5);
            if (rows.Count == 0) break;
            await client.PostRows(server, "compact_presence", rows.Select(r => new { key = r.Key, session = r.Session, vid = r.Vid, lastSeen = r.LastSeen }), token);
            presence = (rows[^1].LastSeen, rows[^1].Key);
            if (rows.Count < PageRows * 5) break;
        }

        foreach (var ack in await client.Acks(server, token))
        {
            if (ack.DatasetId != dataset) continue;
            try { store.AcknowledgeCategoryRevision(ack); }
            catch (InvalidDataException) { }
        }
        lastSuccess = now();
        Publish("idle");
    }
}

public sealed record PurchaseCheckRow(long Id, string Payload);
public sealed record PresenceRow(string Key, string Session, long Vid, string LastSeen);
public sealed record TradeRunRow(string Started, string PlanJson);

public sealed partial class LocalStore
{
    public bool RequireUplinkAck { get; set; }
    public string DatasetIdValue => DatasetId;
    public (int Count, long Bytes) PendingCategoryStats()
    {
        lock (this)
        {
            var row = Query("SELECT COUNT(*),COALESCE(SUM(LENGTH(body)),0) FROM category_outbox WHERE acked_at IS NULL AND body IS NOT NULL").First();
            return ((int)(long)row[0]!, (long)row[1]!);
        }
    }
    public List<PurchaseCheckRow> PurchaseChecksSince(long afterId, int limit)
    {
        lock (this) return Query("SELECT id,payload_json FROM purchase_checks WHERE id>? ORDER BY id LIMIT ?", afterId, (long)limit).Select(r => new PurchaseCheckRow((long)r[0]!, (string)r[1]!)).ToList();
    }
    public List<PresenceRow> PresenceSince(string lastSeen, string key, int limit)
    {
        lock (this)
        {
            Exec("CREATE INDEX IF NOT EXISTS ix_presence_seen ON compact_presence(last_seen,key)");
            return Query("SELECT key,session,vid,last_seen FROM compact_presence WHERE last_seen IS NOT NULL AND (last_seen>? OR (last_seen=? AND key>?)) ORDER BY last_seen,key LIMIT ?", lastSeen, lastSeen, key, (long)limit)
                .Select(r => new PresenceRow((string)r[0]!, (string)r[1]!, (long)r[2]!, (string)r[3]!)).ToList();
        }
    }
    public TradeRunRow? LatestTradeRun()
    {
        lock (this)
        {
            var row = Query("SELECT started,plan_json FROM trade_runs ORDER BY started DESC LIMIT 1").FirstOrDefault();
            return row is null ? null : new TradeRunRow((string)row[0]!, (string)row[1]!);
        }
    }
}
