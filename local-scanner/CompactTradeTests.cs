using System.Buffers.Binary;

namespace Metin2Search.Local;
internal static class CompactTradeTests
{
    public static void Run(string directory,List<string> results)
    {
        void Check(bool condition,string text){if(!condition)throw new Exception("FAILED: "+text);results.Add("PASS: "+text);}
        var raw=new byte[187];BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x11),189);BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(13),1);raw[0x2e]=(byte)'A';
        using var store=new LocalStore(Path.Combine(directory,"compact"));
        TradePage Page(byte[] bytes)=>new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,new(0,2,0,"Silah"),1,1,1,bytes);
        store.SaveTrade(Page(raw));store.SaveTrade(Page(raw));
        Check(store.TradeObservationCount==1&&store.CompactSeenCount==2,"Tekrar turunda sürüm sabit; kontrol sayısı artıyor");
        var yang=raw.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(yang.AsSpan(9),100);
        store.SaveTrade(Page(yang));var won=yang.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(won.AsSpan(0x8b),2);store.SaveTrade(Page(won));
        Check(store.TradeObservationCount==3&&store.CompactListingCount==1,"Yang ve Won değişimi aynı ilan grubunda yeni sürüm olarak saklanıyor");
        var vid=won.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(vid.AsSpan(1),555);store.SaveTrade(Page(vid));
        Check(store.TradeObservationCount==3,"Geçici VID değişimi tekrar ürün sürümü oluşturmuyor");
        var owner=won.ToArray();owner[0x2e]=(byte)'B';store.SaveTrade(Page(owner));
        Check(store.TradeObservationCount==4&&store.CompactListingCount==2,"Yeni sahipli ilan mutlaka kaydediliyor; belirsiz sahiplik ilişkisi uydurulmuyor");
        var doubleRow=raw.Concat(raw).ToArray();store.SaveTrade(Page(doubleRow));
        Check(store.CompactListingCount==3,"Aynı özellikte birden fazla ilan ayrı sayıda korunuyor");
        var page=Page(owner);store.SaveTrade(page);var count=store.CompactSeenCount;store.SaveTrade(page);
        Check(store.CompactSeenCount==count,"Aynı sayfa kimliği tekrar işlendiğinde transaction idempotent");
        Check(store.TradeItems().All(i=>i.Raw.Length==187)&&store.Integrity=="ok","Kayıpsız sıkıştırma ham eşya baytlarını ve bütünlüğü koruyor");
        store.ConfigureServer("Marmara, CH1");
        try{store.ConfigureServer("Dandanakan, CH1");throw new Exception("Sunucu karışması kabul edildi");}catch(InvalidDataException){}
        Check(store.ServerName=="Marmara","Başka sunucu aynı veritabanına yazamıyor");
        store.ApplyMarketCheck(new(555,"A","Unknown","timeout",DateTimeOffset.UtcNow));
        Check((long)store.Query("SELECT is_fake FROM market_states WHERE vid=555")[0][0]! == 0,"Zaman aşımı yeni FAKE pini oluşturmuyor");
        store.Execute("UPDATE market_states SET is_fake=1,status='Unavailable' WHERE vid=555");
        store.ApplyMarketCheck(new(555,"A","Unknown","timeout",DateTimeOffset.UtcNow));
        Check((long)store.Query("SELECT is_fake FROM market_states WHERE vid=555")[0][0]! == 1,"Belirsiz tekrar kontrolü önceki FAKE pinini kaldırmıyor");
        store.ApplyMarketCheck(new(555,"A","Open","Matching StartShop",DateTimeOffset.UtcNow));
        Check((long)store.Query("SELECT is_fake FROM market_states WHERE vid=555")[0][0]! == 1,"Pazarın açılması satış engeli pinini kaldırmıyor");
        var diagnostic=System.Text.Json.JsonSerializer.SerializeToElement(new{sequence=1,source="GameWindow.AppendChat",preparingCandidate=true,context=new{serverInfo="Marmara, CH1",slot=0,raw=Convert.ToHexString(raw)},arguments=new[]{"Premium pazar hazırlanıyor"}});
        Check(store.SaveSaleDiagnostic("watch",diagnostic)&&!store.SaveSaleDiagnostic("watch",diagnostic)&&store.Query("SELECT 1 FROM sale_diagnostics").Count==1,"Satış tanısı sunucusuyla kalıcı saklanıyor; tekrar polling çift kayıt yaratmıyor");
        Check((long)store.Query("SELECT is_fake FROM market_states WHERE vid=555")[0][0]! == 1,"Zaman eşleşmeli hazırlık mesajı kesin FAKE durumu gibi uygulanmıyor");
        Check(ServerStorage.DirectoryFor(directory,"Marmara, CH1")==ServerStorage.DirectoryFor(directory,"Marmara, CH2")&&ServerStorage.DirectoryFor(directory,"Marmara, CH1")!=ServerStorage.DirectoryFor(directory,"Dandanakan, CH1"),"Sunucu adları ayrı klasörlerde; kanal etiketi sunucu kimliğini değiştirmiyor");
        Check(Path.GetFullPath(ServerStorage.DirectoryFor(directory,"../../tehlike, CH1")).StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Sunucu adı kayıt kökünün dışına çıkamıyor");
        var migrationDir=Path.Combine(directory,"legacy-compact");
        using(var legacy=new LocalStore(migrationDir)){
            legacy.Execute("INSERT INTO raw_blobs VALUES(?,?)",ItemDecoder.Hash(raw),raw);
            legacy.Execute("INSERT INTO trade_pages VALUES(?,?,?,?,?,?,?,?)","legacy",legacy.SessionId,DateTimeOffset.UtcNow.ToString("O"),"{}",1,1,1,ItemDecoder.Hash(raw));
            var original=JsonData.Encode(TradeDecoder.Decode(raw,0));original=original.TrimEnd()[..^1]+",\"unrecognizedFutureField\":\"must survive\"}";
            legacy.Execute("INSERT INTO trade_items VALUES(?,?,?,?,?,?,?,?)","legacy",0,"A","",189,0,0,original);
            File.WriteAllText(Path.Combine(migrationDir,"original.json"),original);
        }
        using(var migrated=new LocalStore(migrationDir)){
            Check(migrated.TradeObservationCount==1&&migrated.TradeItems()[0].Raw.SequenceEqual(raw)&&migrated.Query("SELECT 1 FROM trade_items").Count==0&&Directory.GetFiles(migrationDir,"*.pre-v05-*.sqlite").Length==1,"Eski kayıt dönüşümünden önce yedek alınıyor; ham veri kayıpsız taşınıyor");
            var original=File.ReadAllText(Path.Combine(migrationDir,"original.json"));
            Check((string)migrated.Query("SELECT payload_hash FROM compact_versions")[0][0]! == ItemDecoder.Hash(System.Text.Encoding.UTF8.GetBytes(original)),"Bilinmeyen eski JSON alanları dahil orijinal içerik birebir korunuyor");
            var output=Path.Combine(migrationDir,"export.json");migrated.ExportTrade(output);
            Check(File.ReadAllText(output).Contains("unrecognizedFutureField"),"Bilinmeyen eski alanlar JSON dışa aktarımında da korunuyor");
        }
    }
}
