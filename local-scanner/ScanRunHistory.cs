namespace Metin2Search.Local;

public sealed record RecentScanRow(string Started,string Kind,string Duration,string Status,string Categories);
public sealed record ScanRunRow(string RunId,string Started,string Finished,string Duration,string Status,long Completed,long Expected);
public sealed partial class LocalStore
{
    public List<ScanRunRow> ScanRuns()=>Query("SELECT r.run_id,r.started,r.completed_at,r.status,COALESCE(c.expected_queries,r.expected_scopes),CASE WHEN c.run_id IS NOT NULL THEN (SELECT COUNT(*) FROM scan_query_progress p WHERE p.run_id=r.run_id AND p.status='Complete') ELSE (SELECT COUNT(*) FROM scope_snapshots s WHERE s.run_id=r.run_id AND s.status='Complete') END FROM lifecycle_runs r LEFT JOIN scan_query_counts c ON c.run_id=r.run_id WHERE NOT EXISTS(SELECT 1 FROM scan_run_kinds k WHERE k.run_id=r.run_id AND k.kind='Hot') ORDER BY r.started DESC LIMIT 100")
        .Select(r=>{
            var start=DateTimeOffset.Parse((string)r[1]!);var end=r[2] is string text?DateTimeOffset.Parse(text):DateTimeOffset.UtcNow;
            return new ScanRunRow((string)r[0]!,start.ToLocalTime().ToString("dd.MM HH:mm:ss"),r[2] is null?"—":end.ToLocalTime().ToString("HH:mm:ss"),FormatScanDuration(end-start),
                (string)r[3]! switch{"Complete"=>"Tamamlandı","Incomplete"=>"Eksik / hatalı","Interrupted"=>"Yarıda kesildi","Running"=>"Taranıyor","SweepComplete"=>"Doğrulama tamam","SweepIncomplete"=>"Doğrulama eksik","Partial"=>"Hızlı geçiş",var s=>s},(long)r[5]!, (long)r[4]!);
        }).ToList();
    public List<RecentScanRow> RecentScans(int limit)=>Query("SELECT r.run_id,r.started,r.completed_at,r.status,COALESCE(c.expected_queries,r.expected_scopes),(SELECT COUNT(*) FROM scan_query_progress p WHERE p.run_id=r.run_id AND p.status IN ('Complete','HotDone')),COALESCE(k.kind,'Full') FROM lifecycle_runs r LEFT JOIN scan_query_counts c ON c.run_id=r.run_id LEFT JOIN scan_run_kinds k ON k.run_id=r.run_id ORDER BY r.started DESC LIMIT ?",(long)limit)
        .Select(r=>{
            var start=DateTimeOffset.Parse((string)r[1]!);var end=r[2] is string text?DateTimeOffset.Parse(text):DateTimeOffset.UtcNow;
            var kind=(string)r[6]! switch{"Hot"=>"Hızlı","Sweep"=>"Doğrulama",_=>"Tam"};
            var status=(string)r[3]! switch{"Complete"=>"Tamamlandı","Incomplete"=>"Eksik / hatalı","Interrupted"=>"Kesildi","Running"=>"Taranıyor","SweepComplete"=>"Tamamlandı","SweepIncomplete"=>"Eksik","Partial"=>"Tamamlandı",var s=>s};
            return new RecentScanRow(start.ToLocalTime().ToString("dd.MM HH:mm:ss"),kind,r[2] is null?"…":FormatScanDuration(end-start),status,$"{(long)r[5]!} / {(long)r[4]!}");
        }).ToList();
    internal static string FormatScanDuration(TimeSpan duration)=>$"{Math.Max(0,(long)duration.TotalMinutes)} dk {Math.Max(0,duration.Seconds):00} sn";
    public List<TradeCoverageRow> TradePlanCoverage(){
        var rows=Query("SELECT label,status,pages,expected,message FROM scan_query_progress WHERE run_id=(SELECT id FROM trade_runs ORDER BY started DESC LIMIT 1) ORDER BY rowid");
        return rows.Count==0?TradeCoverage():rows.Select(r=>new TradeCoverageRow((string)r[0]!, (string)r[1]!, (long)r[2]!, (long)r[3]!, (string)r[4]!)).ToList();
    }
}
