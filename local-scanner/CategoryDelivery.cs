using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

public sealed record CategoryBatch(string BatchId,string CategoryKey,long Revision,string ScanId,string CompletedAt,string Encoding,string BodyHash,byte[] Body);
public sealed record CategoryAck(int SchemaVersion,string DatasetId,string Server,string CategoryKey,long Revision,string BatchId,string BodyHash);

public sealed partial class LocalStore
{
    private void InitializeCategoryDelivery(){Exec("""
        CREATE TABLE IF NOT EXISTS category_delivery_runs(run_id TEXT PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS category_delivery_scopes(scan_id TEXT NOT NULL,run_id TEXT NOT NULL,category_key TEXT NOT NULL,scope_id TEXT NOT NULL,PRIMARY KEY(scan_id,scope_id));
        CREATE INDEX IF NOT EXISTS ix_category_delivery_run ON category_delivery_scopes(run_id);
        CREATE TABLE IF NOT EXISTS category_delivery_state(category_key TEXT PRIMARY KEY,revision INTEGER NOT NULL,acked_revision INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS category_delivery_items(category_key TEXT NOT NULL,key TEXT NOT NULL,version_id INTEGER NOT NULL,is_present INTEGER NOT NULL,is_active INTEGER NOT NULL,PRIMARY KEY(category_key,key));
        CREATE TABLE IF NOT EXISTS category_outbox(batch_id TEXT PRIMARY KEY,category_key TEXT NOT NULL,revision INTEGER NOT NULL,scan_id TEXT NOT NULL UNIQUE,completed_at TEXT NOT NULL,encoding TEXT NOT NULL,body_hash TEXT NOT NULL,body BLOB,acked_at TEXT,UNIQUE(category_key,revision));
        CREATE INDEX IF NOT EXISTS ix_category_pending ON category_outbox(acked_at,category_key,revision);
        """);ProcessCategoryAcknowledgements();}

    private bool UsesCategoryDelivery(string runId)=>Query("SELECT 1 FROM category_delivery_runs WHERE run_id=?",runId).Count>0;

    private void CategoryDeliveryAudit(TradeAudit data,string time)
    {
        if(ServerName is null)return;
        if(data.Kind=="RunStart"&&data.PlannedQueries is not null){
            Execute("INSERT OR REPLACE INTO trade_metadata VALUES('category_delivery_protocol','1')");
            Execute("INSERT OR REPLACE INTO trade_metadata VALUES('category_delivery_trigger','category-complete')");
            Execute("INSERT OR IGNORE INTO category_delivery_runs VALUES(?)",data.RunId);return;
        }
        if(data.Category is null||data.PlanQueryId is null)return;
        var categoryKey=Scope(data.PlanCategory??data.Category);
        if(data.Kind=="Pending"){
            Execute("INSERT OR IGNORE INTO category_delivery_scopes VALUES(?,?,?,?)",data.PlanQueryId,data.RunId,categoryKey,Scope(data.Category));return;
        }
        if(data.Kind=="HotDone"&&UsesCategoryDelivery(data.RunId)){
            if(Query("SELECT 1 FROM category_outbox WHERE scan_id=?",data.PlanQueryId).Count>0)return;
            var hotScopes=Query("SELECT d.scope_id,s.status FROM category_delivery_scopes d LEFT JOIN scope_snapshots s ON s.run_id=d.run_id AND s.scope_id=d.scope_id WHERE d.scan_id=?",data.PlanQueryId);
            if(hotScopes.Count==0||hotScopes.Any(s=>(string?)s[1]!="Partial"))return;
            CompleteCategory(data.RunId,data.PlanQueryId,categoryKey,hotScopes.Select(s=>(string)s[0]!).ToArray(),time,incremental:true);
            return;
        }
        if(data.Kind!="Complete"||!UsesCategoryDelivery(data.RunId))return;
        if(Query("SELECT 1 FROM category_outbox WHERE scan_id=?",data.PlanQueryId).Count>0)return;
        var scopes=Query("SELECT d.scope_id,s.status FROM category_delivery_scopes d LEFT JOIN scope_snapshots s ON s.run_id=d.run_id AND s.scope_id=d.scope_id WHERE d.scan_id=?",data.PlanQueryId);
        if(scopes.Count==0||scopes.Any(s=>(string?)s[1]!="Complete"))return;
        var run=Query("SELECT status,expected_scopes FROM lifecycle_runs WHERE run_id=?",data.RunId);
        if(run.Count==0||(string)run[0][0]! != "Running"||Query("SELECT 1 FROM scope_snapshots WHERE run_id=?",data.RunId).Count!=(long)run[0][1]!)return;
        CompleteCategory(data.RunId,data.PlanQueryId,categoryKey,scopes.Select(s=>(string)s[0]!).ToArray(),time);
    }

    private void CompleteCategory(string runId,string scanId,string categoryKey,string[] scopes,string time,bool incremental=false)
    {
        var preparation=System.Diagnostics.Stopwatch.StartNew();
        var candidates=new HashSet<string>(StringComparer.Ordinal);
        var seen=new Dictionary<string,long>(StringComparer.Ordinal);
        var consistent=!incremental&&SingleSweepRemoval&&scopes.All(scope=>{
            var total=Query("SELECT total FROM scope_snapshots WHERE run_id=? AND scope_id=? AND status='Complete'",runId,scope).FirstOrDefault()?[0] as long?;
            return total is not null&&(long)Query("SELECT COUNT(*) FROM scope_seen WHERE run_id=? AND scope_id=?",runId,scope)[0][0]! == total;
        });
        var threshold=consistent?1:2;
        var removalReason=consistent?"AbsentInCompleteScanWithCount":"AbsentInTwoCompleteCategoryScans";
        foreach(var scope in scopes){
            if(!incremental)foreach(var r in Query("SELECT m.key FROM scope_memberships m LEFT JOIN category_delivery_items i ON i.category_key=? AND i.key=m.key WHERE m.scope_id=? AND (m.is_active=1 OR i.is_present=1 OR i.is_active=1)",categoryKey,scope))candidates.Add((string)r[0]!);
            foreach(var r in Query("SELECT key,version_id FROM scope_seen WHERE run_id=? AND scope_id=?",runId,scope)){
                var key=(string)r[0]!;seen[key]=Math.Max(seen.GetValueOrDefault(key),(long)r[1]!);
            }
            if(incremental)continue;
            Execute("UPDATE scope_memberships SET misses=misses+1,last_run=? WHERE scope_id=? AND is_active=1 AND NOT EXISTS(SELECT 1 FROM scope_seen s WHERE s.run_id=? AND s.scope_id=scope_memberships.scope_id AND s.key=scope_memberships.key)",runId,scope,runId);
            Execute("UPDATE scope_memberships SET is_active=0 WHERE scope_id=? AND misses>=? AND is_active=1",scope,threshold);
        }
        if(incremental)foreach(var key in seen.Keys)candidates.Add(key);
        var tombstones=new List<object>();
        if(!incremental)foreach(var key in candidates){
            if(Query("SELECT 1 FROM listing_lifecycle l WHERE l.key=? AND l.is_active=1 AND NOT EXISTS(SELECT 1 FROM scope_memberships m WHERE m.key=l.key AND m.is_active=1)",key).Count==0)continue;
            Execute("UPDATE listing_lifecycle SET is_active=0,removed_at=?,reason=? WHERE key=?",time,removalReason,key);
            Execute("INSERT INTO lifecycle_events(key,occurred,is_active,reason,run_id) VALUES(?,?,0,?,?)",key,time,removalReason,runId);
            var eventId=(long)Query("SELECT MAX(id) FROM lifecycle_events WHERE key=?",key)[0][0]!;
            var versionId=(long)Query("SELECT version_id FROM listing_lifecycle WHERE key=?",key)[0][0]!;
            tombstones.Add(new{listingKey=key,reason=removalReason,lifecycleEventId=eventId,versionId,removedAt=time});
        }
        var upserts=new List<object>();var membershipRemovals=new List<object>();var payloads=new Dictionary<string,string>();
        foreach(var key in candidates){
            var old=Query("SELECT version_id,is_present,is_active FROM category_delivery_items WHERE category_key=? AND key=?",categoryKey,key).FirstOrDefault();
            var observed=seen.ContainsKey(key);
            if(!observed&&old is null)continue;
            var active=(long?)Query("SELECT is_active FROM listing_lifecycle WHERE key=?",key).FirstOrDefault()?[0] == 1;
            var present=scopes.Any(scope=>Query("SELECT 1 FROM scope_memberships WHERE scope_id=? AND key=? AND is_active=1",scope,key).Count>0);
            var version=seen.GetValueOrDefault(key,old is null?0:(long)old[0]!);
            if(old is not null)version=Math.Max(version,(long)old[0]!);
            if(observed&&(old is null||version!=(long)old[0]!||(long)old[1]! !=1||(long)old[2]! != (active?1:0))){
                var row=Query("SELECT v.payload_hash,p.bytes,v.observed FROM compact_versions v JOIN compact_payloads p ON p.hash=v.payload_hash WHERE v.id=? AND v.key=?",version,key).Single();
                var hash=((string)row[0]!).ToLowerInvariant();payloads.TryAdd(hash,Unzip((byte[])row[1]!));
                var parts=categoryKey.Split(':');
                var eventId=(long)Query("SELECT COALESCE(MAX(id),0) FROM lifecycle_events WHERE key=?",key)[0][0]!;
                upserts.Add(new{listingKey=key,versionId=version,payloadHash=hash,isActive=active,lifecycleEventId=eventId,observed=(string)row[2]!,category=new{Job=int.Parse(parts[1]),Category=int.Parse(parts[2]),Sub=int.Parse(parts[3])}});
            }
            if(old is not null&&(long)old[1]! ==1&&!present)membershipRemovals.Add(new{listingKey=key,reason=removalReason});
            var publishedPresent=present&&(observed||(old is not null&&(long)old[1]! ==1));
            var publishedActive=active&&(observed||(old is not null&&(long)old[2]! ==1));
            if(version>0)Execute("INSERT INTO category_delivery_items VALUES(?,?,?,?,?) ON CONFLICT(category_key,key) DO UPDATE SET version_id=excluded.version_id,is_present=excluded.is_present,is_active=excluded.is_active",categoryKey,key,version,publishedPresent?1:0,publishedActive?1:0);
        }
        if(incremental&&upserts.Count==0){
            foreach(var scope in scopes){Execute("DELETE FROM scope_seen WHERE run_id=? AND scope_id=?",runId,scope);Execute("DELETE FROM scope_pages WHERE run_id=? AND scope_id=?",runId,scope);}
            Execute("DELETE FROM category_delivery_scopes WHERE scan_id=?",scanId);
            return;
        }
        var previous=(long?)Query("SELECT revision FROM category_delivery_state WHERE category_key=?",categoryKey).FirstOrDefault()?[0]??0;
        var revision=checked(previous+1);
        var json=JsonData.Encode(new{schemaVersion=1,datasetId=DatasetId,server=ServerName,channel=1,categoryKey,scanId,revision,previousRevision=previous,completedAt=time,complete=!incremental,kind=incremental?"incremental":"complete",coveredScopes=scopes,upserts,payloads,membershipRemovals,tombstones});
        var body=Zip(json);var hashOfBody=ItemDecoder.Hash(body).ToLowerInvariant();var batchId=ItemDecoder.Hash(Encoding.UTF8.GetBytes(DatasetId+"|"+categoryKey+"|"+revision)).ToLowerInvariant();
        Execute("INSERT INTO category_outbox VALUES(?,?,?,?,?,'br',?,?,NULL)",batchId,categoryKey,revision,scanId,time,hashOfBody,body);
        RecordDaily("category",batchId,new{batchId,categoryKey,revision,scanId,completedAt=time,encoding="br",bodyHash=hashOfBody},body);
        Execute("INSERT INTO category_delivery_state VALUES(?,?,0) ON CONFLICT(category_key) DO UPDATE SET revision=excluded.revision",categoryKey,revision);
        foreach(var scope in scopes){Execute("DELETE FROM scope_seen WHERE run_id=? AND scope_id=?",runId,scope);Execute("DELETE FROM scope_pages WHERE run_id=? AND scope_id=?",runId,scope);}
        Execute("DELETE FROM category_delivery_scopes WHERE scan_id=?",scanId);
        Metrics.Record("store.category.prepare",preparation.Elapsed);
    }

    public List<CategoryBatch> PendingCategoryBatches(int limit=100)
    {
        if(limit is <1 or >1000)throw new ArgumentOutOfRangeException(nameof(limit));
        return Query("SELECT batch_id,category_key,revision,scan_id,completed_at,encoding,body_hash,body FROM category_outbox WHERE acked_at IS NULL ORDER BY category_key,revision LIMIT ?",limit)
            .Select(r=>new CategoryBatch((string)r[0]!, (string)r[1]!, (long)r[2]!, (string)r[3]!, (string)r[4]!, (string)r[5]!, (string)r[6]!, (byte[])r[7]!)).ToList();
    }
    public string CategoryAckFile(string categoryKey)=>Path.Combine(Path.GetDirectoryName(FilePath)!,"category-sync","acks",ItemDecoder.Hash(Encoding.UTF8.GetBytes(categoryKey)).ToLowerInvariant()+".json");
    private void PublishCategoryReady(string scanId)
    {
        var row=Query("SELECT batch_id,category_key,revision,completed_at FROM category_outbox WHERE scan_id=?",scanId).FirstOrDefault();
        if(row is null)return;
        var folder=Path.Combine(Path.GetDirectoryName(FilePath)!,"category-sync");
        var temporary=Path.Combine(folder,"ready-"+Guid.NewGuid().ToString("N")+".tmp");
        try{
            Directory.CreateDirectory(folder);
            File.WriteAllText(temporary,JsonData.Encode(new{schemaVersion=1,datasetId=DatasetId,server=ServerName,batchId=row[0],categoryKey=row[1],revision=row[2],completedAt=row[3]}));
            File.Move(temporary,Path.Combine(folder,"ready.json"),true);
        }catch(Exception error)when(error is IOException or UnauthorizedAccessException){
        }finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}}
    }
    public void AcknowledgeCategoryRevision(CategoryAck ack)
    {
        lock(this){
            if(ack.SchemaVersion!=1||ack.DatasetId!=DatasetId||ack.Server!=ServerName||ack.Revision<1)throw new InvalidDataException("Kategori ACK sunucu/veri kümesi eşleşmedi.");
            var row=Query("SELECT batch_id,body_hash FROM category_outbox WHERE category_key=? AND revision=?",ack.CategoryKey,ack.Revision).FirstOrDefault();
            if(row is null||(string)row[0]! !=ack.BatchId||(string)row[1]! !=ack.BodyHash)throw new InvalidDataException("Kategori ACK batch/hash/revision eşleşmedi.");
            Exec("BEGIN IMMEDIATE");try{
                Execute("UPDATE category_outbox SET acked_at=COALESCE(acked_at,?),body=NULL WHERE category_key=? AND revision<=?",DateTimeOffset.UtcNow.ToString("O"),ack.CategoryKey,ack.Revision);
                Execute("UPDATE category_delivery_state SET acked_revision=MAX(acked_revision,?) WHERE category_key=?",ack.Revision,ack.CategoryKey);
                Exec("COMMIT");
            }catch{try{Exec("ROLLBACK");}catch{}throw;}
        }
    }
    public void ProcessCategoryAcknowledgements()
    {
        if(RequireUplinkAck)return;
        lock(this){
            foreach(var row in Query("SELECT category_key FROM category_delivery_state WHERE revision>acked_revision")){
                var file=CategoryAckFile((string)row[0]!);
                if(!File.Exists(file))continue;
                try{
                    if(new FileInfo(file).Length>4096)continue;
                    var ack=JsonSerializer.Deserialize<CategoryAck>(File.ReadAllText(file),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                    if(ack is not null)AcknowledgeCategoryRevision(ack);
                }catch(Exception error)when(error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException){}
            }
        }
    }
}
