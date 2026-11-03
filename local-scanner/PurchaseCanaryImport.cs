using System.Text.Json;

namespace Metin2Search.Local;

public static class PurchaseCanaryImport
{
    public static void Run(string reportFile,string contextFile,string dataDirectory)
    {
        using var report=JsonDocument.Parse(File.ReadAllText(reportFile));
        using var context=JsonDocument.Parse(File.ReadAllText(contextFile));
        var r=report.RootElement;var c=context.RootElement;
        if(!r.GetProperty("sent").GetBoolean()||r.GetProperty("error").ValueKind!=JsonValueKind.Null||r.GetProperty("clientPid").GetInt32()!=c.GetProperty("clientPid").GetInt32())throw new InvalidDataException("Canary sender/process evidence mismatch.");
        var steps=r.GetProperty("steps");var first=steps.GetProperty("firstPreflight");var last=steps.GetProperty("finalPreflight");
        var result=steps.GetProperty("result");var nonce=r.GetProperty("nonce").GetString()!;
        const string notice="[Kişisel premium dükkan] Dükkan şu anda hazırlanıyor.";
        if(result.GetProperty("code").GetString()!="PREMIUM_PREPARING"||!result.GetProperty("requestAndShopVerified").GetBoolean()||
            result.GetProperty("text").GetString()!=notice||result.GetProperty("sendCount").GetInt32()!=1||result.GetProperty("nativeReturn").GetInt32()!=1||
            result.GetProperty("nonce").GetString()!=nonce||last.GetProperty("nonce").GetString()!=nonce||
            !result.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("source").GetString()=="NativeChat.Info"&&e.GetProperty("text").GetString()==notice))
            throw new InvalidDataException("Verified native system response missing.");
        var raw=last.GetProperty("raw").GetString()!;
        if(raw.Length!=374||first.GetProperty("raw").GetString()!=raw||!last.GetProperty("sendingSupported").GetBoolean()||
           !first.GetProperty("wallet").GetProperty("walletVerified").GetBoolean()||!last.GetProperty("wallet").GetProperty("walletVerified").GetBoolean())throw new InvalidDataException("Canary target/preflight mismatch.");
        var snapshot=c.GetProperty("steps").GetProperty("snapshot");var inspection=c.GetProperty("steps").GetProperty("inspect");
        var slot=last.GetProperty("slot").GetInt32();
        if(slot!=0||!snapshot.GetProperty("raw").GetString()!.StartsWith(raw,StringComparison.Ordinal)||inspection.GetProperty("serverInfo").GetString()!=last.GetProperty("serverInfo").GetString())throw new InvalidDataException("Fresh context does not match canary.");
        var server=PurchaseServerIds.Resolve(last.GetProperty("serverInfo").GetString()!)??throw new InvalidDataException("Unknown server.");
        foreach(var wallet in new[]{first.GetProperty("wallet"),last.GetProperty("wallet"),result.GetProperty("wallet"),c.GetProperty("steps").GetProperty("wallet")}){
            if(!wallet.GetProperty("walletVerified").GetBoolean()||wallet.GetProperty("won").GetInt64()!=0||wallet.GetProperty("yang").GetInt64()!=last.GetProperty("wallet").GetProperty("yang").GetInt64()||wallet.GetProperty("serverInfo").GetString()!=last.GetProperty("serverInfo").GetString())throw new InvalidDataException("Canary wallet/server changed.");
        }
        var checkedAt=DateTimeOffset.Parse(r.GetProperty("at").GetString()!);
        if(checkedAt>DateTimeOffset.UtcNow.AddSeconds(5)||checkedAt<DateTimeOffset.UtcNow.AddMinutes(-15))throw new InvalidDataException("Canary evidence expired.");
        using var store=new LocalStore(dataDirectory);
        store.ConfigureServer(inspection.GetProperty("serverInfo").GetString()!);
        if(store.Query("SELECT 1 FROM purchase_checks WHERE json_extract(payload_json,'$.requestId')=?",nonce).Count>0)return;
        using var lease=store.AcquireScannerWriter();
        var filter=inspection.GetProperty("filters");
        var category=new TradeCategory(filter.GetProperty("job").GetInt32(),filter.GetProperty("category").GetInt32(),filter.GetProperty("sub").GetInt32(),"Canlı satın alma doğrulaması / tek gözlem");
        var page=new TradePage("canary-"+nonce,checkedAt,category,last.GetProperty("generation").GetInt64(),snapshot.GetProperty("page").GetUInt32(),snapshot.GetProperty("total").GetUInt32(),Convert.FromHexString(raw));
        store.SaveTrade(page);var target=store.PurchaseTargets(page).Single();
        if(target.Server!=server||target.Seller!=last.GetProperty("seller").GetString()||target.ShopVid!=last.GetProperty("shopVid").GetUInt32())throw new InvalidDataException("Committed canary anchor mismatch.");
        var record=new PurchaseCheckRecord(1,server,store.SessionId,target.ShopVid,target.Seller,nonce,target.ListingKey,checkedAt.ToString("O"),"Preparing","request-and-shop","shop",notice,"scanner-purchase-canary/native-chat-info-v1; serialized client context, no server request ID",true,false);
        store.SavePurchaseCheck(record);
        File.WriteAllText(reportFile+".import.json",JsonSerializer.Serialize(new{imported=true,sendsPurchase=false,record}));
    }
}
