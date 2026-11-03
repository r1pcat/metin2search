using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Metin2Search.Local;

public sealed partial class LocalStore
{
    internal Func<DateTimeOffset> ArchiveClock {get;set;}=()=>DateTimeOffset.UtcNow;
    internal Action? ArchiveSnapshotReady {get;set;}
    private static readonly TimeZoneInfo ArchiveZone=FindArchiveZone();
    private bool dailyEnabled;
    private string dailyTransactionDay="",dailyMaintenanceDay="";
    private DateTimeOffset dailyTransactionUtc;
    private readonly System.Diagnostics.Stopwatch dailyMaintenanceClock=System.Diagnostics.Stopwatch.StartNew();
    private readonly HashSet<string> verifiedDailyArchives=[];
    private readonly SemaphoreSlim dailyGate=new(1,1);
    internal bool DailyBackgroundEnabled {get;set;}=true;
    private int dailyQueued;
    private int dailyTimerStarted;
    private readonly CancellationTokenSource dailyStop=new();
    private static TimeZoneInfo FindArchiveZone(){try{return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");}catch(TimeZoneNotFoundException){return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");}}
    internal static string ArchiveDay(DateTimeOffset utc)=>TimeZoneInfo.ConvertTime(utc,ArchiveZone).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    private void InitializeDailyArchive()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS daily_archive_pending(seq INTEGER PRIMARY KEY AUTOINCREMENT,day TEXT NOT NULL,kind TEXT NOT NULL,source_key TEXT NOT NULL,created_utc TEXT NOT NULL,metadata_json TEXT NOT NULL,blob_hash TEXT,blob BLOB,UNIQUE(kind,source_key));
            CREATE INDEX IF NOT EXISTS ix_daily_pending_day ON daily_archive_pending(day,seq);
            """);
        dailyEnabled=(string?)Query("SELECT value FROM trade_metadata WHERE key='daily_archive_protocol'").FirstOrDefault()?[0]=="1";
    }
    public void EnableDailyArchiving()
    {
        lock(this){
            if(!dailyEnabled){
                Exec("BEGIN IMMEDIATE");
                try{
                Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_archive_protocol','1')");
                Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_lifecycle_cursor',?)",Query("SELECT COALESCE(MAX(id),0) FROM lifecycle_events")[0][0]);
                Exec("COMMIT");
                dailyEnabled=true;
                }catch{try{Exec("ROLLBACK");}catch{}throw;}
            }
        }
        QueueDailyMaintenance();
    }
    private void QueueDailyMaintenance(){
        StartDailyBackground();
        if(!dailyEnabled||!DailyBackgroundEnabled||Interlocked.CompareExchange(ref dailyQueued,1,0)!=0)return;
        _=Task.Run(()=>{try{MaintainDailyArchive();}finally{Interlocked.Exchange(ref dailyQueued,0);}});
    }
    private void StartDailyBackground()
    {
        if(!dailyEnabled||!DailyBackgroundEnabled||dailyStop.IsCancellationRequested||Interlocked.CompareExchange(ref dailyTimerStarted,1,0)!=0)return;
        _=Task.Run(async()=>{
            try{while(!dailyStop.IsCancellationRequested){
                var now=ArchiveClock();
                var local=TimeZoneInfo.ConvertTime(now,ArchiveZone).Date.AddDays(1);
                var midnight=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),ArchiveZone);
                var delay=midnight-now.UtcDateTime;
                if(delay<=TimeSpan.Zero)delay=TimeSpan.FromMilliseconds(1);
                if(delay>TimeSpan.FromMinutes(1))delay=TimeSpan.FromMinutes(1);
                MaintainDailyArchive(true);
                await Task.Delay(delay,dailyStop.Token);
            }}catch(OperationCanceledException)when(dailyStop.IsCancellationRequested){}
        });
    }
    private void BeginDailyTransaction(){if(!dailyEnabled)return;dailyTransactionUtc=ArchiveClock().ToUniversalTime();dailyTransactionDay=ArchiveDay(dailyTransactionUtc);}
    private void RecordDaily(string kind,string key,object metadata,byte[]? blob=null)
    {
        if(!dailyEnabled)return;
        if(dailyTransactionDay.Length==0)BeginDailyTransaction();
        Execute("INSERT OR IGNORE INTO daily_archive_pending(day,kind,source_key,created_utc,metadata_json,blob_hash,blob) VALUES(?,?,?,?,?,?,?)",dailyTransactionDay,kind,key,dailyTransactionUtc.ToString("O"),JsonData.Encode(metadata),blob is null?null:ItemDecoder.Hash(blob).ToLowerInvariant(),blob);
    }
    private void CaptureDailyLifecycle()
    {
        if(!dailyEnabled)return;
        var cursor=long.Parse((string?)Query("SELECT value FROM trade_metadata WHERE key='daily_lifecycle_cursor'").FirstOrDefault()?[0]??"0",CultureInfo.InvariantCulture);
        var rows=Query("SELECT id,key,occurred,is_active,reason,run_id FROM lifecycle_events WHERE id>? ORDER BY id",cursor);
        foreach(var r in rows)RecordDaily("lifecycle",((long)r[0]!).ToString(CultureInfo.InvariantCulture),new{lifecycleEventId=r[0],listingKey=r[1],occurred=r[2],isActive=r[3],reason=r[4],runId=r[5]});
        if(rows.Count>0)Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_lifecycle_cursor',?)",((long)rows[^1][0]!).ToString(CultureInfo.InvariantCulture));
    }
    private sealed class JournalTag;
    private LocalStore(string file,JournalTag _)
    {
        FilePath=Path.GetFullPath(file);Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if(Native.sqlite3_open_v2(FilePath,out db,2|4|0x10000,IntPtr.Zero)!=0){var error=Error();Dispose();throw new IOException(error);}
        Native.sqlite3_busy_timeout(db,2000);Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
    }
    private LocalStore OpenDailyJournal(string day)
    {
        var journal=new LocalStore(Path.Combine(Path.GetDirectoryName(FilePath)!,"Gunler",day,"daily.sqlite"),new JournalTag());
        try{
            journal.Exec("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT NOT NULL);CREATE TABLE IF NOT EXISTS events(seq INTEGER PRIMARY KEY,kind TEXT NOT NULL,source_key TEXT NOT NULL,created_utc TEXT NOT NULL,metadata_json TEXT NOT NULL,blob_hash TEXT);CREATE TABLE IF NOT EXISTS blobs(hash TEXT PRIMARY KEY,bytes BLOB NOT NULL);");
            foreach(var pair in new Dictionary<string,string>{{"schemaVersion","1"},{"datasetId",DatasetId},{"server",ServerName!},{"day",day},{"timezone","Europe/Istanbul"}}){
                var previous=(string?)journal.Query("SELECT value FROM meta WHERE key=?",pair.Key).FirstOrDefault()?[0];
                if(previous is not null&&previous!=pair.Value)throw new InvalidDataException("Günlük kimliği eşleşmedi: "+pair.Key);
                journal.Execute("INSERT OR IGNORE INTO meta VALUES(?,?)",pair.Key,pair.Value);
            }
            return journal;
        }catch{journal.Dispose();throw;}
    }
    public void MaintainDailyArchive(bool force=false)
    {
        if(!dailyGate.Wait(0))return;
        try{MaintainDailyArchiveCore(force);}finally{dailyGate.Release();}
    }
    private void MaintainDailyArchiveCore(bool force)
    {
        string today;
        lock(this){
            if(!dailyEnabled||ServerName is null||db==IntPtr.Zero||Native.sqlite3_get_autocommit(db)==0)return;
            today=ArchiveDay(ArchiveClock());
            if(!force&&dailyMaintenanceDay==today&&dailyMaintenanceClock.Elapsed<TimeSpan.FromSeconds(1))return;
            dailyMaintenanceDay=today;dailyMaintenanceClock.Restart();
        }
            try{
                var root=Path.Combine(Path.GetDirectoryName(FilePath)!,"Gunler");Directory.CreateDirectory(root);
                using var lease=new FileStream(Path.Combine(root,".maintenance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
                Directory.CreateDirectory(Path.Combine(root,today));
                while(true){
                    var pending=Query("SELECT seq,day,kind,source_key,created_utc,metadata_json,blob_hash,blob FROM daily_archive_pending ORDER BY seq LIMIT 500");
                    if(pending.Count==0)break;
                    foreach(var group in pending.GroupBy(r=>(string)r[1]!)){
                        using var journal=OpenDailyJournal(group.Key);journal.Exec("BEGIN IMMEDIATE");
                        try{foreach(var r in group){
                            if(r[7] is byte[] bytes)journal.Execute("INSERT OR IGNORE INTO blobs VALUES(?,?)",r[6],bytes);
                            journal.Execute("INSERT OR IGNORE INTO events VALUES(?,?,?,?,?,?)",r[0],r[2],r[3],r[4],r[5],r[6]);
                        }journal.Exec("COMMIT");}catch{try{journal.Exec("ROLLBACK");}catch{}throw;}
                    }
                    Execute("DELETE FROM daily_archive_pending WHERE seq<=?",pending[^1][0]);
                }
                foreach(var folder in Directory.EnumerateDirectories(root)){
                    var day=Path.GetFileName(folder);
                    if(!DateOnly.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)||string.CompareOrdinal(day,today)>=0||!File.Exists(Path.Combine(folder,"daily.sqlite")))continue;
                    if(Query("SELECT 1 FROM daily_archive_pending WHERE day=? LIMIT 1",day).Count>0)continue;
                    using var journal=OpenDailyJournal(day);SealDailyArchive(journal,day);
                }
                Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_archive_current_day',?)",today);
                Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_archive_error','')");
            }catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException){
                try{Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_archive_error',?)",error.Message);}catch(IOException){}
            }
    }
    private static string HashFile(string file){using var input=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();}
    private void SealDailyArchive(LocalStore journal,string day)
    {
        var bounds=journal.Query("SELECT COUNT(*),COALESCE(MAX(seq),0),MIN(CAST(json_extract(metadata_json,'$.sourceVersion') AS INTEGER)),MAX(CAST(json_extract(metadata_json,'$.sourceVersion') AS INTEGER)) FROM events")[0];
        var folder=Path.Combine(Path.GetDirectoryName(journal.FilePath)!,"backup");Directory.CreateDirectory(folder);
        var manifestFile=Path.Combine(folder,"manifest.json");
        if(File.Exists(manifestFile)){
            try{
                using var doc=JsonDocument.Parse(File.ReadAllText(manifestFile));var m=doc.RootElement;var name=m.GetProperty("archiveFile").GetString()!;
                if(name!=Path.GetFileName(name))throw new InvalidDataException("Arşiv dosya adı geçersiz.");
                var compressed=Path.Combine(folder,name);
                if(m.GetProperty("schemaVersion").GetInt32()==1&&m.GetProperty("encoding").GetString()=="br"&&m.GetProperty("complete").GetBoolean()&&m.GetProperty("datasetId").GetString()==DatasetId&&m.GetProperty("server").GetString()==ServerName&&m.GetProperty("day").GetString()==day&&m.GetProperty("events").GetInt64()==(long)bounds[0]!&&m.GetProperty("lastSequence").GetInt64()==(long)bounds[1]!&&File.Exists(compressed)&&new FileInfo(compressed).Length==m.GetProperty("compressedBytes").GetInt64()){
                    var signature=manifestFile+":"+File.GetLastWriteTimeUtc(manifestFile).Ticks+":"+File.GetLastWriteTimeUtc(compressed).Ticks+":"+new FileInfo(compressed).Length;
                    if(verifiedDailyArchives.Contains(signature))return;
                    if(HashFile(compressed)==m.GetProperty("compressedSha256").GetString()&&VerifyBrotli(compressed,m.GetProperty("sqliteSha256").GetString()!,m.GetProperty("sqliteBytes").GetInt64())){verifiedDailyArchives.Add(signature);return;}
                }
            }catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or IOException or ArgumentException){}
        }
        var token=Guid.NewGuid().ToString("N");var snapshot=Path.Combine(folder,"snapshot-"+token+".tmp.sqlite");var packed=Path.Combine(folder,"archive-"+token+".tmp");var tempManifest=Path.Combine(folder,"manifest-"+token+".tmp");
        try{
            SnapshotSqlite(journal,snapshot);
            using(var check=new LocalStore(snapshot,new JournalTag())){if(check.Integrity!="ok"||(long)check.Query("SELECT COUNT(*) FROM events")[0][0]! !=(long)bounds[0]!)throw new InvalidDataException("Günlük snapshot bütünlüğü başarısız.");check.Exec("PRAGMA wal_checkpoint(TRUNCATE)");}
            var hash=HashFile(snapshot);var length=new FileInfo(snapshot).Length;
            ArchiveSnapshotReady?.Invoke();
            using(var input=File.OpenRead(snapshot))using(var output=File.Create(packed))using(var br=new BrotliStream(output,CompressionLevel.Optimal))input.CopyTo(br);
            var compressedHash=HashFile(packed);if(!VerifyBrotli(packed,hash,length))throw new InvalidDataException("Günlük sıkıştırma roundtrip başarısız.");
            var archiveFile="daily-"+compressedHash+".sqlite.br";
            var localStart=DateTime.SpecifyKind(DateOnly.ParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue),DateTimeKind.Unspecified);
            var manifest=new{schemaVersion=1,datasetId=DatasetId,server=ServerName,day,timezone="Europe/Istanbul",createdAt=ArchiveClock().ToUniversalTime().ToString("O"),complete=true,events=bounds[0],lastSequence=bounds[1],minVersionId=bounds[2],maxVersionId=bounds[3],utcStart=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart,ArchiveZone)).ToString("O"),utcEnd=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1),ArchiveZone)).ToString("O"),archiveFile,encoding="br",sqliteSha256=hash,compressedSha256=compressedHash,sqliteBytes=length,compressedBytes=new FileInfo(packed).Length};
            File.WriteAllText(tempManifest,JsonData.Encode(manifest));File.Move(packed,Path.Combine(folder,archiveFile),true);File.Move(tempManifest,manifestFile,true);
            Execute("INSERT OR REPLACE INTO trade_metadata VALUES('daily_archive_last_success',?)",JsonData.Encode(new{day,manifest=manifestFile,completedAt=ArchiveClock().ToUniversalTime().ToString("O")}));
        }finally{foreach(var file in new[]{snapshot,snapshot+"-wal",snapshot+"-shm",packed,tempManifest})try{if(File.Exists(file))File.Delete(file);}catch(IOException){}}
    }
    private static bool VerifyBrotli(string file,string hash,long bytes)
    {
        using var input=File.OpenRead(file);using var br=new BrotliStream(input,CompressionMode.Decompress);using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[131072];long total=0;int count;
        while((count=br.Read(buffer,0,buffer.Length))>0){total+=count;if(total>bytes)return false;digest.AppendData(buffer,0,count);}
        return total==bytes&&Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()==hash;
    }
    private static void SnapshotSqlite(LocalStore source,string targetFile)
    {
        IntPtr target=IntPtr.Zero;source.Exec("BEGIN");source.Query("SELECT COUNT(*) FROM events");
        try{
            if(Native.sqlite3_open_v2(targetFile,out target,2|4|0x10000,IntPtr.Zero)!=0)throw new IOException("Günlük snapshot hedefi açılamadı.");
            var backup=Native.sqlite3_backup_init(target,"main",source.db,"main");if(backup==IntPtr.Zero)throw new IOException("Günlük snapshot başlatılamadı.");
            int rc;try{rc=Native.sqlite3_backup_step(backup,-1);}finally{Native.sqlite3_backup_finish(backup);}if(rc!=101)throw new IOException("Günlük snapshot tamamlanamadı: "+rc);
        }finally{if(target!=IntPtr.Zero)Native.sqlite3_close_v2(target);source.Exec("COMMIT");}
    }
    private IDisposable DailyWriterLease(FileStream lease)
    {
        EnableDailyArchiving();
        return lease;
    }
}
