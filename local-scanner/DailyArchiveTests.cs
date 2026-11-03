using System.Buffers.Binary;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class DailyArchiveTests
{
    internal static void Run(string directory,List<string> results)
    {
        void Check(bool condition,string text){if(!condition)throw new Exception("FAILED: "+text);results.Add("PASS: "+text);}
        var before=DateTimeOffset.Parse("2026-10-15T20:59:59Z");var after=before.AddSeconds(2);var now=before;
        Check(LocalStore.ArchiveDay(before)=="2026-10-15"&&LocalStore.ArchiveDay(after)=="2026-10-16","Günlük bölüm İstanbul 00:00 sınırında değişir; host timezone kullanılmaz");
        var root=Path.Combine(directory,"daily-archive");
        var raw=new byte[187];raw[46]=(byte)'A';BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(17),189);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(13),1);
        TradePage Page(uint price){var b=raw.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9),price);return new(Guid.NewGuid().ToString("N"),now,new(0,2,0,"Silah"),1,1,1,b);}
        string dataset,key;long version;
        using(var db=new LocalStore(root,dailyMaintenance:false,archiveClock:()=>now)){
            db.ConfigureServer("Safir, CH1");db.SaveTrade(Page(1));db.EnableDailyArchiving();
            db.SaveTrade(Page(2));var pending=db.Query("SELECT seq,day,kind,source_key,created_utc,metadata_json,blob_hash,blob FROM daily_archive_pending");
            Check(pending.Any(r=>(string)r[2]! =="version")&&pending.All(r=>(string)r[1]! =="2026-10-15"),"Canlı sürüm ve günlük outbox aynı transaction'da eski güne kaydedilir");
            var blocker=Path.Combine(root,"Gunler");File.WriteAllText(blocker,"fixture blocker");db.MaintainDailyArchive(true);
            Check(db.Query("SELECT 1 FROM daily_archive_pending").Count==pending.Count,"Journal yazımı başarısızsa canlı kayıt ve bekleyen günlük olayları kaybolmaz");File.Delete(blocker);
            db.MaintainDailyArchive(true);Check(db.Query("SELECT 1 FROM daily_archive_pending").Count==0,"Journal commit'inden sonra kalıcı outbox temizlenir");
            foreach(var r in pending)db.Execute("INSERT INTO daily_archive_pending(seq,day,kind,source_key,created_utc,metadata_json,blob_hash,blob) VALUES(?,?,?,?,?,?,?,?)",r);
            db.MaintainDailyArchive(true);
            db.Execute("ATTACH DATABASE ? AS dailycheck",Path.Combine(root,"Gunler","2026-10-15","daily.sqlite"));
            Check((long)db.Query("SELECT COUNT(*) FROM dailycheck.events")[0][0]! ==pending.Count,"İki commit arasında çökme tekrarı günlük olaylarını çoğaltmaz");
            Check((long)db.Query("SELECT COUNT(*) FROM dailycheck.events WHERE kind='version'")[0][0]! ==1,"Önceden var olan tam geçmiş yeni güne topluca kopyalanmaz");
            db.Execute("DETACH DATABASE dailycheck");
            db.Execute("CREATE TRIGGER daily_fail BEFORE INSERT ON daily_archive_pending BEGIN SELECT RAISE(ABORT,'fixture-failure'); END");
            var oldCount=db.TradeObservationCount;bool failed=false;try{db.SaveTrade(Page(3));}catch(IOException){failed=true;}db.Execute("DROP TRIGGER daily_fail");
            Check(failed&&db.TradeObservationCount==oldCount&&db.Query("SELECT 1 FROM daily_archive_pending").Count==0,"Outbox hatası item transaction'ını tamamen geri alır");
            int clockCalls=0;db.ArchiveClock=()=>clockCalls++==0?before:after;db.SaveTrade(Page(3));db.ArchiveClock=()=>now;
            Check(db.Query("SELECT DISTINCT day FROM daily_archive_pending").All(r=>(string)r[0]! =="2026-10-15"),"Transaction içinde gece yarısı geçse de sayfa iki güne bölünmez");
            dataset=(string)db.Query("SELECT value FROM trade_metadata WHERE key='dataset_id'")[0][0]!;var last=db.Query("SELECT id,key FROM compact_versions ORDER BY id DESC LIMIT 1")[0];version=(long)last[0]!;key=(string)last[1]!;
        }
        now=after;
        using(var db=new LocalStore(root,dailyMaintenance:false,archiveClock:()=>now)){
            var backup=Path.Combine(root,"Gunler","2026-10-15","backup");File.WriteAllText(backup,"fixture blocker");db.MaintainDailyArchive(true);
            Check(File.Exists(Path.Combine(root,"Gunler","2026-10-15","daily.sqlite"))&&!File.Exists(Path.Combine(backup,"manifest.json")),"Yedekleme hatasında ham günlük korunur ve tamamlanmış manifest yayınlanmaz");File.Delete(backup);
            using var reached=new ManualResetEventSlim();using var proceed=new ManualResetEventSlim();
            db.ArchiveSnapshotReady=()=>{reached.Set();if(!proceed.Wait(TimeSpan.FromSeconds(10)))throw new IOException("fixture compression wait timeout");};
            var maintenance=Task.Run(()=>db.MaintainDailyArchive(true));
            Check(reached.Wait(TimeSpan.FromSeconds(10)),"SQLite WAL dahil snapshot sıkıştırma aşamasına ulaşır");
            try{
                var write=Task.Run(()=>db.SaveTrade(Page(4)));
                Check(write.Wait(TimeSpan.FromSeconds(3)),"Arşiv sıkıştırması canlı tarama yazma kilidini tutmaz");
            }finally{proceed.Set();maintenance.GetAwaiter().GetResult();db.ArchiveSnapshotReady=null;}
            db.MaintainDailyArchive(true);
            var manifest=Path.Combine(backup,"manifest.json");using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var m=doc.RootElement;
            Check(m.GetProperty("complete").GetBoolean()&&m.GetProperty("datasetId").GetString()==dataset&&m.GetProperty("events").GetInt64()>0&&File.Exists(Path.Combine(backup,m.GetProperty("archiveFile").GetString()!)),"Kapalı geçen gece sonraki açılışta Brotli/hash manifest'i ile telafi edilir");
            var bytes=File.ReadAllBytes(manifest);db.MaintainDailyArchive(true);Check(bytes.SequenceEqual(File.ReadAllBytes(manifest)),"Tamamlanan değişmemiş yedek tekrar sıkıştırılmaz");
            Check((string)db.Query("SELECT value FROM trade_metadata WHERE key='dataset_id'")[0][0]! ==dataset&&db.Query("SELECT 1 FROM compact_versions WHERE id=? AND key=?",version,key).Count==1&&db.TradeObservationCount==version+1,"Gün değişiminde dataset, ilan anahtarı ve global sürüm kimlikleri korunur");
            Check(Directory.Exists(Path.Combine(root,"Gunler","2026-10-16"))&&File.Exists(Path.Combine(root,"Gunler","2026-10-15","daily.sqlite")),"Yeni gün klasörü oluşturulur; eski gün veya kaynak geçmişi silinmez");
            File.WriteAllText(Path.Combine(directory,"daily-archive-manifest-path.txt"),manifest);
        }
        var idleRoot=Path.Combine(directory,"daily-idle");now=before;
        using(var idle=new LocalStore(idleRoot,dailyMaintenance:false,archiveClock:()=>now)){
            idle.ConfigureServer("Safir, CH1");idle.EnableDailyArchiving();idle.SaveTrade(Page(1));idle.MaintainDailyArchive(true);
            now=DateTimeOffset.Parse("2026-10-15T20:59:59.950Z");var clock=System.Diagnostics.Stopwatch.StartNew();var start=now;idle.ArchiveClock=()=>start+clock.Elapsed;idle.DailyBackgroundEnabled=true;idle.EnableDailyArchiving();
            Check(SpinWait.SpinUntil(()=>File.Exists(Path.Combine(idleRoot,"Gunler","2026-10-15","backup","manifest.json")),TimeSpan.FromSeconds(5)),"Tarama ve yeni sayfa olmadan açık program gece yarısında günü yedekler");
        }
    }
}
