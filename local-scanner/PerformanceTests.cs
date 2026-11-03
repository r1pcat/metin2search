using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace Metin2Search.Local;
internal static class PerformanceTests
{
    public static void Run(string directory,List<string> results)
    {
        void Check(bool value,string name){if(!value)throw new Exception("FAILED: "+name);results.Add("PASS: "+name);}
        var path=Path.Combine(directory,"performance-source");
        using(var db=new LocalStore(path)){
            db.ConfigureServer("Ölçüm, CH1");
            using(var lease=db.AcquireScannerWriter()){
                db.TradeAudit(new("RunStart","owner-run","",null,0,1,"fixture"));
                using var other=new LocalStore(path,false);
                try{using var denied=other.AcquireScannerWriter();throw new Exception("Second writer accepted");}catch(IOException){}
                Check((string)other.Query("SELECT status FROM lifecycle_runs WHERE run_id='owner-run'")[0][0]! == "Running","İkinci UI/engellenen yazıcı canlı turu kesmez; sunucu yazıcı lease'i tekildir");
            }
            db.StopTradeRun("owner-run","fixture");
            var sessions=(long)db.Query("SELECT COUNT(*) FROM sessions")[0][0]!;
            using(var reader=LocalStore.OpenReadOnly(db.FilePath)){
                try{reader.Execute("INSERT INTO trade_metadata VALUES('must-not-write','x')");throw new Exception("Reader wrote");}catch(IOException){}
                Check((long)reader.Query("SELECT COUNT(*) FROM sessions")[0][0]! == sessions,"Salt okunur görünüm oturum eklemez ve yazma yapamaz");
            }
            Check((string)db.Query("SELECT ?","ilk")[0][0]! == "ilk"&&(string)db.Query("SELECT ?","ikinci")[0][0]! == "ikinci","Hazırlanmış statement yeniden bağlanır; önceki parametre sızmaz");
            var category=new TradeCategory(0,2,0,new string('K',300));
            byte[] Raw(uint id,uint price){var raw=new byte[187];raw[0x2e]=(byte)'S';raw[0x15]=(byte)'X';BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(5),id);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(13),1);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(17),189);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(9),price);return raw;}
            for(int page=0;page<100;page++){
                var bytes=Enumerable.Range(0,11).SelectMany(i=>Raw((uint)((page%20)*11+i+1),(uint)(page/20+1))).ToArray();
                db.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,category,page,1,11,bytes));
            }
            var key=(string)db.Query("SELECT key FROM compact_listings LIMIT 1")[0][0]!;
            var when=DateTimeOffset.UtcNow.ToString("O");
            db.Execute("BEGIN IMMEDIATE");
            for(int scope=0;scope<30;scope++)foreach(var row in db.Query("SELECT key FROM compact_listings"))db.Execute("INSERT INTO scope_memberships VALUES(?,?,?,?,0,1)","ch1:"+scope+":5:0",row[0],when,"fixture-run");
            db.Execute("COMMIT");
            db.Execute("CREATE TRIGGER fail_page BEFORE INSERT ON compact_pages BEGIN SELECT RAISE(ABORT,'fixture rollback'); END");
            var duplicate=Raw(9999,1).Concat(Raw(9999,1)).ToArray();
            var failed=new TradePage("rollback",DateTimeOffset.UtcNow,category,1,1,2,duplicate){RunId="delta",QueryId="delta-query"};
            try{db.SaveTrade(failed);throw new Exception("Rollback missing");}catch(IOException){}
            db.Execute("DROP TRIGGER fail_page");db.SaveTrade(failed);
            Check(db.Query("SELECT key FROM compact_listings WHERE key LIKE '%:0'").Count>0&&db.Query("SELECT key FROM compact_listings WHERE key LIKE '%:1'").Count==1,"Sayfa rollback'i occurrence delta'sını tüketmez; özdeş iki ilan ayrı kalır");
            db.PublishScannerStatus(true);using var status=JsonDocument.Parse((string)db.Query("SELECT metrics_json FROM scanner_status")[0][0]!);
            Check(status.RootElement.GetProperty("stages").TryGetProperty("store.page",out _)&&(long)db.Query("SELECT import_interval_seconds FROM scanner_status")[0][0]! == 60&&db.Query("SELECT last_complete_run FROM scanner_status")[0][0] is null,"Sınırlı performans özeti/60sn sözleşmesi var; eksik tam tur taze gösterilmez");
            Check(db.TradeItems(limit:7).Count==7,"Görünüm pencere limiti geçmişi silmeden uygulanır");
        }
        var target=Path.Combine(directory,"performance-optimized");
        var manifest=LocalStore.CreateOptimizedCopy(Path.Combine(path,"market.sqlite"),target);
        using(var normalized=new LocalStore(target,false)){
            Check((string)normalized.Query("SELECT type FROM sqlite_master WHERE name='compact_versions'")[0][0]! == "view"&&normalized.TradeObservationCount==1102,"Normalize kopya eski compact_versions kolonlarını ve source_version ID'lerini korur");
            var before=normalized.TradeObservationCount;var original=normalized.TradeItems().First(i=>i.Seller=="S");
            normalized.SaveTrade(new("view-write",DateTimeOffset.UtcNow,new(0,2,0,"test"),1,1,1,original.Raw));
            var key=(string)normalized.Query("SELECT key FROM scope_memberships LIMIT 1")[0][0]!;
            normalized.Execute("INSERT OR REPLACE INTO scope_memberships VALUES('repeat',?,?,?,0,1)",key,"now","run");
            normalized.Execute("INSERT OR REPLACE INTO scope_memberships VALUES('repeat',?,?,?,0,1)",key,"later","run");
            Check(normalized.Query("SELECT 1 FROM scope_memberships").Count==6601&&normalized.Query("PRAGMA foreign_key_check").Count==0&&normalized.Integrity=="ok","Uyum view'larına yazma intern ID'lerini bozmaz; üyelikler ve bütünlük korunur");
            Check(File.Exists(Path.Combine(path,"market.sqlite"))&&File.Exists(Path.Combine(target,"optimization-manifest.json")),"Migration yalnız ayrı kopyayı değiştirir; kaynak ve hash eşdeğerlik manifest'i kalır");
        }
        File.WriteAllText(Path.Combine(directory,"storage-benchmark.json"),JsonData.Encode(manifest));
        var cutover=Path.Combine(directory,"cutover-stage");
        LocalStore.OptimizeAndInstall(Path.Combine(path,"market.sqlite"),cutover);
        using(var installed=new LocalStore(path,false))Check(installed.TradeObservationCount==1102&&Directory.GetFiles(path,"*.pre-v060-*.sqlite").Length==1&&File.Exists(Path.Combine(cutover,"cutover.json")),"Kontrollü kesim güncel kaynağı yeniden snapshot alır; eski DB'yi arşivleyip ID/geçmişi korur");
    }
}

