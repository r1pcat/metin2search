using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace Metin2Search.Local;

internal static class CategoryDeliveryTests
{
    private static readonly TradeCategory Shared=new(0,3,2,"Ortak / Kalkan") {CoveredCategories=[new(0,3,2,"Savaşçı / Kalkan"),new(1,3,2,"Ninja / Kalkan")]};
    private static readonly TradeCategory Other=new(2,2,0,"Sura / Kılıç");
    private static byte[] Raw(uint won=1,uint marker=1){var b=new byte[187];b[46]=(byte)'A';BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5),marker);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9),100);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13),1);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(17),19);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x8b),won);return b;}
    private static JsonElement Body(CategoryBatch batch){
        if(ItemDecoder.Hash(batch.Body).ToLowerInvariant()!=batch.BodyHash)throw new Exception("Outbox hash mismatch");
        using var input=new MemoryStream(batch.Body);using var br=new BrotliStream(input,CompressionMode.Decompress);using var json=JsonDocument.Parse(br);return json.RootElement.Clone();
    }
    private sealed class Harness(string directory,bool singleSweep=false):IDisposable
    {
        internal readonly LocalStore Db=Create(directory,singleSweep);
        private static LocalStore Create(string dir,bool singleSweep){var db=new LocalStore(dir){SingleSweepRemoval=singleSweep};db.ConfigureServer("Safir, CH1");return db;}
        internal string Run="";
        private static string Q(string run,TradeCategory c)=>$"{run}:{c.Job}:{c.Category}:{c.Sub}";
        internal void Audit(string kind,TradeCategory c,int pages=0,int expected=0){foreach(var alias in TradePlan.Covered(c))Db.TradeAudit(new(kind,Run,Q(Run,alias),alias,pages,expected,kind){PlanQueryId=Q(Run,c),PlanCategory=c,PlanLabel=c.Label});}
        internal void Begin(){Run=Guid.NewGuid().ToString("N");Db.TradeAudit(new("RunStart",Run,"",null,0,3,"fixture"){PlannedQueries=2});Audit("Pending",Shared);Audit("Pending",Other);}
        internal void Finish(TradeCategory c,byte[] raw,bool invalid=false){Audit("Running",c);var total=invalid?12u:(uint)(raw.Length/187);Db.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,c,1,total==0?0u:1u,total,raw){RunId=Run,QueryId=Q(Run,c)});Audit("Complete",c,1,(int)((total+10)/11));}
        internal void End()=>Db.TradeAudit(new("RunEnd",Run,"",null,0,3,"fixture"));
        internal CategoryBatch Last(string scope="ch1:0:3:2")=>Db.PendingCategoryBatches(1000).Last(b=>b.CategoryKey==scope);
        public void Dispose()=>Db.Dispose();
    }
    internal static void Run(string directory,List<string> results)
    {
        void Check(bool value,string name){if(!value)throw new Exception("FAILED: "+name);results.Add("PASS: "+name);}
        using(var h=new Harness(Path.Combine(directory,"category-delivery"))){
            h.Begin();h.Finish(Shared,Raw());var first=h.Last();var body=Body(first);
            Check(h.Db.PendingCategoryBatches().Count==1&&body.GetProperty("complete").GetBoolean()&&body.GetProperty("upserts").GetArrayLength()==1&&body.GetProperty("coveredScopes").GetArrayLength()==2,"Kategori tamamlanınca diğer kategori beklerken tek Brotli delta kalıcı kuyruğa girer");
            Check(JsonSerializer.SerializeToElement(h.Db.Metrics.Snapshot()).GetProperty("stages").TryGetProperty("store.category.prepare",out _),"Kategori paket hazırlama maliyeti ayrı performans aşaması olarak ölçülür");
            using(var ready=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(h.Db.FilePath)!,"category-sync","ready.json"))))Check(ready.RootElement.GetProperty("batchId").GetString()==first.BatchId,"Kategori completion commit'inden sonra ready.json dosyası consumer'ı timer beklemeden uyandırır");
            var upsert=body.GetProperty("upserts")[0];var payload=body.GetProperty("payloads").GetProperty(upsert.GetProperty("payloadHash").GetString()!).GetString()!;
            Check(ItemDecoder.Hash(System.Text.Encoding.UTF8.GetBytes(payload)).ToLowerInvariant()==upsert.GetProperty("payloadHash").GetString()&&upsert.GetProperty("category").GetProperty("Category").GetInt32()==3&&upsert.TryGetProperty("observed",out _),"Paket orijinal payload hash'ini, observed ve decoder kategori metadatasını korur");
            h.Audit("Complete",Shared,1,1);Check(h.Db.PendingCategoryBatches().Count==1,"Aynı scanId Complete tekrarı yeni revision veya paket üretmez");
            h.Audit("Failed",Other);h.End();Check(h.Db.PendingCategoryBatches().Count==1,"Turun başka kategorisi hatalı olsa da tamamlanan kategori paketi korunur");
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();var same=Body(h.Last());
            Check(same.GetProperty("upserts").GetArrayLength()==0&&same.GetProperty("payloads").EnumerateObject().Count()==0&&same.GetProperty("revision").GetInt64()==2,"Aynı item tekrarında yalnız küçük completion zarfı; tam payload/geçmiş gönderilmez");
            h.Begin();h.Finish(Shared,Raw(2));h.Audit("Failed",Other);h.End();
            Check(Body(h.Last()).GetProperty("upserts").GetArrayLength()==1,"Fiyat değişikliği sonraki kategori revision'ında bir upsert üretir");
            h.Begin();h.Finish(Shared,Raw(2).Concat(Raw(3,2)).ToArray());h.Audit("Failed",Other);h.End();
            Check(Body(h.Last()).GetProperty("upserts").GetArrayLength()==1,"Yeni item eklenince aynı kalan komşu item yeniden gönderilmez");
            var count=h.Db.PendingCategoryBatches().Count;
            h.Begin();h.Finish(Shared,Raw(2),true);h.Audit("Failed",Other);h.End();
            Check(h.Db.PendingCategoryBatches().Count==count,"Eksik sayfa ve hatalı kategori completion paketi veya tombstone üretmez");
            h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();
            Check(Body(h.Last()).GetProperty("tombstones").GetArrayLength()==0,"İlk başarılı aynı-kategori yokluğu kaldırılma oluşturmaz");
            h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();var gone=Body(h.Last());
            Check(gone.GetProperty("tombstones").GetArrayLength()==2&&gone.GetProperty("membershipRemovals").GetArrayLength()==2,"İki başarılı kategori yokluğu tüm turu beklemeden iki kaldırılma üretir; RunEnd çift saymaz");
            Check(h.Db.Query("SELECT 1 FROM scope_seen").Count==0&&h.Db.Query("SELECT 1 FROM category_delivery_scopes").Count==0,"Kategori completion staging'i temizlenir; kalıcı kaynak geçmişi ve kuyruk korunur");
        }
        using(var h=new Harness(Path.Combine(directory,"category-single-sweep"),singleSweep:true)){
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();
            h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();var gone=Body(h.Last());
            Check(gone.GetProperty("tombstones").GetArrayLength()==1&&gone.GetProperty("tombstones")[0].GetProperty("reason").GetString()=="AbsentInCompleteScanWithCount"&&h.Db.TradeItems()[0].IsActive==false,"Sayım sunucu toplamıyla tutarlıysa tek tam okumada görülmeyen ilan kaldırılır");
        }
        using(var h=new Harness(Path.Combine(directory,"category-single-sweep-mismatch"),singleSweep:true)){
            h.Begin();h.Finish(Shared,Raw().Concat(Raw(1,2)).ToArray());h.Audit("Failed",Other);h.End();
            h.Begin();h.Audit("Running",Shared);
            h.Db.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,Shared,1,1,2,Raw().Concat(Raw(1,2)).ToArray()){RunId=h.Run,QueryId=h.Run+":0:3:2"});
            h.Db.Execute("DELETE FROM scope_seen WHERE key=(SELECT key FROM scope_seen ORDER BY key LIMIT 1)");
            h.Audit("Complete",Shared,1,1);h.Audit("Failed",Other);h.End();
            Check(h.Db.Query("SELECT COUNT(*) FROM listing_lifecycle WHERE is_active=1")[0][0] is 2L,"Görülen ilan sayısı sunucu toplamına eşit değilse tek okuma kaldırma yapmaz; iki okuma kuralı sürer");
        }
        using(var h=new Harness(Path.Combine(directory,"category-overlap"))){
            h.Begin();h.Finish(Shared,Raw());h.Finish(Other,Raw());h.End();var before=Body(h.Last("ch1:2:2:0")).GetProperty("upserts")[0].GetProperty("lifecycleEventId").GetInt64();
            for(int i=0;i<2;i++){h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();}
            var detach=Body(h.Last());
            Check(detach.GetProperty("membershipRemovals").GetArrayLength()==1&&detach.GetProperty("tombstones").GetArrayLength()==0&&h.Db.TradeItems()[0].IsActive==true,"Başka aktif sınıf/kategori üyeliği global silinmeyi engeller; yalnız kategori üyeliği kaldırılır");
            for(int i=0;i<2;i++){h.Begin();h.Audit("Failed",Shared);h.Finish(Other,[]);h.End();}
            var tomb=Body(h.Last("ch1:2:2:0")).GetProperty("tombstones")[0];
            Check(tomb.GetProperty("lifecycleEventId").GetInt64()>before&&h.Db.TradeItems()[0].IsActive==false,"Son koruyucu kapsam da iki kez yoksa monoton lifecycleEventId ile tek global tombstone çıkar");
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();
            Check(Body(h.Last()).GetProperty("upserts")[0].GetProperty("lifecycleEventId").GetInt64()>tomb.GetProperty("lifecycleEventId").GetInt64(),"Yeniden görülme eski tombstone'dan yeni olay kimliği taşır; geç gelen eski paket aktifliği ezemez");
        }
        var recovery=Path.Combine(directory,"category-recovery");
        using(var h=new Harness(Path.Combine(directory,"category-partial-reactivation"))){
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();
            for(int i=0;i<2;i++){h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();}
            h.Begin();h.Finish(Shared,Raw(),true);h.Audit("Failed",Other);h.End();
            h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();
            Check(h.Db.TradeObservationCount==1&&Body(h.Last()).GetProperty("upserts").GetArrayLength()==1,"Başarısız taramadaki ara pozitif kanıt yayın sayılmaz; aynı sürümün sonraki başarılı yeniden görünmesi kaybolmaz");
        }
        using(var h=new Harness(recovery)){
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();
            h.Begin();h.Finish(Shared,[]);
        }
        using(var h=new Harness(recovery)){
            using var writer=h.Db.AcquireScannerWriter();
            Check(h.Db.PendingCategoryBatches().Count==2&&h.Db.Query("SELECT misses FROM scope_memberships").All(r=>(long)r[0]! ==1),"Çökme kurtarması kuyruğu ve başarılı kategorinin yokluk sayacını korur");
            h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();
            Check(Body(h.Last()).GetProperty("tombstones").GetArrayLength()==1,"Yeniden açılıştan sonraki ikinci başarılı yokluk doğru kaldırılma üretir");
            h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();
            Check(h.Db.Query("SELECT misses FROM scope_memberships").All(r=>(long)r[0]! ==2)&&Body(h.Last()).GetProperty("tombstones").GetArrayLength()==0,"Pasif eski kayıtların yokluk sayaçları her tur tekrar yazılmaz veya tekrar tombstone üretilmez");
        }
        var normalized=Path.Combine(directory,"category-normalized");
        LocalStore.CreateOptimizedCopy(Path.Combine(recovery,"market.sqlite"),normalized);
        using(var h=new Harness(normalized)){
            h.Begin();h.Finish(Shared,Raw());h.Audit("Failed",Other);h.End();
            Check((string)h.Db.Query("SELECT type FROM sqlite_master WHERE name='scope_memberships'")[0][0]! =="view"&&Body(h.Last()).GetProperty("upserts").GetArrayLength()==1,"Normalize canlı şemadaki view/trigger üzerinden kategori yeniden-görülme deltası kayıpsız üretilir");
            for(int i=0;i<2;i++){h.Begin();h.Finish(Shared,[]);h.Audit("Failed",Other);h.End();}
            Check(Body(h.Last()).GetProperty("tombstones").GetArrayLength()==1&&h.Db.Integrity=="ok","Normalize view/trigger şemasında iki kategori yokluğu, kuyruk ve SQLite bütünlüğü korunur");
        }
        var durable=Path.Combine(directory,"category-ack");CategoryBatch packet;string dataset;
        using(var h=new Harness(durable)){
            h.Db.DailyBackgroundEnabled=false;h.Db.EnableDailyArchiving();
            h.Begin();h.Db.Execute("CREATE TRIGGER fail_category_queue BEFORE INSERT ON category_outbox BEGIN SELECT RAISE(ABORT,'fixture outbox failure'); END;");
            try{h.Finish(Shared,Raw());throw new Exception("No outbox rollback");}catch(IOException){}
            Check(h.Db.PendingCategoryBatches().Count==0&&h.Db.Query("SELECT 1 FROM category_delivery_state").Count==0,"Kuyruk yazımı başarısızsa kategori revision ve silinme uzlaşması atomik geri alınır");
            h.Db.Execute("DROP TRIGGER fail_category_queue");h.Audit("Complete",Shared,1,1);h.Audit("Failed",Other);h.End();
            h.Begin();h.Finish(Shared,Raw(2));h.Audit("Failed",Other);h.End();
            packet=h.Last();dataset=(string)h.Db.Query("SELECT value FROM trade_metadata WHERE key='dataset_id'")[0][0]!;
        }
        using(var reopened=new LocalStore(durable,false,dailyMaintenance:false)){
            Check(reopened.PendingCategoryBatches().Last().Body.SequenceEqual(packet.Body),"Yeniden açılış ACK'siz aynı batchId ve değişmeyen bytes ile retry sağlar");
            var ack=new CategoryAck(1,dataset,"Safir",packet.CategoryKey,packet.Revision,packet.BatchId,packet.BodyHash);
            try{reopened.AcknowledgeCategoryRevision(ack with{Server="Başka Sunucu"});throw new Exception("Wrong server ACK accepted");}catch(InvalidDataException){}
            try{reopened.AcknowledgeCategoryRevision(ack with{BodyHash="bad"});throw new Exception("Wrong hash ACK accepted");}catch(InvalidDataException){}
            try{reopened.AcknowledgeCategoryRevision(ack with{DatasetId="other"});throw new Exception("Wrong dataset ACK accepted");}catch(InvalidDataException){}
            Check(reopened.PendingCategoryBatches().Count==2,"Yanlış sunucu, dataset veya hash ACK'si kuyruktan veri düşürmez");
            var file=reopened.CategoryAckFile(packet.CategoryKey);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,JsonData.Encode(ack));reopened.ProcessCategoryAcknowledgements();reopened.AcknowledgeCategoryRevision(ack);
            Check(reopened.PendingCategoryBatches().Count==0&&reopened.Query("SELECT body FROM category_outbox").All(r=>r[0] is null)&&reopened.Query("SELECT 1 FROM category_outbox").Count==2,"Kümülatif dosya ACK ve tekrarı idempotenttir; büyük BLOB'lar temizlenir, küçük receipt'ler kalır");
            reopened.MaintainDailyArchive(true);
            reopened.Execute("ATTACH DATABASE ? AS archivecheck",Path.Combine(durable,"Gunler",LocalStore.ArchiveDay(DateTimeOffset.UtcNow),"daily.sqlite"));
            Check(((byte[])reopened.Query("SELECT bytes FROM archivecheck.blobs WHERE hash=?",packet.BodyHash)[0][0]!).SequenceEqual(packet.Body),"ACK sonrası gönderim kuyruğunun BLOB'u temizlense de kategori paketi günlük arşivde aynen korunur");
            reopened.Execute("DETACH DATABASE archivecheck");
        }
    }
}

