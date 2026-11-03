using System.Runtime.InteropServices;

namespace Metin2Search.Local;

public sealed partial class LocalStore : IDisposable
{
    private IntPtr db;
    public ScanMetrics Metrics {get;}=new();
    public string FilePath { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string,IntPtr> statements=[];
    public static LocalStore OpenReadOnly(string file)=>new(file,true,0);
    private LocalStore(string file,bool readOnly,int _)
    {
        FilePath=Path.GetFullPath(file);
        if(Native.sqlite3_open_v2(FilePath,out db,1|0x10000,IntPtr.Zero)!=0){var error=Error();Dispose();throw new IOException(error);}
        Native.sqlite3_busy_timeout(db,5000);Exec("PRAGMA query_only=ON;");
        ServerName=Query("SELECT value FROM trade_metadata WHERE key='server'").FirstOrDefault()?[0] as string;
        DatasetId=(string?)Query("SELECT value FROM trade_metadata WHERE key='dataset_id'").FirstOrDefault()?[0]??"";
    }
    public LocalStore(string directory,bool compactLegacy=true,bool dailyMaintenance=true,Func<DateTimeOffset>? archiveClock=null)
    {
        DailyBackgroundEnabled=dailyMaintenance;if(archiveClock is not null)ArchiveClock=archiveClock;
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "market.sqlite");
        var rc = Native.sqlite3_open_v2(FilePath, out db, 2 | 4 | 0x10000, IntPtr.Zero);
        if (rc != 0) { var message = Error(); Dispose(); throw new IOException(message); }
        try
        {
            Native.sqlite3_busy_timeout(db, 5000);
            Exec("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
            Exec("""
                CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY, started TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS captures(
                    id TEXT PRIMARY KEY, session_id TEXT NOT NULL REFERENCES sessions(id), observed TEXT NOT NULL,
                    source TEXT NOT NULL, shop TEXT NOT NULL, item_count INTEGER NOT NULL, slot_count INTEGER NOT NULL,
                    complete INTEGER NOT NULL, payload_hash TEXT NOT NULL, metadata_json TEXT NOT NULL, manager_hash TEXT);
                CREATE TABLE IF NOT EXISTS raw_blobs(hash TEXT PRIMARY KEY, bytes BLOB NOT NULL);
                CREATE TABLE IF NOT EXISTS observations(
                    capture_id TEXT NOT NULL REFERENCES captures(id), tab INTEGER NOT NULL, slot INTEGER NOT NULL,
                    vnum INTEGER NOT NULL, count INTEGER NOT NULL, raw_hash TEXT NOT NULL REFERENCES raw_blobs(hash),
                    decoded_json TEXT NOT NULL, PRIMARY KEY(capture_id,tab,slot));
                CREATE TABLE IF NOT EXISTS events(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL REFERENCES sessions(id),
                    occurred TEXT NOT NULL, kind TEXT NOT NULL, message TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS diagnostics(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL REFERENCES sessions(id),
                    occurred TEXT NOT NULL, address TEXT NOT NULL, requested_bytes INTEGER NOT NULL,
                    actual_bytes INTEGER NOT NULL, stage TEXT NOT NULL, raw_hash TEXT REFERENCES raw_blobs(hash), message TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS decoding_history(
                    capture_id TEXT NOT NULL, tab INTEGER NOT NULL, slot INTEGER NOT NULL, previous_count INTEGER NOT NULL,
                    previous_json TEXT NOT NULL, decoder_revision TEXT NOT NULL, PRIMARY KEY(capture_id,tab,slot,decoder_revision));
                CREATE INDEX IF NOT EXISTS ix_captures_observed ON captures(observed);
                CREATE INDEX IF NOT EXISTS ix_observations_vnum ON observations(vnum);
                PRAGMA user_version=1;
                """);
            Execute("INSERT INTO sessions VALUES(?,?)", SessionId, DateTimeOffset.UtcNow.ToString("O"));
            Exec("""
                CREATE TABLE IF NOT EXISTS trade_pages(id TEXT PRIMARY KEY,session_id TEXT NOT NULL REFERENCES sessions(id),observed TEXT NOT NULL,
                    category_json TEXT NOT NULL,generation INTEGER NOT NULL,page INTEGER NOT NULL,total INTEGER NOT NULL,raw_hash TEXT NOT NULL REFERENCES raw_blobs(hash));
                CREATE TABLE IF NOT EXISTS trade_items(page_id TEXT NOT NULL REFERENCES trade_pages(id),slot INTEGER NOT NULL,seller TEXT NOT NULL,
                    name TEXT NOT NULL,vnum INTEGER NOT NULL,won INTEGER NOT NULL,yang INTEGER NOT NULL,decoded_json TEXT NOT NULL,PRIMARY KEY(page_id,slot));
                CREATE INDEX IF NOT EXISTS ix_trade_items_seller ON trade_items(seller);
                CREATE INDEX IF NOT EXISTS ix_trade_items_vnum ON trade_items(vnum);
                CREATE TABLE IF NOT EXISTS trade_runs(id TEXT PRIMARY KEY,started TEXT NOT NULL,finished TEXT,plan_json TEXT NOT NULL,summary TEXT);
                CREATE TABLE IF NOT EXISTS trade_queries(id TEXT PRIMARY KEY,run_id TEXT NOT NULL REFERENCES trade_runs(id),label TEXT NOT NULL,
                    status TEXT NOT NULL,pages INTEGER NOT NULL,expected INTEGER NOT NULL,message TEXT NOT NULL,updated TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS trade_page_details(page_id TEXT PRIMARY KEY REFERENCES trade_pages(id),run_id TEXT,query_id TEXT,details_json TEXT,error TEXT);
                CREATE TABLE IF NOT EXISTS trade_item_features(page_id TEXT NOT NULL,slot INTEGER NOT NULL,search_text TEXT NOT NULL,PRIMARY KEY(page_id,slot));
                """);
            ReindexLegacyLiveRecords();
            InitializeCompactTrade();
            InitializeDailyArchive();
            if(compactLegacy)CompactLegacyTrade();
            QueueDailyMaintenance();
        }
        catch { Dispose(); throw; }
    }

    public bool Save(MarketCapture capture)
    {
        capture.Validate();
        var payload = JsonData.Encode(capture);
        var payloadHash = ItemDecoder.Hash(System.Text.Encoding.UTF8.GetBytes(payload));
        var existing = Query("SELECT payload_hash FROM captures WHERE id=?", capture.Id);
        if (existing.Count > 0)
        {
            if ((string)existing[0][0]! != payloadHash) throw new InvalidDataException("Aynı kimlik farklı veri içeriyor; kayıt reddedildi.");
            return false;
        }
        var rows = capture.Slots.Select(s => ItemDecoder.Decode(capture, s)).ToArray();
        Exec("BEGIN IMMEDIATE");
        try
        {
            string? managerHash = null;
            if (capture.RawManager is { } rawManager)
            {
                managerHash = ItemDecoder.Hash(rawManager);
                Execute("INSERT OR IGNORE INTO raw_blobs VALUES(?,?)", managerHash, rawManager);
            }
            var metadata = JsonData.Encode(capture with { Slots = [], RawManager = null });
            Execute("INSERT INTO captures VALUES(?,?,?,?,?,?,?,?,?,?,?)", capture.Id, SessionId, capture.ObservedAt.ToString("O"),
                capture.Source, capture.ShopLabel, rows.Count(r => r.Vnum != 0), rows.Length, capture.IsComplete ? 1 : 0, payloadHash, metadata, managerHash);
            foreach (var row in rows)
            {
                var hash = ItemDecoder.Hash(row.Raw);
                Execute("INSERT OR IGNORE INTO raw_blobs VALUES(?,?)", hash, row.Raw);
                Execute("INSERT INTO observations VALUES(?,?,?,?,?,?,?)", capture.Id, row.Tab, row.Slot, row.Vnum, row.Count, hash,
                    JsonData.Encode(row with { Raw = [] }));
            }
            Event("Saved", $"{capture.Source} | {capture.ShopLabel} | {rows.Length} yuva; {rows.Count(r => r.Vnum != 0)} ürün | {capture.Id}");
            Exec("COMMIT");
            return true;
        }
        catch { try { Exec("ROLLBACK"); } catch { } throw; }
    }

    public void SaveTrade(TradePage page)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        lock(this){SaveCompactTrade(page);Metrics.Page(page.Raw.Length/187,LastTradeChanged,LastTradeUnchanged);}
        Metrics.Record("store.page",timer.Elapsed);
        PublishScannerStatus();
        QueueDailyMaintenance();
    }
    public List<TradeItem> TradeItems(string search="",int offset=0,bool activeOnly=false,int limit=2000) => Query("""
        SELECT p.bytes,pr.last_seen,ms.status,ms.is_fake,p.hash,i.id,i.key,l.is_active,l.last_seen,l.removed_at,l.reason,l.version_id FROM compact_versions i JOIN compact_payloads p ON p.hash=i.payload_hash
        LEFT JOIN compact_presence pr ON pr.key=i.key
        LEFT JOIN listing_lifecycle l ON l.key=i.key
        LEFT JOIN market_states ms ON ms.session=pr.session AND ms.vid=pr.vid AND ms.seller=i.seller
        WHERE (?=0 OR EXISTS(SELECT 1 FROM listing_lifecycle l WHERE l.key=i.key AND l.is_active=1 AND l.version_id=i.id)) AND (?='' OR i.name LIKE ? ESCAPE '\' OR i.seller LIKE ? ESCAPE '\' OR CAST(i.vnum AS TEXT) LIKE ? ESCAPE '\' OR i.search_text LIKE ? ESCAPE '\')
        ORDER BY i.id DESC LIMIT ? OFFSET ?
        """,activeOnly?1:0,search,Like(search),Like(search),Like(search),Like(search),Math.Clamp(limit,1,2000),offset)
        .Select(r => DecodeTradeRow(r,0)).ToList();
    private TradeItem DecodeTradeRow(object?[] row,int offset)=>WithIdentity(DecodeCompact((byte[])row[offset]!,(string)row[offset+4]!) with{Server=ServerName,LastSeen=(string?)row[offset+1],MarketStatus=(long?)row[offset+3]==1?"FAKE":(string?)row[offset+2]=="Open"?"Açıldı":"Doğrulanmadı",IsFake=(long?)row[offset+3]==1},(long)row[offset+5]!, (string)row[offset+6]!,row.Skip(offset+7).Take(5).ToArray());
    private List<object?[]> ExportTradeRows(long cursor,bool activeOnly=false)=>Query("""
        SELECT i.id,p.bytes,pr.last_seen,ms.status,ms.is_fake,p.hash,i.id,i.key,l.is_active,l.last_seen,l.removed_at,l.reason,l.version_id FROM compact_versions i JOIN compact_payloads p ON p.hash=i.payload_hash
        LEFT JOIN compact_presence pr ON pr.key=i.key
        LEFT JOIN listing_lifecycle l ON l.key=i.key
        LEFT JOIN market_states ms ON ms.session=pr.session AND ms.vid=pr.vid AND ms.seller=i.seller
        WHERE i.id>? AND (?=0 OR EXISTS(SELECT 1 FROM listing_lifecycle l WHERE l.key=i.key AND l.is_active=1 AND l.version_id=i.id)) ORDER BY i.id LIMIT 1000
        """,cursor,activeOnly?1:0);
    private static string Like(string value)=>"%"+value.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
    public long TradePageCount => (long)Query("SELECT (SELECT COUNT(*) FROM trade_pages)+(SELECT COUNT(*) FROM compact_pages)")[0][0]!;
    public bool SingleSweepRemoval {get;set;}=true;
    private string RunKind(string run)=>(string?)Query("SELECT kind FROM scan_run_kinds WHERE run_id=?",run).FirstOrDefault()?[0]??"Full";
    private void PruneHotRuns(string current)
    {
        foreach(var row in Query("SELECT r.run_id FROM lifecycle_runs r JOIN scan_run_kinds k ON k.run_id=r.run_id WHERE k.kind='Hot' AND r.run_id<>? ORDER BY r.started DESC LIMIT -1 OFFSET 60",current)){
            var id=(string)row[0]!;
            foreach(var sql in new[]{"DELETE FROM scope_snapshots WHERE run_id=?","DELETE FROM scan_query_progress WHERE run_id=?","DELETE FROM trade_queries WHERE run_id=?","DELETE FROM scan_query_counts WHERE run_id=?","DELETE FROM trade_runs WHERE id=?","DELETE FROM category_delivery_runs WHERE run_id=?","DELETE FROM scan_run_kinds WHERE run_id=?","DELETE FROM lifecycle_runs WHERE run_id=?"})Execute(sql,id);
        }
    }
    private readonly Dictionary<string,long> runningAuditAt=[];
    private readonly System.Diagnostics.Stopwatch auditClock=System.Diagnostics.Stopwatch.StartNew();
    private bool ThrottleRunningAudit(TradeAudit data)
    {
        if(data.Kind=="RunStart"){lock(runningAuditAt)runningAuditAt.Clear();return false;}
        if(data.Kind!="Running"||data.Pages<=0)return false;
        var now=auditClock.ElapsedMilliseconds;
        lock(runningAuditAt){
            if(runningAuditAt.TryGetValue(data.QueryId,out var last)&&now-last<1500)return true;
            runningAuditAt[data.QueryId]=now;return false;
        }
    }
    public void TradeAudit(TradeAudit data)
    {
        if(ThrottleRunningAudit(data))return;
        var auditTimer=System.Diagnostics.Stopwatch.StartNew();
        try{TradeAuditWrite(data);}finally{Metrics.Record("audit."+data.Kind.ToLowerInvariant(),auditTimer.Elapsed);}
    }
    public void TradeAuditBatch(IReadOnlyList<TradeAudit> entries)
    {
        var batch=entries.Where(e=>!ThrottleRunningAudit(e)).ToArray();if(batch.Length==0)return;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        try{
            if(batch.Any(e=>e.Kind is "RunStart" or "Complete"))ProcessCategoryAcknowledgements();
            lock(this){BeginDailyTransaction();Exec("BEGIN IMMEDIATE");try{
                foreach(var entry in batch)TradeAuditUnlocked(entry);
                CaptureDailyLifecycle();Exec("COMMIT");
            }catch{try{Exec("ROLLBACK");}catch{}throw;}}
            foreach(var plan in batch.Where(e=>e.Kind is "Complete" or "HotDone"&&e.PlanQueryId is not null).Select(e=>e.PlanQueryId!).Distinct())PublishCategoryReady(plan);
            QueueDailyMaintenance();
        }finally{Metrics.Record("audit.batch."+batch[0].Kind.ToLowerInvariant(),timer.Elapsed);}
    }
    private void TradeAuditWrite(TradeAudit data)
    {
        if(data.Kind is "RunStart" or "Complete")ProcessCategoryAcknowledgements();
        lock(this){BeginDailyTransaction();Exec("BEGIN IMMEDIATE");try{TradeAuditUnlocked(data);CaptureDailyLifecycle();Exec("COMMIT");}catch{try{Exec("ROLLBACK");}catch{}throw;}}
        if(data.Kind is "Complete" or "HotDone"&&data.PlanQueryId is not null)PublishCategoryReady(data.PlanQueryId);
        QueueDailyMaintenance();
    }
    private void TradeAuditUnlocked(TradeAudit data)
    {
        var time=DateTimeOffset.UtcNow.ToString("O");
        if(data.Kind=="RunStart"&&data.PlannedQueries is {} queries)
            Execute("INSERT OR REPLACE INTO scan_query_counts VALUES(?,?)",data.RunId,queries);
        if(data.PlanQueryId is {} planId)
            Execute("INSERT INTO scan_query_progress VALUES(?,?,?,?,?,?,?) ON CONFLICT(query_id) DO UPDATE SET status=excluded.status,pages=excluded.pages,expected=excluded.expected,message=excluded.message",planId,data.RunId,data.PlanLabel??data.Category!.Label,data.Kind,data.Pages,data.Expected,data.Message);
        if(data.Kind=="RunStart") Execute("INSERT INTO trade_runs VALUES(?,?,NULL,?,NULL)",data.RunId,time,data.Message);
        else if(data.Kind=="RunEnd") Execute("UPDATE trade_runs SET finished=?,summary=? WHERE id=?",time,data.Message,data.RunId);
        else Execute("INSERT INTO trade_queries VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET status=excluded.status,pages=excluded.pages,expected=excluded.expected,message=excluded.message,updated=excluded.updated",
            data.QueryId,data.RunId,data.Category!.Label,data.Kind,data.Pages,data.Expected,data.Message,time);
        LifecycleAudit(data,time);
        CategoryDeliveryAudit(data,time);
    }
    public List<TradeCoverageRow> TradeCoverage() => Query("SELECT label,status,pages,expected,message FROM trade_queries WHERE run_id=(SELECT id FROM trade_runs ORDER BY started DESC LIMIT 1) ORDER BY rowid")
        .Select(r=>new TradeCoverageRow((string)r[0]!, (string)r[1]!, (long)r[2]!, (long)r[3]!, (string)r[4]!)).ToList();
    public long TradeObservationCount => (long)Query("SELECT COUNT(*) FROM compact_versions")[0][0]!;
    public void ExportTrade(string destination)
    {
        lock(this)ExportTradeUnlocked(destination);
    }
    private void ExportTradeUnlocked(string destination)
    {
        using var writer=new StreamWriter(destination,false,new System.Text.UTF8Encoding(false));
        writer.WriteLine("[");long cursor=0;bool comma=false;
        while(true){var rows=ExportTradeRows(cursor);if(rows.Count==0)break;
            foreach(var row in rows){if(comma)writer.WriteLine(",");var item=DecodeTradeRow(row,1);var json=System.Text.Json.Nodes.JsonNode.Parse(Unzip((byte[])row[1]!))!.AsObject();json["Server"]=item.Server;json["LastSeen"]=item.LastSeen;json["MarketStatus"]=item.MarketStatus;json["IsFake"]=item.IsFake;AddIdentityJson(json,item);writer.Write(json.ToJsonString(JsonData.Options));comma=true;cursor=(long)row[0]!;}}
        writer.WriteLine("\n]");
    }
    public void ExportTradeCsv(string destination)
    {
        lock(this)ExportTradeCsvUnlocked(destination);
    }
    private void ExportTradeCsvUnlocked(string destination)
    {
        using var writer=new StreamWriter(destination,false,new System.Text.UTF8Encoding(true));
        writer.WriteLine("Sunucu,Ürün,Satıcı,Vnum,Adet,Won,Yang,Taşlar,Element,Emiş,Açıklama,HamBase64,PazarDurumu,FAKE,SonGörülme,ObservationId,ListingId,PhysicalItemId,IsActive,RemovedAt,LifecycleReason");
        long cursor=0;while(true){var rows=ExportTradeRows(cursor);if(rows.Count==0)break;
            foreach(var row in rows){var item=DecodeTradeRow(row,1);writer.WriteLine(string.Join(",",new object?[]{item.Server,item.Name,item.Seller,item.Vnum,item.Count,item.Won,item.Yang,item.StonesSummary,item.ElementSummary,item.AbsorptionSummary,string.Join("\n",item.TooltipLines),Convert.ToBase64String(item.Raw),item.MarketStatus,item.IsFake,item.LastSeen,item.ObservationId,item.ListingId,item.PhysicalItemId,item.IsActive,item.RemovedAt,item.LifecycleReason}.Select(v=>Csv(v?.ToString()??""))));cursor=(long)row[0]!;}}
    }
    private static string Csv(string value){if(value.TrimStart().StartsWith('=')||value.TrimStart().StartsWith('+')||value.TrimStart().StartsWith('-')||value.TrimStart().StartsWith('@'))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
    public void StopTradeRun(string runId,string reason)
    {
        lock(this){
        if(Query("SELECT 1 FROM lifecycle_runs WHERE run_id=? AND status='Running'",runId).Count>0){
            Execute("UPDATE lifecycle_runs SET status='Interrupted',completed_at=? WHERE run_id=?",DateTimeOffset.UtcNow.ToString("O"),runId);
            if(!UsesCategoryDelivery(runId))Execute("UPDATE scope_memberships SET misses=0");
            Execute("UPDATE scope_snapshots SET status='Interrupted' WHERE run_id=? AND status IN ('Pending','Running')",runId);
            ClearLifecycleStaging(runId);BumpLifecycleRevision();
        }
        Execute("UPDATE trade_runs SET finished=COALESCE(finished,?),summary=COALESCE(summary,?) WHERE id=?",DateTimeOffset.UtcNow.ToString("O"),reason,runId);
        Execute("UPDATE trade_queries SET status='Interrupted',message=? WHERE run_id=? AND status='Running'",reason,runId);
        Execute("UPDATE scan_query_progress SET status='Interrupted',message=? WHERE run_id=? AND status IN ('Pending','Running')",reason,runId);
        }
    }

    public MarketCapture Load(string id)
    {
        var data = Query("SELECT metadata_json,manager_hash FROM captures WHERE id=?", id);
        if (data.Count != 1) throw new InvalidDataException("Kayıt bulunamadı.");
        var capture = JsonData.Decode<MarketCapture>((string)data[0][0]!);
        var slots = Query("SELECT o.tab,o.slot,b.bytes FROM observations o JOIN raw_blobs b ON b.hash=o.raw_hash WHERE capture_id=? ORDER BY o.tab,o.slot", id)
            .Select(r => new SlotData((int)(long)r[0]!, (int)(long)r[1]!, (byte[])r[2]!)).ToList();
        var manager = data[0][1] is string hash ? (byte[])Query("SELECT bytes FROM raw_blobs WHERE hash=?", hash)[0][0]! : null;
        return capture with { Slots = slots, RawManager = manager };
    }

    public List<CaptureRow> Captures() => Query("SELECT id,observed,source,shop,item_count,slot_count,complete,metadata_json FROM captures ORDER BY observed DESC LIMIT 1000")
        .Select(r => new CaptureRow((string)r[0]!, DateTimeOffset.Parse((string)r[1]!).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            (string)r[2]!, (string)r[3]!, JsonData.Decode<MarketCapture>((string)r[7]!).OwnerName ?? "Bilinmiyor", (int)(long)r[4]!, (int)(long)r[5]!,
            (long)r[6]! == 1 ? "Tam okuma" : "Bağlam doğrulanmamış")).ToList();

    private void ReindexLegacyLiveRecords()
    {
        var candidates = Query("SELECT id,metadata_json FROM captures WHERE source<>'Demo' AND NOT EXISTS(SELECT 1 FROM decoding_history h WHERE h.capture_id=captures.id AND decoder_revision='shop-v2')");
        foreach (var candidate in candidates)
        {
            var metadata = JsonData.Decode<MarketCapture>((string)candidate[1]!);
            if (metadata.ItemLayout != "legacy-v1" || metadata.ProfileVersion != "ghidra-2026-10-14-unverified" || ItemDecoder.IsLegacyDemo(metadata)) continue;
            var capture = Load((string)candidate[0]!);
            Exec("BEGIN IMMEDIATE");
            try
            {
                foreach (var slot in capture.Slots)
                {
                    var previous = Query("SELECT count,decoded_json FROM observations WHERE capture_id=? AND tab=? AND slot=?", capture.Id, slot.Tab, slot.Slot)[0];
                    Execute("INSERT INTO decoding_history VALUES(?,?,?,?,?,?)", capture.Id, slot.Tab, slot.Slot, previous[0], previous[1], "shop-v2");
                    var decoded = ItemDecoder.Decode(capture, slot);
                    Execute("UPDATE observations SET count=?,decoded_json=? WHERE capture_id=? AND tab=? AND slot=?",
                        decoded.Count, JsonData.Encode(decoded with { Raw = [] }), capture.Id, slot.Tab, slot.Slot);
                }
                Event("DecoderMigration", $"{capture.Id}: adet=byte+0x08, yang=DWORD+0x04, won=DWORD+0x0A. Önceki çözümleme ve tüm ham baytlar korundu.");
                Exec("COMMIT");
            }
            catch { try { Exec("ROLLBACK"); } catch { } throw; }
        }
    }
    public List<EventRow> Events() => Query("SELECT occurred,kind,message FROM events ORDER BY id DESC LIMIT 500")
        .Select(r => new EventRow(DateTimeOffset.Parse((string)r[0]!).ToLocalTime().ToString("dd.MM HH:mm:ss"), (string)r[1]!, (string)r[2]!)).ToList();
    public long CaptureCount => (long)Query("SELECT COUNT(*) FROM captures")[0][0]!;
    public long BlobCount => (long)Query("SELECT COUNT(*) FROM raw_blobs")[0][0]!;
    public long ObservationCount => (long)Query("SELECT COUNT(*) FROM observations")[0][0]!;
    public string Integrity => (string)Query("PRAGMA integrity_check")[0][0]!;
    public void Event(string kind, string message) => Execute("INSERT INTO events(session_id,occurred,kind,message) VALUES(?,?,?,?)",
        SessionId, DateTimeOffset.UtcNow.ToString("O"), kind, message);

    public void Diagnostic(MarketReadException error)
    {
        Exec("BEGIN IMMEDIATE");
        try
        {
            foreach (var part in error.Diagnostics)
            {
                string? hash = null;
                if (part.Bytes.Length > 0)
                {
                    hash = ItemDecoder.Hash(part.Bytes);
                    Execute("INSERT OR IGNORE INTO raw_blobs VALUES(?,?)", hash, part.Bytes);
                }
                Execute("INSERT INTO diagnostics(session_id,occurred,address,requested_bytes,actual_bytes,stage,raw_hash,message) VALUES(?,?,?,?,?,?,?,?)",
                    SessionId, DateTimeOffset.UtcNow.ToString("O"), $"0x{part.Address:X}", part.RequestedBytes, part.Bytes.Length, part.Stage, hash, error.Message);
            }
            Exec("COMMIT");
        }
        catch { try { Exec("ROLLBACK"); } catch { } throw; }
    }

    public void Backup(string destination)
    {
        if (File.Exists(destination)) throw new IOException("Yedek hedefi mevcut; yeni bir dosya adı seçin.");
        Execute("VACUUM INTO ?", destination);
    }

    internal void Execute(string sql, params object?[] args)
    {
        lock(this) ExecuteUnlocked(sql,args);
    }
    private void ExecuteUnlocked(string sql,object?[] args)
    {
        using var stmt = Prepare(sql, args);
        var rc = Native.sqlite3_step(stmt.Pointer);
        if (rc != 101) throw new IOException(Error());
    }
    internal List<object?[]> Query(string sql, params object?[] args)
    {
        lock(this)return QueryUnlocked(sql,args);
    }
    private List<object?[]> QueryUnlocked(string sql,object?[] args)
    {
        using var stmt = Prepare(sql, args);
        var output = new List<object?[]>();
        int rc;
        while ((rc = Native.sqlite3_step(stmt.Pointer)) == 100)
        {
            output.Add(ReadRow(stmt.Pointer));
        }
        if (rc != 101) throw new IOException(Error());
        return output;
    }
    private static object?[] ReadRow(IntPtr pointer)
    {
        var row=new object?[Native.sqlite3_column_count(pointer)];
        for(int i=0;i<row.Length;i++)row[i]=Native.sqlite3_column_type(pointer,i) switch{
            1=>Native.sqlite3_column_int64(pointer,i),3=>Marshal.PtrToStringUTF8(Native.sqlite3_column_text(pointer,i)),4=>ReadBlob(pointer,i),_=>null};
        return row;
    }
    private static byte[] ReadBlob(IntPtr stmt, int i)
    {
        var bytes = new byte[Native.sqlite3_column_bytes(stmt, i)];
        if (bytes.Length > 0) Marshal.Copy(Native.sqlite3_column_blob(stmt, i), bytes, 0, bytes.Length);
        return bytes;
    }
    private Statement Prepare(string sql, object?[] args)
    {
        if(db==IntPtr.Zero)throw new IOException("SQLite deposu kapalı.");
        bool cached=statements.TryGetValue(sql,out var ptr);
        if(!cached){
            if(Native.sqlite3_prepare_v2(db,sql,-1,out ptr,IntPtr.Zero)!=0)throw new IOException(Error());
            cached=statements.Count<128;if(cached)statements.Add(sql,ptr);
        }
        var stmt = new Statement(ptr,cached);
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                var rc = args[i] switch
                {
                    null => Native.sqlite3_bind_null(ptr, i + 1),
                    byte[] b => Native.sqlite3_bind_blob(ptr, i + 1, b, b.Length, new IntPtr(-1)),
                    string s => Native.sqlite3_bind_text(ptr, i + 1, s, -1, new IntPtr(-1)),
                    _ => Native.sqlite3_bind_int64(ptr, i + 1, Convert.ToInt64(args[i]))
                };
                if (rc != 0) throw new IOException(Error());
            }
            return stmt;
        }
        catch { stmt.Dispose(); throw; }
    }
    private void Exec(string sql)
    {
        var rc = Native.sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error);
        if (rc == 0) return;
        var message = Marshal.PtrToStringUTF8(error) ?? Error();
        Native.sqlite3_free(error);
        throw new IOException(message);
    }
    private string Error() => db == IntPtr.Zero ? "SQLite açılamadı." : Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(db)) ?? "SQLite hatası.";
    public void Dispose() { dailyStop.Cancel(); lock(this){if (db != IntPtr.Zero) {foreach(var pointer in statements.Values)Native.sqlite3_finalize(pointer);statements.Clear();Native.sqlite3_close_v2(db); db = IntPtr.Zero; }} }
    private sealed class Statement(IntPtr pointer,bool cached) : IDisposable
    {
        public IntPtr Pointer { get; } = pointer;
        public void Dispose(){if(cached){Native.sqlite3_reset(Pointer);Native.sqlite3_clear_bindings(Pointer);}else Native.sqlite3_finalize(Pointer);}
    }
    private static class Native
    {
        private const string Lib = "winsqlite3.dll";
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_get_autocommit(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr error);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void sqlite3_free(IntPtr ptr);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr stmt, IntPtr tail);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_reset(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_clear_bindings(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_backup_init(IntPtr target,[MarshalAs(UnmanagedType.LPUTF8Str)] string targetName,IntPtr source,[MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_step(IntPtr backup,int pages);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_backup_finish(IntPtr backup);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_null(IntPtr stmt, int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr stmt, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, IntPtr destructor);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_blob(IntPtr stmt, int index, byte[] value, int length, IntPtr destructor);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr stmt, int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr stmt, int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_blob(IntPtr stmt, int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr stmt, int index);
    }
}
