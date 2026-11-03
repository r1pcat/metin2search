using System.Text.Json;

namespace Metin2Search.Local;

public static class PurchaseServerIds
{
    public static string? Resolve(string info)
    {
        var name=System.Text.RegularExpressions.Regex.Replace(ServerStorage.Name(info),@"^\[[^\]]+\]\s*","").Normalize(System.Text.NormalizationForm.FormD);
        name=new string(name.Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Replace('ı','i');
        return name is "nite" or "star" or "safir" or "lucifer" or "charon" or "bagjanamu" or "arkadaslar" or "marmara" or "ezel" or "barbaros" or "dandanakan"?name:null;
    }
}

public sealed record PurchaseTarget(string Server,string Session,uint ShopVid,string Seller,string ListingKey,string RequestId,uint Won,uint Yang,string RawHex,long Generation,int Slot);
public sealed record PurchasePreflight(bool WalletVerified,long Yang,long Won,bool SameTarget,bool SendingSupported,string Reason);
public sealed record PurchaseResponse(string Code,string Text,bool RequestAndShopVerified,bool ConfirmedSuccess=false,bool SuspectedInventoryChange=false,long? ActualYangSpent=null);
public sealed class PurchaseCheckBudget(long initialYang)
{
    private readonly object sync=new();
    private long remaining=initialYang>=0?initialYang:throw new ArgumentOutOfRangeException(nameof(initialYang));
    private bool halted;
    public long RemainingYang {get{lock(sync)return remaining;}}
    public bool Halted {get{lock(sync)return halted;}}
    public void Halt(){lock(sync)halted=true;}
    public bool RecordSpend(long confirmedYang)
    {
        lock(sync){if(confirmedYang<0||confirmedYang>remaining)return false;remaining-=confirmedYang;return true;}
    }
}
public interface IPurchaseCheckTransport
{
    Task<PurchasePreflight> Inspect(PurchaseTarget target,CancellationToken token);
    Task<PurchaseResponse> SendOnce(PurchaseTarget target,CancellationToken token);
}
public sealed record PurchaseCheckRecord(int SchemaVersion,string Server,string Session,uint ShopVid,string Seller,string RequestId,string ListingKey,string CheckedAt,string Result,string Correlation,string Scope,string Reason,string EvidenceSource,bool Sent,bool StopAll);

public sealed class PurchaseCheckCycle(IPurchaseCheckTransport transport,Action<PurchaseCheckRecord> save,Func<TimeSpan,CancellationToken,Task>? delay=null,PurchaseCheckBudget? budget=null,bool refuseAffordable=false)
{
    private readonly SemaphoreSlim oneFlight=new(1,1);
    private readonly HashSet<(string,string,uint,string)> attempted=[];
    private bool stopped;
    public bool Stopped=>stopped||budget?.Halted==true;
    private bool WalletAllowed(PurchasePreflight wallet)=>wallet.WalletVerified&&wallet.Won==0&&wallet.Yang>=0&&(budget is null?wallet.Yang==0:wallet.Yang<=budget.RemainingYang);
    public async Task<PurchaseCheckRecord?> Check(PurchaseTarget target,CancellationToken token)
    {
        await oneFlight.WaitAsync(token);try{
            if(Stopped||!attempted.Add((target.Server,target.Session,target.ShopVid,target.Seller)))return null;
            bool sent=false,skippedAffordable=false,notSent=false;var result="Unknown";var correlation="none";var reason="";var scope="listing";
            try{
                if(target.ShopVid==0||string.IsNullOrWhiteSpace(target.Seller)||string.IsNullOrWhiteSpace(target.ListingKey)||string.IsNullOrWhiteSpace(target.RequestId))reason="Eksik pazar/ilan kimliği; gönderilmedi.";
                else if(target.Won==0&&target.Yang==0)reason="Ücretsiz/geçersiz fiyat; gönderilmedi.";
                else{
                    var ready=await transport.Inspect(target,token);
                    if(!WalletAllowed(ready)){reason="Doğrulanmış bakiye izin verilen Yang bütçesine veya sıfır Won koşuluna uymuyor; gönderilmedi.";stopped=true;}
                    else if(!ready.SameTarget){reason="Arama kaydı/sunucu/pazar bağlamı değişti; gönderilmedi.";stopped=true;}
                    else if(!ready.SendingSupported){reason=ready.Reason;stopped=true;}
                    else if(refuseAffordable&&target.Won==0&&ready.Yang>=target.Yang)skippedAffordable=true;
                    else{
                        await (delay??Task.Delay)(TimeSpan.FromSeconds(1),token);
                        var previousYang=ready.Yang;
                        ready=await transport.Inspect(target,token);
                        if(!WalletAllowed(ready)||ready.Yang!=previousYang||!ready.SameTarget||!ready.SendingSupported){reason="Gönderim öncesi son bakiye/hedef kontrolü başarısız; gönderilmedi.";stopped=true;}
                        else{
                            sent=true;
                            var response=await transport.SendOnce(target,token);
                            reason=response.Text;correlation=response.RequestAndShopVerified?"request-and-shop":"time-only";
                            if(response.ConfirmedSuccess){
                                stopped=true;
                                if(budget is null)result="Success";
                                else{
                                    var actual=response.ActualYangSpent;
                                    var bound=target.Won==0&&response.RequestAndShopVerified&&actual is >=0;
                                    var recorded=bound&&budget.RecordSpend(actual!.Value);
                                    if(!recorded||actual!=target.Yang)reason="Doğrulanmış Yang harcaması ilan fiyatı/bütçesiyle eşleşmedi; tüm denemeler durdu. "+reason;
                                    else result="Success";
                                }
                            }
                            else if(response.SuspectedInventoryChange){reason="Beklenmeyen envanter değişimi; olası satın alma, tüm denemeler durdu. "+reason;stopped=true;}
                            else if(response.ActualYangSpent is long unexpected&&unexpected!=0){
                                if(response.RequestAndShopVerified&&unexpected>0)budget?.RecordSpend(unexpected);
                                reason="Yanıt sonucuyla uyuşmayan Yang değişimi; tüm denemeler durdu. "+reason;stopped=true;
                            }
                            else if(response.Code is "NOT_ENOUGH_MONEY" or "NOT_ENOUGH_MONEY_EX")result="NotEnoughMoney";
                            else if(response.Code=="PREMIUM_PREPARING"&&response.RequestAndShopVerified){result="Preparing";scope="shop";}
                            else stopped=true;
                        }
                    }
                }
            }catch(Exception error)when((error is IOException or TimeoutException)&&!sent){notSent=true;reason="Gönderilmeden önce istemci bağlantısı kesildi (gönderim hiç başlamadı): "+error.Message;}
            catch(Exception error)when(error is IOException or TimeoutException){reason="İşlem sonucu belirsiz; tekrar gönderilmedi: "+error.Message;stopped=true;}
            catch(OperationCanceledException){stopped=true;budget?.Halt();throw;}
            if(skippedAffordable||notSent){attempted.Remove((target.Server,target.Session,target.ShopVid,target.Seller));return null;}
            if(stopped&&result!="Success")budget?.Halt();
            var record=new PurchaseCheckRecord(1,target.Server,target.Session,target.ShopVid,target.Seller,target.RequestId,target.ListingKey,DateTimeOffset.UtcNow.ToString("O"),result,correlation,scope,reason,"scanner-purchase-guard-v1",sent,stopped);
            save(record);return record;
        }finally{oneFlight.Release();}
    }
}

public sealed partial class LocalStore
{
    private void InitializePurchaseChecks()=>Exec("CREATE TABLE IF NOT EXISTS purchase_checks(id INTEGER PRIMARY KEY AUTOINCREMENT,payload_json TEXT NOT NULL);CREATE UNIQUE INDEX IF NOT EXISTS ix_purchase_request ON purchase_checks(json_extract(payload_json,'$.requestId'));CREATE INDEX IF NOT EXISTS ix_purchase_seller ON purchase_checks(json_extract(payload_json,'$.seller'),json_extract(payload_json,'$.shopVid'));");
    public bool SavePurchaseCheck(PurchaseCheckRecord record)
    {
        var validScope=record.Result=="Preparing"?record.Scope=="shop"&&record.Correlation=="request-and-shop"&&record.Sent:record.Scope=="listing"&&(record.Result is "Unknown" or "NotEnoughMoney" or "Success");
        if(record.SchemaVersion!=1||ServerName is null||record.Server!=PurchaseServerIds.Resolve(ServerName)||record.Session!=SessionId||record.ShopVid==0||string.IsNullOrWhiteSpace(record.RequestId)||!validScope)throw new InvalidDataException("Doğrulanmamış satın alma kaydı reddedildi.");
        lock(this){
            if(Query("SELECT 1 FROM compact_presence p JOIN compact_versions v ON v.key=p.key WHERE p.key=? AND p.session=? AND p.vid=? AND v.seller=? LIMIT 1",record.ListingKey,SessionId,record.ShopVid,record.Seller).Count==0)throw new InvalidDataException("Satın alma kanıtı mevcut sunucu/session/VID/ilan ile eşleşmedi.");
            var json=JsonSerializer.Serialize(record,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
            var old=Query("SELECT payload_json FROM purchase_checks WHERE json_extract(payload_json,'$.requestId')=?",record.RequestId).FirstOrDefault();
            if(old is not null){if((string)old[0]! !=json)throw new InvalidDataException("Aynı requestId için farklı sonuç değiştirilemez.");return false;}
            BeginDailyTransaction();Exec("BEGIN IMMEDIATE");try{Execute("INSERT INTO purchase_checks(payload_json) VALUES(?)",json);RecordDaily("purchase-check",record.RequestId,record);Exec("COMMIT");}catch{try{Exec("ROLLBACK");}catch{}throw;}
        }
        QueueDailyMaintenance();return true;
    }
}
