namespace Metin2Search.Local;

public sealed partial class LocalStore
{
    public IDisposable AcquireScannerWriter()
    {
        FileStream lease;
        try{lease=new FileStream(FilePath+".scanner.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);}
        catch(IOException e){throw new IOException("Bu sunucuda başka bir tarayıcı/yazıcı çalışıyor; ikinci tarama başlatılmadı.",e);}
        try{
            var owner=System.Text.Encoding.UTF8.GetBytes(JsonData.Encode(new{pid=Environment.ProcessId,session=SessionId,started=DateTimeOffset.UtcNow}));
            lease.SetLength(0);lease.Write(owner);lease.Flush(true);
            lock(this){
                Exec("BEGIN IMMEDIATE");
                try{
                    if(Query("SELECT 1 FROM lifecycle_runs WHERE status='Running' LIMIT 1").Count>0){
                        if(Query("SELECT 1 FROM lifecycle_runs r WHERE r.status='Running' AND NOT EXISTS(SELECT 1 FROM category_delivery_runs d WHERE d.run_id=r.run_id)").Count>0)
                            Execute("UPDATE scope_memberships SET misses=0");
                        Execute("UPDATE scan_query_progress SET status='Interrupted' WHERE status IN ('Pending','Running') AND run_id IN (SELECT run_id FROM lifecycle_runs WHERE status='Running')");
                        Execute("UPDATE lifecycle_runs SET status='Interrupted' WHERE status='Running'");
                        Execute("UPDATE scope_snapshots SET status='Interrupted' WHERE status IN ('Pending','Running')");
                        Exec("DELETE FROM scope_seen; DELETE FROM scope_pages; DELETE FROM category_delivery_scopes;");BumpLifecycleRevision();
                    }
                    Exec("COMMIT");
                }catch{try{Exec("ROLLBACK");}catch{}throw;}
            }
            return DailyWriterLease(lease);
        }catch{lease.Dispose();throw;}
    }
}
