using System.Text.Json;
using System.Diagnostics;
using System.Text;

namespace Metin2Search.Local;
public sealed record MarketCheck(uint Vid,string Seller,string Status,string Reason,DateTimeOffset CheckedAt);
public static class MarketVerification
{
    public static async Task<MarketCheck> Check(ITradeBridge bridge,uint vid,string seller,CancellationToken token,Func<JsonElement,CancellationToken,Task>? verifiedOpen=null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var hex=Convert.ToHexString(Encoding.GetEncoding(1254).GetBytes(seller));
        var status="Unknown";var reason="Pazar doğrulanamadı";
        try
        {
            var result=await bridge.Command($"marketbegin {vid} {hex}",token);
            var timer=Stopwatch.StartNew();
            while(true)
            {
                status=result.GetProperty("status").GetString()!;reason=result.GetProperty("reason").GetString()!;
                if(status!="Pending")break;
                if(timer.Elapsed>TimeSpan.FromSeconds(2)){status="Unknown";reason="Açılış cevabı gelmedi; FAKE kanıtı değil";break;}
                await Task.Delay(50,token);result=await bridge.Command("marketpoll",token);
            }
            if(status=="Open"&&(result.GetProperty("vid").GetUInt32()!=vid||result.GetProperty("seller").GetString()!=seller)){status="Unknown";reason="Pazar cevabı hedefle eşleşmedi";}
            if(status!="Open")status="Unknown";
            if(status=="Open"&&verifiedOpen is not null){
                try{await verifiedOpen(result,token);}
                catch(Exception error)when(error is IOException or InvalidDataException or InvalidOperationException or TradeQueryException){reason+=" · Yerleşim doğrulanamadı: "+error.Message;}
            }
        }
        catch(TradeQueryException error){status="Unknown";reason=error.Message;}
        finally
        {
            try{await bridge.Command("marketend",CancellationToken.None);}catch(Exception error)when(error is IOException or TimeoutException or TradeQueryException){}
        }
        return new(vid,seller,status,reason,DateTimeOffset.UtcNow);
    }
}
