using System.Text.Json;

namespace Metin2Search.Local;

public sealed record ShopOpenProof(string ServerInfo,uint SellerVid,string Owner,long OpenGeneration);
public static class VerifiedShopLayout
{
    public static ShopOpenProof Proof(JsonElement data)
    {
        if(!data.TryGetProperty("status",out var status)||status.GetString()!="Open"||!data.TryGetProperty("openGeneration",out var generation)||generation.GetInt64()<1||!data.TryGetProperty("serverInfo",out _))
            throw new InvalidDataException("Doğrulanmış pazar açılışı/nesli yok.");
        var result=new ShopOpenProof(data.GetProperty("serverInfo").GetString()!,data.GetProperty("vid").GetUInt32(),data.GetProperty("seller").GetString()!,data.GetProperty("openGeneration").GetInt64());
        if(result.SellerVid==0||string.IsNullOrWhiteSpace(result.Owner)||string.IsNullOrWhiteSpace(result.ServerInfo))throw new InvalidDataException("Eksik pazar kimliği.");
        return result;
    }
    public static MarketCapture Validate(MarketCapture capture,ShopOpenProof before,ShopOpenProof after)
    {
        capture.Validate();
        if(before!=after||capture.Source!="Live"||capture.ItemLayout!="shop-v2"||!capture.IsComplete||!capture.OwnerVerified||!capture.ChannelVerified||capture.Channel!=1||!capture.PricesVerified||capture.OwnerName!=before.Owner||capture.ShopVid!=before.SellerVid.ToString()||ServerStorage.Name(capture.Server)!=ServerStorage.Name(before.ServerInfo))throw new InvalidDataException("Pazar snapshot kimliği/açılış nesli eşleşmedi.");
        var tabs=capture.Slots.Count/40;
        if(tabs is <1 or >3||capture.Slots.Count!=tabs*40||capture.Slots.Any(s=>s.Tab>=tabs))throw new InvalidDataException("Pazarın bütün sekme/yuvaları okunmadı.");
        return capture;
    }
    public static async Task<MarketCapture> Capture(ITradeBridge bridge,JsonElement opened,Func<ShopOpenProof,CancellationToken,Task<MarketCapture>> read,CancellationToken token)
    {
        var before=Proof(opened);var first=Proof(await bridge.Command("marketpoll",token));
        if(first!=before)throw new InvalidDataException("Okuma öncesi pazar değişti.");
        var capture=await read(before,token);var after=Proof(await bridge.Command("marketpoll",token));
        return Validate(capture,before,after);
    }
}

public sealed partial class LocalStore
{
    private void InitializeVerifiedShopLayouts()=>Exec("""
        CREATE TABLE IF NOT EXISTS verified_shop_layouts(owner TEXT PRIMARY KEY,snapshot_id TEXT NOT NULL UNIQUE,revision INTEGER NOT NULL UNIQUE,observed TEXT NOT NULL,seller_vid INTEGER NOT NULL,source_session TEXT NOT NULL,open_generation INTEGER NOT NULL,body_hash TEXT NOT NULL,body BLOB NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_shop_layout_revision ON verified_shop_layouts(revision);
        INSERT OR IGNORE INTO trade_metadata VALUES('shop_layout_revision','0');
        """);
    public void SaveVerifiedShopLayout(MarketCapture capture,ShopOpenProof before,ShopOpenProof after)
    {
        VerifiedShopLayout.Validate(capture,before,after);
        if(ServerName is null||ServerName!=ServerStorage.Name(before.ServerInfo))throw new InvalidDataException("Pazar snapshot sunucusu kaynakla eşleşmedi.");
        string id;long revision;
        lock(this){
            BeginDailyTransaction();Exec("BEGIN IMMEDIATE");
            try{
                revision=checked(long.Parse((string)Query("SELECT value FROM trade_metadata WHERE key='shop_layout_revision'")[0][0]!)+1);id=capture.Id;
                var items=capture.Slots.Select(slot=>(Slot:slot,Item:ItemDecoder.Decode(capture,slot))).Where(x=>x.Item.Vnum!=0).Select(x=>new{snapshotItemId=id+":"+x.Slot.Tab+":"+x.Slot.Slot,page=x.Slot.Tab,cell=x.Slot.Slot,vnum=x.Item.Vnum,count=x.Item.Count,won=x.Item.RawPriceB,yang=x.Item.RawPriceA,raw=Convert.ToBase64String(x.Slot.Raw)}).ToArray();
                var body=Zip(JsonData.Encode(new{schemaVersion=1,datasetId=DatasetId,server=ServerName,channel=1,snapshotId=id,revision,observed=capture.ObservedAt,owner=before.Owner,sellerVid=before.SellerVid,sourceSession=SessionId,openGeneration=before.OpenGeneration,source="open-shop-128/stable-double-read+StartShop-generation",complete=true,slotIndicesVerified=true,layout=new{verified=false,columns=(int?)null,rows=(int?)null,pageCount=capture.Slots.Count/40,cellsPerPage=40,reason="Grid geometry not proven; do not infer it from search slots or item size"},entries=items,listingMatches=Array.Empty<object>()}));
                var hash=ItemDecoder.Hash(body).ToLowerInvariant();
                Execute("INSERT INTO verified_shop_layouts VALUES(?,?,?,?,?,?,?,?,?) ON CONFLICT(owner) DO UPDATE SET snapshot_id=excluded.snapshot_id,revision=excluded.revision,observed=excluded.observed,seller_vid=excluded.seller_vid,source_session=excluded.source_session,open_generation=excluded.open_generation,body_hash=excluded.body_hash,body=excluded.body",before.Owner,id,revision,capture.ObservedAt.ToString("O"),before.SellerVid,SessionId,before.OpenGeneration,hash,body);
                Execute("UPDATE trade_metadata SET value=? WHERE key='shop_layout_revision'",revision.ToString());
                RecordDaily("shop-layout",id,new{snapshotId=id,revision,owner=before.Owner,sellerVid=before.SellerVid,bodyHash=hash,encoding="br"},body);
                Exec("COMMIT");
            }catch{try{Exec("ROLLBACK");}catch{}throw;}
        }
        QueueDailyMaintenance();
        var folder=Path.Combine(Path.GetDirectoryName(FilePath)!,"shop-layout-sync");var temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
        try{Directory.CreateDirectory(folder);File.WriteAllText(temp,JsonData.Encode(new{schemaVersion=1,datasetId=DatasetId,revision,snapshotId=id}));File.Move(temp,Path.Combine(folder,"ready.json"),true);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        finally{try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}}
    }
}
