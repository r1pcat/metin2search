using System.Diagnostics;

namespace Metin2Search.Local;

public sealed class ScanMetrics
{
    private sealed class Stage{public long Count,Ticks,Max;public Queue<long> Recent=new();}
    private readonly Dictionary<string,Stage> stages=[];
    private readonly Stopwatch elapsed=Stopwatch.StartNew();
    private long pages,items,changed,unchanged;
    public void Record(string name,TimeSpan duration){lock(stages){if(!stages.TryGetValue(name,out var s))stages[name]=s=new();s.Count++;s.Ticks+=duration.Ticks;s.Max=Math.Max(s.Max,duration.Ticks);s.Recent.Enqueue(duration.Ticks);if(s.Recent.Count>256)s.Recent.Dequeue();}}
    public void Page(int count,int changes,int same){lock(stages){pages++;items+=count;changed+=changes;unchanged+=same;}}
    public object Snapshot(){lock(stages){return new{elapsedSeconds=elapsed.Elapsed.TotalSeconds,pages,items,changed,unchanged,pagesPerSecond=pages/Math.Max(0.001,elapsed.Elapsed.TotalSeconds),stages=stages.ToDictionary(p=>p.Key,p=>{var s=p.Value;var recent=s.Recent.Order().ToArray();return new{count=s.Count,meanMs=TimeSpan.FromTicks(s.Ticks/Math.Max(1,s.Count)).TotalMilliseconds,p50Ms=Percentile(recent,.5),p95Ms=Percentile(recent,.95),p99Ms=Percentile(recent,.99),maxMs=TimeSpan.FromTicks(s.Max).TotalMilliseconds,window=recent.Length};})};}}
    private static double Percentile(long[] values,double p)=>values.Length==0?0:TimeSpan.FromTicks(values[(int)Math.Ceiling((values.Length-1)*p)]).TotalMilliseconds;
}

public sealed partial class LocalStore
{
    private readonly Stopwatch statusClock=Stopwatch.StartNew();
    private bool statusPublished;
    private void InitializeScanStatus()=>Exec("CREATE TABLE IF NOT EXISTS scanner_status(id INTEGER PRIMARY KEY CHECK(id=1),updated_at TEXT NOT NULL,source_revision TEXT NOT NULL,version_id INTEGER NOT NULL,latest_seen TEXT,last_complete_run TEXT,current_run_started TEXT,metrics_json TEXT NOT NULL,coverage_json TEXT NOT NULL,import_interval_seconds INTEGER NOT NULL)");
    public void PublishScannerStatus(bool force=false)
    {
        lock(this){
            if(!force&&statusPublished&&statusClock.Elapsed<TimeSpan.FromSeconds(60))return;
            var coverage=Query("SELECT s.scope_id,s.status,s.pages,s.total,(SELECT MAX(c.completed_at) FROM scope_snapshots c WHERE c.scope_id=s.scope_id AND c.status='Complete') FROM scope_snapshots s WHERE s.run_id=(SELECT r.run_id FROM lifecycle_runs r WHERE NOT EXISTS(SELECT 1 FROM scan_run_kinds k WHERE k.run_id=r.run_id AND k.kind IN ('Hot','Sweep')) ORDER BY r.started DESC LIMIT 1)")
                .Select(r=>new{scope=(string)r[0]!,status=(string)r[1]!,pages=r[2],total=r[3],lastSuccessfulCompletedAt=r[4]}).ToArray();
            Execute("INSERT OR REPLACE INTO scanner_status VALUES(1,?,?,?,?,?,?,?,?,60)",DateTimeOffset.UtcNow.ToString("O"),Query("SELECT value FROM trade_metadata WHERE key='lifecycle_revision'")[0][0],Query("SELECT COALESCE(MAX(id),0) FROM compact_versions")[0][0],Query("SELECT MAX(last_seen) FROM listing_lifecycle")[0][0],Query("SELECT MAX(completed_at) FROM lifecycle_runs WHERE status='Complete'")[0][0],Query("SELECT MAX(started) FROM lifecycle_runs WHERE status='Running'")[0][0],JsonData.Encode(Metrics.Snapshot()),JsonData.Encode(coverage));
            statusPublished=true;statusClock.Restart();
        }
    }
}
