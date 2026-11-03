using System.Buffers.Binary;
using System.Text.Json;

namespace Metin2Search.Local;
internal static class ListingLifecycleTests
{
    public static void Run(string directory,List<string> results)
    {
        void Check(bool c,string label){if(!c)throw new Exception("FAILED: "+label);results.Add("PASS: "+label);}
        using var db=new LocalStore(Path.Combine(directory,"lifecycle"));db.ConfigureServer("Safir, CH1");
        var weapon=new TradeCategory(0,2,0,"Savaşçı / Silah / Tek el");var armor=new TradeCategory(1,3,0,"Ninja / Zırh / Gövde");
        byte[] Raw(uint won,uint candidate=123){var b=new byte[187];b[0x2e]=(byte)'A';BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5),candidate);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13),1);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(17),23006);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x8b),won);return b;}
        string Run(params (TradeCategory category,byte[] raw,uint total,bool success)[] queries){
            var run=Guid.NewGuid().ToString("N");db.TradeAudit(new("RunStart",run,"",null,0,queries.Length,"test"));
            foreach(var q in queries)db.TradeAudit(new("Pending",run,run+":"+q.category.Job+":"+q.category.Category+":"+q.category.Sub,q.category,0,0,""));
            foreach(var q in queries){var id=run+":"+q.category.Job+":"+q.category.Category+":"+q.category.Sub;db.TradeAudit(new("Running",run,id,q.category,0,0,""));
                if(q.success){db.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,q.category,1,q.total==0?0u:1u,q.total,q.raw){RunId=run,QueryId=id});db.TradeAudit(new("Complete",run,id,q.category,1,(int)((q.total+10)/11),""));}
                else db.TradeAudit(new("Failed",run,id,q.category,0,0,"timeout"));
            }
            db.TradeAudit(new("RunEnd",run,"",null,queries.Count(q=>q.success),queries.Length,"done"));return run;
        }
        bool Active(string key)=> (long)db.Query("SELECT is_active FROM listing_lifecycle WHERE key=?",key)[0][0]! == 1;
        void EmptyBoth()=>Run((weapon,[],0,true),(armor,[],0,true));
        db.SaveTrade(new("legacy-unscoped",DateTimeOffset.UtcNow,weapon,1,1,1,Raw(5,555)));
        var legacyKey=db.TradeItems()[0].ListingId!;
        Check(db.TradeItems()[0].IsActive is null&&db.Query("SELECT 1 FROM listing_lifecycle").Count==0,"Eski kapsamı bilinmeyen kayıt otomatik aktif veya kaldırılmış sayılmıyor");
        Run((weapon,Raw(70),1,true),(armor,[],0,true));var first=db.TradeItems().Single(i=>i.Won==70);var key=first.ListingId!;var firstVersion=first.ObservationId;
        Check(first.IsActive==true&&first.PhysicalItemId is null&&first.ObservationId!=first.ListingId,"Gözlem/ilan ayrılıyor; ham aday ve özellik hash'i fiziksel UID olmuyor");
        var originalKey=key;var count=db.TradeObservationCount;
        Run((weapon,Raw(130),1,true),(armor,[],0,true));
        var versions=db.TradeItems().Where(i=>i.ListingId==key).ToArray();
        Check(versions.Length==2&&versions.Single(i=>i.Won==130).IsActive==true&&versions.Single(i=>i.Won==70).IsActive==false,"Aynı grubun eski fiyat sürümü geçmişte; yalnızca güncel sürüm aktif");
        var versionCount=db.TradeObservationCount;
        Run((weapon,Raw(130),1,true),(armor,[],0,true));
        Check(db.TradeObservationCount==versionCount&&db.TradeItems().Any(i=>i.ObservationId==firstVersion)&&key==originalKey,"Değişmeyen tekrar yeni sürüm yaratmıyor; mevcut kimlikler korunuyor");
        EmptyBoth();Check(Active(key),"Tek boş tam tur ilanı kaldırmıyor");
        Run((weapon,[],0,true),(armor,[],0,false));EmptyBoth();Check(Active(key),"Başarısız/timeout kapsamı tüm turun kaldırma kararını engelliyor ve teyidi sıfırlıyor");
        EmptyBoth();Check(!Active(key)&&db.TradeItems().Single(i=>i.Won==130).RemovedAt is not null,"İki ardışık eksiksiz turda görülmeyen ilan satış dışı; geçmiş duruyor");
        Check(db.Query("SELECT 1 FROM listing_lifecycle WHERE key=?",legacyKey).Count==0,"Tam turlar da eski bilinmeyen kapsamları varsayımla emekli etmiyor");
        var removedEvents=db.Query("SELECT 1 FROM lifecycle_events WHERE is_active=0").Count;
        Run((weapon,Raw(130),1,true),(armor,[],0,true));
        Check(Active(key)&&db.TradeItems().Single(i=>i.Won==130).RemovedAt is null&&db.TradeObservationCount==versionCount,"Tekrar görülen ilan sürüm üretmeden yeniden aktifleşiyor; removedAt temizleniyor");
        EmptyBoth();
        Run((weapon,Raw(130),12,true),(armor,[],0,true));
        EmptyBoth();Check(Active(key),"Eksik sayfalı cevap Complete auditi gelse de kaldırma teyidi sayılmıyor");
        var interrupted=Guid.NewGuid().ToString("N");db.TradeAudit(new("RunStart",interrupted,"",null,0,2,"test"));db.StopTradeRun(interrupted,"user stop");
        EmptyBoth();Check(Active(key),"Durdurulmuş tur ardışık yokluk teyidini bozuyor");
        Run((weapon,Raw(130),1,true),(armor,Raw(130),1,true));
        Run((weapon,[],0,true));Run((weapon,[],0,true));
        Check(Active(key),"Başka kapsamda görülen ilan yalnızca bir kapsamdan kaybolunca kaldırılmıyor");
        var beforeEvents=db.Query("SELECT 1 FROM lifecycle_events").Count;
        var activeFile=Path.Combine(directory,"active-market.json");db.ExportActiveTrade(activeFile);
        using(var json=JsonDocument.Parse(File.ReadAllText(activeFile))){var root=json.RootElement;
            Check(root.GetProperty("Items").GetArrayLength()==1&&root.GetProperty("Items")[0].GetProperty("Won").GetUInt32()==130&&root.GetProperty("Items")[0].GetProperty("PhysicalItemId").ValueKind==JsonValueKind.Null,"Aktif JSON yalnızca güncel doğrulanmış aktif sürümü aktarır; legacy geçmişi katmaz");
            Check(root.GetProperty("ScopeSnapshots").GetArrayLength()>0&&root.GetProperty("LifecycleEvents").GetArrayLength()==beforeEvents&&root.GetProperty("Runs").EnumerateArray().Any(r=>r.GetProperty("status").GetString()=="Incomplete"),"Aktif JSON tamamlanma/kapsam/tombstone kanıtını taşır");
        }
        var history=Path.Combine(directory,"identity-history.json");db.ExportTrade(history);
        using(var json=JsonDocument.Parse(File.ReadAllText(history)))Check(json.RootElement.GetArrayLength()==db.TradeObservationCount&&json.RootElement.EnumerateArray().All(i=>i.TryGetProperty("ObservationId",out _)&&i.TryGetProperty("ListingId",out _)&&i.GetProperty("PhysicalItemId").ValueKind==JsonValueKind.Null),"Geçmiş JSON bütün sürümleri kimlik ve aktiflik metadatasıyla korur");
        Check(db.Query("SELECT physical_item_id FROM compact_identity WHERE physical_item_id IS NOT NULL").Count==0&&db.Query("SELECT raw05 FROM compact_identity WHERE raw05=123").Count>=2&&db.Integrity=="ok","SQLite ham kimlik adayını kanıtlanmamış olarak tutar; şema ve geçmiş sağlam");
        Check(removedEvents==1&&db.Query("SELECT 1 FROM lifecycle_events WHERE is_active=1").Count==2,"Aktiflik olayları sürüm sayısından bağımsız; removal/reactivation görünür");
        try{db.ConfigureServer("Başka Sunucu, CH1");throw new Exception("Server accepted");}catch(InvalidDataException){}
        Check(db.ServerName=="Safir"&&Active(key),"Farklı sunucu aynı aktiflik kayıtlarını uzlaştıramaz");
        Check(db.TradeItems(activeOnly:true).Count==1&&db.TradeItems(activeOnly:true).All(i=>i.IsActive==true),"Aktif GUI sorgusu geçmiş fiyatları ve doğrulanmamış legacy kayıtları dışlar");
        var currentRevision=(string)db.Query("SELECT value FROM trade_metadata WHERE key='lifecycle_revision'")[0][0]!;
        Run((weapon,Raw(130),1,true),(armor,Raw(130),1,true));
        Check(long.Parse((string)db.Query("SELECT value FROM trade_metadata WHERE key='lifecycle_revision'")[0][0]!)>long.Parse(currentRevision)&&db.TradeObservationCount==versionCount,"Seen-only tarama lifecycle revision artırır; importer sürüm artmadan güncelleyebilir");

        using(var multi=new LocalStore(Path.Combine(directory,"lifecycle-multipage"))){
            multi.ConfigureServer("Safir, CH1");
            string Multi(uint total2,bool duplicate=false){
                var run=Guid.NewGuid().ToString("N");var q=run+":0:2:0";
                multi.TradeAudit(new("RunStart",run,"",null,0,1,"fixture"));multi.TradeAudit(new("Pending",run,q,weapon,0,0,""));multi.TradeAudit(new("Running",run,q,weapon,0,0,""));
                multi.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,weapon,1,1,12,Enumerable.Range(0,11).SelectMany(i=>Raw(130,(uint)i+1)).ToArray()){RunId=run,QueryId=q});
                multi.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,weapon,2,duplicate?1u:2u,total2,Raw(130,12)){RunId=run,QueryId=q});
                multi.TradeAudit(new("Complete",run,q,weapon,2,2,"done"));multi.TradeAudit(new("RunEnd",run,"",null,1,1,"done"));return run;
            }
            var good=Multi(12);var changed=Multi(13);var dup=Multi(12,true);
            Check((string)multi.Query("SELECT status FROM lifecycle_runs WHERE run_id=?",good)[0][0]! == "Complete"&&new[]{changed,dup}.All(r=>(string)multi.Query("SELECT status FROM lifecycle_runs WHERE run_id=?",r)[0][0]! == "Incomplete"),"11+1 sayfa eksiksiz kabul edilir; değişen total ve yinelenen sayfa kapsam tamamlamaz");
            Check(multi.Query("SELECT 1 FROM scope_pages").Count==0&&multi.Query("SELECT 1 FROM scope_seen").Count==0,"Tamamlanan/hatalı tur staging kayıtları büyümez; kapsam ve ham geçmiş kalır");
        }
        var restartPath=Path.Combine(directory,"lifecycle-restart");string? identity;
        using(var firstDb=new LocalStore(restartPath)){
            firstDb.ConfigureServer("Safir, CH1");firstDb.SaveTrade(new("saved",DateTimeOffset.UtcNow,weapon,1,1,1,Raw(1)));identity=firstDb.TradeItems()[0].ObservationId;
            firstDb.TradeAudit(new("RunStart","interrupted","",null,0,1,"fixture"));
        }
        using(var reopened=new LocalStore(restartPath)){
            Check((string)reopened.Query("SELECT status FROM lifecycle_runs")[0][0]! == "Running","Sadece ikinci pencere açılması aktif taramayı kesmez");
            using var writer=reopened.AcquireScannerWriter();
            Check(reopened.TradeItems()[0].ObservationId==identity&&(string)reopened.Query("SELECT status FROM lifecycle_runs")[0][0]! == "Interrupted","Yazıcı sahipliği alınınca yarım tur kurtarılır; gözlem kimliği sabit kalır");
        }
    }
}
