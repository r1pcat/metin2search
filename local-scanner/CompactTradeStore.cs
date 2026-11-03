using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Metin2Search.Local;

public sealed partial class LocalStore
{
    private string? compactRun;
    private readonly Dictionary<string,int> occurrence = [];
    private string? purchasePageId;
    private Dictionary<int,string> purchasePageKeys=[];
    public string? ServerName { get; private set; }
    public int LastTradeChanged { get; private set; }
    public int LastTradeUnchanged { get; private set; }
    public long CompactListingCount => (long)Query("SELECT COUNT(*) FROM compact_listings")[0][0]!;
    public long CompactSeenCount => (long)Query("SELECT COALESCE(SUM(seen),0) FROM compact_listings")[0][0]!;
    public bool HasLegacyTrade=>Query("SELECT 1 FROM trade_items LIMIT 1").Count>0;

    private void InitializeCompactTrade()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS compact_payloads(hash TEXT PRIMARY KEY,bytes BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS compact_listings(key TEXT PRIMARY KEY,version_hash TEXT NOT NULL,first_seen TEXT NOT NULL,last_seen TEXT NOT NULL,seen INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS compact_versions(id INTEGER PRIMARY KEY,key TEXT NOT NULL,observed TEXT NOT NULL,version_hash TEXT NOT NULL,payload_hash TEXT NOT NULL REFERENCES compact_payloads(hash),seller TEXT NOT NULL,name TEXT NOT NULL,vnum INTEGER NOT NULL,search_text TEXT NOT NULL,reason TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS compact_pages(id TEXT PRIMARY KEY,observed TEXT NOT NULL,category TEXT NOT NULL,page INTEGER NOT NULL,changed INTEGER NOT NULL,unchanged INTEGER NOT NULL,error TEXT);
            CREATE TABLE IF NOT EXISTS compact_legacy_details(page_id TEXT PRIMARY KEY,payload_hash TEXT NOT NULL REFERENCES compact_payloads(hash),run_id TEXT,query_id TEXT,error TEXT);
            CREATE TABLE IF NOT EXISTS compact_presence(key TEXT PRIMARY KEY,session TEXT NOT NULL,vid INTEGER NOT NULL,last_seen TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS market_states(session TEXT NOT NULL,vid INTEGER NOT NULL,seller TEXT NOT NULL,status TEXT NOT NULL,verified TEXT,attempted TEXT NOT NULL,reason TEXT NOT NULL,is_fake INTEGER NOT NULL,PRIMARY KEY(session,vid,seller));
            CREATE TABLE IF NOT EXISTS trade_metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sale_diagnostics(id TEXT PRIMARY KEY,observed TEXT NOT NULL,seller TEXT NOT NULL,vnum INTEGER NOT NULL,source TEXT NOT NULL,payload_hash TEXT NOT NULL REFERENCES compact_payloads(hash),preparing_candidate INTEGER NOT NULL);
            """);
        ServerName=Query("SELECT value FROM trade_metadata WHERE key='server'").FirstOrDefault()?[0] as string;
        if((string?)Query("SELECT type FROM sqlite_master WHERE name='compact_versions'").FirstOrDefault()?[0]=="table")
            Exec("CREATE INDEX IF NOT EXISTS ix_compact_key ON compact_versions(key); CREATE INDEX IF NOT EXISTS ix_compact_vnum ON compact_versions(vnum);");
        InitializeListingLifecycle();
        InitializeScanStatus();
    }
    public void ConfigureServer(string info)
    {
        var name=ServerStorage.Name(info);
        if(ServerName is not null&&ServerName!=name)throw new InvalidDataException("Veritabanı başka sunucuya ait; tarama durdu.");
        Execute("INSERT OR IGNORE INTO trade_metadata VALUES('server',?)",name);ServerName=name;
    }

    private static byte[] Zip(string json)
    {
        using var output=new MemoryStream();
        using(var compressor=new BrotliStream(output,CompressionLevel.Optimal,true)) compressor.Write(Encoding.UTF8.GetBytes(json));
        return output.ToArray();
    }
    private static string Unzip(byte[] data)
    {
        using var input=new MemoryStream(data);using var stream=new BrotliStream(input,CompressionMode.Decompress);using var output=new MemoryStream();
        var buffer=new byte[8192];int count;
        while((count=stream.Read(buffer))>0){if(output.Length+count>4*1024*1024)throw new InvalidDataException("Ayrıntı boyutu sınırı aşıldı.");output.Write(buffer,0,count);}
        return Encoding.UTF8.GetString(output.ToArray());
    }
    private static TradeItem DecodeCompact(byte[] data,string? expectedHash=null)
    {
        var json=Unzip(data);
        if(expectedHash is not null&&ItemDecoder.Hash(Encoding.UTF8.GetBytes(json))!=expectedHash)throw new InvalidDataException("Sıkıştırılmış kayıt hash doğrulaması başarısız.");
        return JsonData.Decode<TradeItem>(json);
    }
    private string Payload(string json)
    {
        var hash=ItemDecoder.Hash(Encoding.UTF8.GetBytes(json));
        Execute("INSERT OR IGNORE INTO compact_payloads VALUES(?,?)",hash,Zip(json));return hash;
    }
    private static string Canonical(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)return "{"+string.Join(",",value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+Canonical(p.Value)))+"}";
        if(value.ValueKind==JsonValueKind.Array)return "["+string.Join(",",value.EnumerateArray().Select(Canonical))+"]";
        return value.GetRawText();
    }
    private static string Identity(TradeItem item)
    {
        var raw=item.Raw.ToArray();
        Array.Clear(raw,1,4);Array.Clear(raw,9,8);Array.Clear(raw,0x8b,4);
        Array.Clear(raw,0x2e,25);
        return ItemDecoder.Hash(raw);
    }
    private static readonly System.Text.RegularExpressions.Regex RemainingTime=new(@"Kalan S[üu]re\s*:\s*(?:\d+\s*(?:g|sa\.?|dk\.?|sn\.?)\s*)+",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant|System.Text.RegularExpressions.RegexOptions.Compiled);
    private static string StableText(string text)=>RemainingTime.Replace(System.Text.RegularExpressions.Regex.Replace(text,@"\s+"," "),"Kalan Süre : * ").Trim();
    private static string StableDetail(JsonElement value,string? name=null)
    {
        switch(value.ValueKind)
        {
            case JsonValueKind.Object:
                return "{"+string.Join(",",value.EnumerateObject().Where(p=>p.Name is not ("slot" or "rendererCalls")).OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+StableDetail(p.Value,p.Name)))+"}";
            case JsonValueKind.Array:
                if(name=="lines"&&value.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.String))
                    return JsonSerializer.Serialize(StableText(string.Join(" ",value.EnumerateArray().Select(v=>v.GetString()))));
                return "["+string.Join(",",value.EnumerateArray().Select(v=>StableDetail(v)))+"]";
            case JsonValueKind.String:return JsonSerializer.Serialize(StableText(value.GetString()??""));
            default:return value.GetRawText();
        }
    }
    private static string StableSignature(TradeItem item)
    {
        var raw=item.Raw.ToArray();Array.Clear(raw,1,4);
        return ItemDecoder.Hash(Encoding.UTF8.GetBytes(Convert.ToHexString(raw)+(item.ClientDetails is {ValueKind:JsonValueKind.Object} d?StableDetail(d):"")));
    }
    private static string Version(TradeItem item)
    {
        var raw=item.Raw.ToArray();Array.Clear(raw,1,4);
        var detail=item.ClientDetails is {} d?JsonSerializer.SerializeToElement(d.EnumerateObject().Where(p=>p.Name!="slot").ToDictionary(p=>p.Name,p=>p.Value)):default;
        return ItemDecoder.Hash(Encoding.UTF8.GetBytes(Convert.ToHexString(raw)+(detail.ValueKind==JsonValueKind.Undefined?"":Canonical(detail))));
    }
    private void SaveCompactTrade(TradePage page)
    {
        BeginDailyTransaction();
        page.Validate();LastTradeChanged=0;LastTradeUnchanged=0;
        var run=page.RunId??page.Id;
        if(compactRun!=run){compactRun=run;occurrence.Clear();}
        var nextCounts=new Dictionary<string,int>();
        var pageKeys=new Dictionary<int,string>();
        var appeared=0;var changedSlots=new HashSet<int>();
        Exec("BEGIN IMMEDIATE");
        try
        {
            if(Query("SELECT id FROM compact_pages WHERE id=?",page.Id).Count>0){Exec("ROLLBACK");return;}
            if(page.RestartsQuery)
            {
                var prefix=(page.QueryId??JsonData.Encode(page.Category))+":";
                foreach(var stale in occurrence.Keys.Where(k=>k.StartsWith(prefix,StringComparison.Ordinal)).ToList())occurrence.Remove(stale);
                if(page.RunId is not null)foreach(var covered in TradePlan.Covered(page.Category))Execute("DELETE FROM scope_pages WHERE run_id=? AND scope_id=?",page.RunId,Scope(covered));
            }
            foreach(var observedItem in page.Items)
            {
                var item=observedItem with{Server=ServerName};
                var basis=item.Seller+":"+Identity(item);
                var query=(page.QueryId??JsonData.Encode(page.Category))+":"+basis;
                var n=nextCounts.TryGetValue(query,out var pending)?pending:occurrence.GetValueOrDefault(query);nextCounts[query]=n+1;
                var key=basis+":"+n;var version=Version(item);var time=page.ObservedAt.ToString("O");
                pageKeys.Add(item.Slot,key);
                Execute("INSERT INTO compact_presence VALUES(?,?,?,?) ON CONFLICT(key) DO UPDATE SET session=excluded.session,vid=excluded.vid,last_seen=excluded.last_seen",key,SessionId,item.SellerVid,time);
                var previous=Query("SELECT version_hash FROM compact_listings WHERE key=?",key);
                if(previous.Count>0&&(string)previous[0][0]! == version)
                {
                    Execute("UPDATE compact_listings SET last_seen=?,seen=seen+1 WHERE key=?",time,key);LastTradeUnchanged++;RecordListingSeen(page,item,key,time);continue;
                }
                if(previous.Count>0&&item.TooltipLines.Any(l=>l.Contains("Kalan S",StringComparison.OrdinalIgnoreCase)))
                {
                    var latest=Query("SELECT p.bytes FROM compact_versions v JOIN compact_payloads p ON p.hash=v.payload_hash WHERE v.key=? ORDER BY v.id DESC LIMIT 1",key);
                    if(latest.Count>0&&StableSignature(DecodeCompact((byte[])latest[0][0]!))==StableSignature(item))
                    {
                        Execute("UPDATE compact_listings SET last_seen=?,seen=seen+1 WHERE key=?",time,key);LastTradeUnchanged++;RecordListingSeen(page,item,key,time);continue;
                    }
                }
                if(previous.Count>0 && (item.ClientDetails is null || (item.ClientDetails.Value.TryGetProperty("status",out var detailStatus)&&detailStatus.GetString()!="client-rendered")))
                {
                    var prior=Query("SELECT p.bytes FROM compact_versions v JOIN compact_payloads p ON p.hash=v.payload_hash WHERE v.key=? ORDER BY v.id DESC LIMIT 1",key);
                    if(prior.Count>0){var old=DecodeCompact((byte[])prior[0][0]!);if(old.ClientDetails is not null){
                        var merged=item with{ClientDetails=old.ClientDetails,DetailError=item.DetailError};
                        var mergedVersion=Version(merged);
                        if((string)previous[0][0]! == mergedVersion){Execute("UPDATE compact_listings SET last_seen=?,seen=seen+1 WHERE key=?",time,key);LastTradeUnchanged++;}
                        else{StoreVersion(merged,key,mergedVersion,time,"Updated; previous details retained");LastTradeChanged++;changedSlots.Add(item.Slot);}RecordListingSeen(page,item,key,time);continue;
                    }}
                }
                StoreVersion(item,key,version,time,previous.Count==0?"Appeared":"Changed");LastTradeChanged++;changedSlots.Add(item.Slot);if(previous.Count==0)appeared++;
                var versionId=(long)Query("SELECT MAX(id) FROM compact_versions WHERE key=?",key)[0][0]!;
                Execute("UPDATE version_identity SET page_id=?,slot=? WHERE version_id=?",page.Id,item.Slot,versionId);
                RecordListingSeen(page,item,key,time);
            }
            Execute("INSERT INTO compact_pages VALUES(?,?,?,?,?,?,?)",page.Id,page.ObservedAt.ToString("O"),JsonData.Encode(page.Category),page.Page,LastTradeChanged,LastTradeUnchanged,page.DetailError);
            RecordScopePage(page);
            RecordDaily("page",page.Id,new{pageId=page.Id,observedAt=page.ObservedAt,category=page.Category,page=page.Page,total=page.Total,changed=LastTradeChanged,unchanged=LastTradeUnchanged,error=page.DetailError});
            CaptureDailyLifecycle();
            Exec("COMMIT");foreach(var entry in nextCounts)occurrence[entry.Key]=entry.Value;
            purchasePageId=page.Id;purchasePageKeys=pageKeys;
            if(page.Result is {} result){result.Changed=LastTradeChanged;result.Unchanged=LastTradeUnchanged;result.Appeared=appeared;result.ChangedSlots.UnionWith(changedSlots);}
        }
        catch{try{Exec("ROLLBACK");}catch{}throw;}
    }
    public void ApplyMarketCheck(MarketCheck check)
    {
        if(check.Status is not ("Open" or "Unknown"))throw new InvalidDataException("Kanıtlanmamış pazar durumu reddedildi.");
        Execute("INSERT INTO market_states VALUES(?,?,?,?,?,?,?,0) ON CONFLICT(session,vid,seller) DO UPDATE SET status=CASE WHEN excluded.status='Open' THEN 'Open' ELSE market_states.status END,verified=CASE WHEN excluded.status='Open' THEN excluded.verified ELSE market_states.verified END,attempted=excluded.attempted,reason=excluded.reason",SessionId,check.Vid,check.Seller,check.Status,check.Status=="Open"?check.CheckedAt.ToString("O"):null,check.CheckedAt.ToString("O"),check.Reason);
    }
    private void StoreVersion(TradeItem item,string key,string version,string time,string reason,string? originalJson=null,byte[]? packed=null)
    {
        var json=originalJson??JsonData.Encode(item);
        var payload=ItemDecoder.Hash(Encoding.UTF8.GetBytes(json));
        var payloadBytes=packed??Zip(json);
        Execute("INSERT OR IGNORE INTO compact_payloads VALUES(?,?)",payload,payloadBytes);
        Execute("INSERT INTO compact_versions(key,observed,version_hash,payload_hash,seller,name,vnum,search_text,reason) VALUES(?,?,?,?,?,?,?,?,?)",key,time,version,payload,item.Seller,item.Name,item.Vnum,string.Join("\n",item.TooltipLines)+"\n"+item.BonusSummary+"\n"+item.StonesSummary,reason);
        var id=(long)Query("SELECT MAX(id) FROM compact_versions WHERE key=?",key)[0][0]!;
        Execute("INSERT INTO version_identity VALUES(?,NULL,?,?,?,?)",id,item.Slot,"search-result-187/server",ItemDecoder.U32(item.Raw,5),item.Raw[0]);
        Execute("INSERT INTO compact_listings VALUES(?,?,?,?,1) ON CONFLICT(key) DO UPDATE SET version_hash=excluded.version_hash,last_seen=excluded.last_seen,seen=seen+1",key,version,time,time);
        RecordDaily("version",id.ToString(System.Globalization.CultureInfo.InvariantCulture),new{sourceVersion=id,listingKey=key,observed=time,versionHash=version,payloadHash=payload,seller=item.Seller,name=item.Name,vnum=item.Vnum,reason},payloadBytes);
    }

    public void CompactLegacyTrade()
    {
        lock(this)CompactLegacyTradeUnlocked();
    }
    private void CompactLegacyTradeUnlocked()
    {
        if(Query("SELECT 1 FROM trade_items LIMIT 1").Count==0)return;
        Backup(FilePath+".pre-v05-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite");
        Exec("BEGIN IMMEDIATE");
        try
        {
            long cursor=0;
            while(true)
            {
                var rows=Query("SELECT i.rowid,i.page_id,i.slot,i.decoded_json,p.observed FROM trade_items i JOIN trade_pages p ON p.id=i.page_id WHERE i.rowid>? ORDER BY i.rowid LIMIT 200",cursor);
                if(rows.Count==0)break;
                foreach(var row in rows){var json=(string)row[3]!;var bytes=Zip(json);if(Unzip(bytes)!=json)throw new InvalidDataException("Sıkıştırma doğrulaması başarısız.");
                    var item=JsonData.Decode<TradeItem>(json);StoreVersion(item,"legacy:"+row[1]+":"+row[2],Version(item),(string)row[4]!,"Legacy observation",json,bytes);cursor=(long)row[0]!;}
            }
            foreach(var row in Query("SELECT page_id,run_id,query_id,details_json,error FROM trade_page_details"))
                Execute("INSERT INTO compact_legacy_details VALUES(?,?,?,?,?)",row[0],Payload((string?)row[3]??"null"),row[1],row[2],row[4]);
            Exec("DELETE FROM trade_item_features; DELETE FROM trade_items; DELETE FROM trade_page_details;");
            Exec("COMMIT");Exec("PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
        }
        catch{try{Exec("ROLLBACK");}catch{}throw;}
    }
}
