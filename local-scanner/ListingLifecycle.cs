using System.Text.Json;
using System.Text.Json.Nodes;

namespace Metin2Search.Local;

public sealed partial class LocalStore
{
    private string DatasetId = "";
    private static string Scope(TradeCategory c) => $"ch1:{c.Job}:{c.Category}:{c.Sub}";
    private void InitializeListingLifecycle()
    {
        Execute("INSERT OR IGNORE INTO trade_metadata VALUES('dataset_id',?)",Guid.NewGuid().ToString("N"));
        DatasetId=(string)Query("SELECT value FROM trade_metadata WHERE key='dataset_id'")[0][0]!;
        Execute("INSERT OR IGNORE INTO trade_metadata VALUES('lifecycle_revision','0')");
        Exec("""
            CREATE TABLE IF NOT EXISTS listing_lifecycle(key TEXT PRIMARY KEY,is_active INTEGER NOT NULL,last_seen TEXT NOT NULL,removed_at TEXT,reason TEXT NOT NULL,version_id INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS lifecycle_runs(run_id TEXT PRIMARY KEY,server TEXT NOT NULL,channel INTEGER NOT NULL,expected_scopes INTEGER NOT NULL,status TEXT NOT NULL,started TEXT NOT NULL,completed_at TEXT);
            CREATE TABLE IF NOT EXISTS scan_query_counts(run_id TEXT PRIMARY KEY,expected_queries INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS scan_query_progress(query_id TEXT PRIMARY KEY,run_id TEXT NOT NULL,label TEXT NOT NULL,status TEXT NOT NULL,pages INTEGER NOT NULL,expected INTEGER NOT NULL,message TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_scan_query_run ON scan_query_progress(run_id,status);
            CREATE TABLE IF NOT EXISTS scope_snapshots(run_id TEXT NOT NULL,scope_id TEXT NOT NULL,server TEXT NOT NULL,channel INTEGER NOT NULL,job INTEGER NOT NULL,category INTEGER NOT NULL,sub INTEGER NOT NULL,status TEXT NOT NULL,pages INTEGER NOT NULL DEFAULT 0,total INTEGER,completed_at TEXT,PRIMARY KEY(run_id,scope_id));
            CREATE TABLE IF NOT EXISTS scope_pages(run_id TEXT NOT NULL,scope_id TEXT NOT NULL,page_id TEXT NOT NULL,page INTEGER NOT NULL,total INTEGER NOT NULL,item_count INTEGER NOT NULL,PRIMARY KEY(run_id,scope_id,page_id));
            CREATE TABLE IF NOT EXISTS scope_seen(run_id TEXT NOT NULL,scope_id TEXT NOT NULL,key TEXT NOT NULL,version_id INTEGER NOT NULL,last_seen TEXT NOT NULL,PRIMARY KEY(run_id,scope_id,key));
            CREATE TABLE IF NOT EXISTS scope_memberships(scope_id TEXT NOT NULL,key TEXT NOT NULL,last_seen TEXT NOT NULL,last_run TEXT NOT NULL,misses INTEGER NOT NULL,is_active INTEGER NOT NULL,PRIMARY KEY(scope_id,key));
            CREATE TABLE IF NOT EXISTS lifecycle_events(id INTEGER PRIMARY KEY,key TEXT NOT NULL,occurred TEXT NOT NULL,is_active INTEGER NOT NULL,reason TEXT NOT NULL,run_id TEXT);
            CREATE TABLE IF NOT EXISTS version_identity(version_id INTEGER PRIMARY KEY,page_id TEXT,slot INTEGER,source TEXT NOT NULL,raw05 INTEGER,raw00 INTEGER);
            CREATE TABLE IF NOT EXISTS scan_run_kinds(run_id TEXT PRIMARY KEY,kind TEXT NOT NULL);
            CREATE VIEW IF NOT EXISTS compact_identity AS
                SELECT v.id AS version_id,m.value||':'||v.id AS observation_id,v.key AS listing_id,
                    NULL AS physical_item_id,'unproven' AS physical_item_verification,
                    'server+legacy-feature-group; not physical identity' AS listing_scope,
                    e.page_id,e.slot,e.source,e.raw05,e.raw00
                FROM compact_versions v CROSS JOIN trade_metadata m LEFT JOIN version_identity e ON e.version_id=v.id
                WHERE m.key='dataset_id';
            """);
        if((string?)Query("SELECT type FROM sqlite_master WHERE name='scope_memberships'").FirstOrDefault()?[0]=="table")
            Exec("CREATE INDEX IF NOT EXISTS ix_memberships_key ON scope_memberships(key,is_active)");
        InitializeCategoryDelivery();
        InitializeVerifiedShopLayouts();
        InitializePurchaseChecks();
    }
    private void RecordListingSeen(TradePage page,TradeItem item,string key,string time)
    {
        foreach(var category in TradePlan.Covered(page.Category))RecordListingSeenScope(page with{Category=category},item,key,time);
    }
    private void RecordListingSeenScope(TradePage page,TradeItem item,string key,string time)
    {
        var versionId=(long)Query("SELECT MAX(id) FROM compact_versions WHERE key=?",key)[0][0]!;
        Execute("INSERT OR IGNORE INTO version_identity VALUES(?,?,?,?,?,?)",versionId,page.Id,item.Slot,"search-result-187/server",ItemDecoder.U32(item.Raw,5),item.Raw[0]);
        Execute("UPDATE version_identity SET page_id=COALESCE(page_id,?) WHERE version_id=?",page.Id,versionId);
        if(page.RunId is null||page.QueryId is null||ServerName is null)return;
        var scope=Scope(page.Category);
        if(Query("SELECT 1 FROM scope_snapshots s JOIN lifecycle_runs r ON r.run_id=s.run_id WHERE s.run_id=? AND s.scope_id=? AND s.status='Running' AND r.status='Running' AND s.server=?",page.RunId,scope,ServerName).Count==0)return;
        Execute("INSERT INTO scope_seen VALUES(?,?,?,?,?) ON CONFLICT(run_id,scope_id,key) DO UPDATE SET version_id=excluded.version_id,last_seen=excluded.last_seen",page.RunId,scope,key,versionId,time);
        var old=Query("SELECT is_active FROM listing_lifecycle WHERE key=?",key);
        Execute("INSERT INTO listing_lifecycle VALUES(?,1,?,NULL,'Observed',?) ON CONFLICT(key) DO UPDATE SET is_active=1,last_seen=excluded.last_seen,removed_at=NULL,reason='Observed',version_id=excluded.version_id",key,time,versionId);
        Execute("INSERT OR REPLACE INTO scope_memberships VALUES(?,?,?,?,0,1)",scope,key,time,page.RunId);
        if(old.Count==0||(long)old[0][0]! == 0)Execute("INSERT INTO lifecycle_events(key,occurred,is_active,reason,run_id) VALUES(?,?,1,'Observed',?)",key,time,page.RunId);
    }
    private void RecordScopePage(TradePage page)
    {
        foreach(var category in TradePlan.Covered(page.Category))RecordScopePageSingle(page with{Category=category});
    }
    private void RecordScopePageSingle(TradePage page)
    {
        if(page.RunId is null||page.QueryId is null||ServerName is null)return;
        Execute("INSERT OR IGNORE INTO scope_pages SELECT ?,?,?,?,?,? WHERE EXISTS(SELECT 1 FROM scope_snapshots WHERE run_id=? AND scope_id=? AND status='Running')",page.RunId,Scope(page.Category),page.Id,page.Page,page.Total,page.Raw.Length/187,page.RunId,Scope(page.Category));
        BumpLifecycleRevision();
    }
    private void LifecycleAudit(TradeAudit data,string time)
    {
        if(ServerName is null)return;
        BumpLifecycleRevision();
        if(data.Kind=="RunStart"){
            Execute("INSERT OR IGNORE INTO lifecycle_runs VALUES(?,?,1,?,'Running',?,NULL)",data.RunId,ServerName,data.Expected,time);
            Execute("INSERT OR IGNORE INTO scan_run_kinds VALUES(?,?)",data.RunId,data.Mode??"Full");
            return;
        }
        if(data.Kind=="RunEnd"){CompleteLifecycleRun(data.RunId,time);return;}
        if(data.Category is null)return;
        var scope=Scope(data.Category);
        if(data.Kind=="Pending")Execute("INSERT OR IGNORE INTO scope_snapshots(run_id,scope_id,server,channel,job,category,sub,status) VALUES(?,?,?,1,?,?,?,'Pending')",data.RunId,scope,ServerName,data.Category.Job,data.Category.Category,data.Category.Sub);
        else if(data.Kind=="Running")Execute("UPDATE scope_snapshots SET status='Running' WHERE run_id=? AND scope_id=? AND status='Pending'",data.RunId,scope);
        else if(data.Kind=="Failed")Execute("UPDATE scope_snapshots SET status='Failed' WHERE run_id=? AND scope_id=?",data.RunId,scope);
        else if(data.Kind=="HotDone")Execute("UPDATE scope_snapshots SET status='Partial',pages=?,completed_at=? WHERE run_id=? AND scope_id=? AND status='Running'",data.Pages,time,data.RunId,scope);
        else if(data.Kind=="Complete"){
            var pages=Query("SELECT page,total,item_count FROM scope_pages WHERE run_id=? AND scope_id=? ORDER BY page",data.RunId,scope);
            long total=pages.Count>0?(long)pages[0][1]!:0;
            var expected=Math.Max(1,(total+10)/11);
            bool valid=pages.Count==expected&&data.Pages==expected&&data.Expected==(total+10)/11;
            for(int i=0;i<pages.Count&&valid;i++){
                var p=(long)pages[i][0]!;var count=(long)pages[i][2]!;
                valid=(long)pages[i][1]! == total && (total==0?(p is 0 or 1)&&count==0:p==i+1&&count==Math.Min(11,total-i*11));
            }
            Execute("UPDATE scope_snapshots SET status=?,pages=?,total=?,completed_at=? WHERE run_id=? AND scope_id=? AND status='Running'",valid?"Complete":"Incomplete",pages.Count,total,time,data.RunId,scope);
        }
    }
    private void CompleteLifecycleRun(string run,string time)
    {
        var r=Query("SELECT expected_scopes,status,server FROM lifecycle_runs WHERE run_id=?",run);
        if(r.Count==0||(string)r[0][1]! != "Running")return;
        var scopes=Query("SELECT scope_id,status FROM scope_snapshots WHERE run_id=?",run);
        bool complete=(string)r[0][2]! == ServerName&&scopes.Count==(long)r[0][0]!&&scopes.Count>0&&scopes.All(s=>(string)s[1]! == "Complete");
        var kind=RunKind(run);
        var finalStatus=kind switch{"Hot"=>"Partial","Sweep"=>complete?"SweepComplete":"SweepIncomplete",_=>complete?"Complete":"Incomplete"};
        Execute("UPDATE lifecycle_runs SET status=?,completed_at=? WHERE run_id=?",finalStatus,time,run);
        if(kind is "Hot" or "Sweep"){ClearLifecycleStaging(run);if(kind=="Hot")PruneHotRuns(run);return;}
        if(UsesCategoryDelivery(run)){ClearLifecycleStaging(run);return;}
        if(!complete){Execute("UPDATE scope_memberships SET misses=0");ClearLifecycleStaging(run);return;}
        foreach(var scope in scopes){
            Execute("UPDATE scope_memberships SET misses=misses+1,last_run=? WHERE scope_id=? AND NOT EXISTS(SELECT 1 FROM scope_seen s WHERE s.run_id=? AND s.scope_id=scope_memberships.scope_id AND s.key=scope_memberships.key)",run,scope[0],run);
            Execute("UPDATE scope_memberships SET is_active=0 WHERE scope_id=? AND misses>=2",scope[0]);
        }
        foreach(var row in Query("SELECT l.key FROM listing_lifecycle l WHERE l.is_active=1 AND EXISTS(SELECT 1 FROM scope_memberships m WHERE m.key=l.key) AND NOT EXISTS(SELECT 1 FROM scope_memberships m WHERE m.key=l.key AND m.is_active=1)")){
            Execute("UPDATE listing_lifecycle SET is_active=0,removed_at=?,reason='AbsentInTwoCompleteRuns' WHERE key=?",time,row[0]);
            Execute("INSERT INTO lifecycle_events(key,occurred,is_active,reason,run_id) VALUES(?,?,0,'AbsentInTwoCompleteRuns',?)",row[0],time,run);
        }
        ClearLifecycleStaging(run);
    }
    public bool HasCompleteScopeRead(string scopeId)=>Query("SELECT 1 FROM scope_snapshots WHERE scope_id=? AND status='Complete' LIMIT 1",scopeId).Count>0;
    private void ClearLifecycleStaging(string run){Execute("DELETE FROM scope_seen WHERE run_id=?",run);Execute("DELETE FROM scope_pages WHERE run_id=?",run);Execute("DELETE FROM category_delivery_scopes WHERE run_id=?",run);}
    private void BumpLifecycleRevision()=>Execute("UPDATE trade_metadata SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT) WHERE key='lifecycle_revision'");
    private TradeItem WithIdentity(TradeItem item,long versionId,string key,object?[] state)
    {
        bool? active=null;string? removed=null;string reason="LegacyUnscoped";
        if(state[0] is not null){active=(long)state[0]! == 1&&(long)state[4]! == versionId;removed=(string?)state[2];reason=(long)state[4]! == versionId?(string)state[3]!:"SupersededVersion";}
        return item with{ObservationId=DatasetId+":"+versionId,ListingId=key,IsActive=active,RemovedAt=removed,LifecycleReason=reason,LastSeen=(string?)state[1]??item.LastSeen};
    }
    private static void AddIdentityJson(JsonObject json,TradeItem item)
    {
        json["ObservationId"]=item.ObservationId;json["ListingId"]=item.ListingId;json["PhysicalItemId"]=null;
        json["IdentityEvidence"]=JsonSerializer.SerializeToNode(item.IdentityEvidence,JsonData.Options);
        json["IsActive"]=item.IsActive;json["RemovedAt"]=item.RemovedAt;json["LifecycleReason"]=item.LifecycleReason;
    }
    public void ExportActiveTrade(string destination)
    {
        lock(this){
            using var writer=new StreamWriter(destination,false,new System.Text.UTF8Encoding(false));
            writer.Write("{\"SchemaVersion\":1,\"DatasetId\":"+JsonSerializer.Serialize(DatasetId)+",\"LifecycleRevision\":"+(string)Query("SELECT value FROM trade_metadata WHERE key='lifecycle_revision'")[0][0]!+",\"Server\":"+JsonSerializer.Serialize(ServerName)+",\"Channel\":1,\"ExportedAt\":"+JsonSerializer.Serialize(DateTimeOffset.UtcNow)+",\"Items\":[");
            bool comma=false;long cursor=0;
            while(true){var rows=ExportTradeRows(cursor,true);if(rows.Count==0)break;
                foreach(var row in rows){cursor=(long)row[0]!;var item=DecodeTradeRow(row,1);if(item.IsActive!=true)continue;
                    if(comma)writer.Write(',');var json=JsonNode.Parse(Unzip((byte[])row[1]!))!.AsObject();AddIdentityJson(json,item);json["Server"]=ServerName;json["LastSeen"]=item.LastSeen;writer.Write(json.ToJsonString(JsonData.Options));comma=true;}}
            writer.Write("],\"ScopeSnapshots\":");WriteRows(writer,"SELECT * FROM scope_snapshots ORDER BY rowid");
            writer.Write(",\"Runs\":");WriteRows(writer,"SELECT * FROM lifecycle_runs ORDER BY rowid");
            writer.Write(",\"LifecycleEvents\":");WriteRows(writer,"SELECT * FROM lifecycle_events ORDER BY id");writer.Write('}');
        }
    }
    private void WriteRows(TextWriter writer,string sql)
    {
        var names=sql.Contains("scope_snapshots")?new[]{"run_id","scope_id","server","channel","job","category","sub","status","pages","total","completed_at"}:
            sql.Contains("lifecycle_runs")?new[]{"run_id","server","channel","expected_scopes","status","started","completed_at"}:new[]{"id","key","occurred","is_active","reason","run_id"};
        writer.Write(JsonSerializer.Serialize(Query(sql).Select(r=>names.Select((n,i)=>(n,r[i])).ToDictionary(p=>p.n,p=>p.Item2)),JsonData.Options));
    }
}
