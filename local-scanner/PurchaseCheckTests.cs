namespace Metin2Search.Local;
internal static class PurchaseCheckTests
{
 sealed class Probe:IPurchaseCheckTransport
 {
  public int Reads,Sends;public bool Timeout;
  public Func<int,PurchasePreflight> Ready= _=>new(true,0,0,true,true,"");
  public PurchaseResponse Response=new("NOT_ENOUGH_MONEY","Yetersiz Yang",false);
  public Task<PurchasePreflight> Inspect(PurchaseTarget t,CancellationToken c)=>Task.FromResult(Ready(++Reads));
  public Task<PurchaseResponse> SendOnce(PurchaseTarget t,CancellationToken c){Sends++;return Timeout?Task.FromException<PurchaseResponse>(new TimeoutException()):Task.FromResult(Response);}
 }
 internal static void Run(List<string> results)
 {
  void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);results.Add("PASS: "+label);}
  var saved=new List<PurchaseCheckRecord>();PurchaseCheckCycle Cycle(Probe p,PurchaseCheckBudget? budget=null)=>new(p,saved.Add,(_,_)=>Task.CompletedTask,budget);
  var target=new PurchaseTarget("safir","session",22,"KORDONCELIL","key","request",0,1000,new string('0',374),1,0);
  PurchaseCheckRecord? Run(PurchaseCheckCycle c,PurchaseTarget? t=null)=>c.Check(t??target,CancellationToken.None).GetAwaiter().GetResult();
  var normal=new Probe();var cycle=Cycle(normal);var record=Run(cycle);
  Check(normal.Reads==2&&normal.Sends==1&&record?.Result=="NotEnoughMoney"&&record.Correlation=="time-only","İki sıfır bakiye okuması, tek gönderim; yetersiz para FAKE değildir");
  Check(Run(cycle,target with{ListingKey="other",RequestId="other"}) is null&&normal.Sends==1,"Aynı pazar farklı kategoride döngü içinde tekrar satın alınmaz");
  Run(Cycle(normal));Check(normal.Sends==2,"Yeni döngü tek deneme hakkını yeniler");
  foreach(var funded in new[]{new PurchasePreflight(true,1,0,true,true,""),new PurchasePreflight(true,0,1,true,true,""),new PurchasePreflight(false,0,0,true,true,"")}){
   var p=new Probe{Ready=_=>funded};var c=Cycle(p);Run(c);Check(p.Sends==0&&c.Stopped,"Dolu veya doğrulanmamış Yang/Won bakiyesi gönderimi durdurur");
  }
  var changing=new Probe{Ready=n=>new(true,n==1?0:1,0,true,true,"")};var changed=Cycle(changing);Run(changed);Check(changing.Sends==0&&changed.Stopped,"Son kontrolde değişen bakiye gönderimi engeller");
  var free=new Probe();Run(Cycle(free),target with{Yang=0,Won=0});Check(free.Sends==0,"Ücretsiz ilan seçilmez");
  var moved=new Probe{Ready=_=>new(true,0,0,false,true,"")};Run(Cycle(moved));Check(moved.Sends==0,"Değişen arama/pazar hedefinde gönderim yapılmaz");
  var timed=new Probe{Timeout=true};var timedCycle=Cycle(timed);Run(timedCycle);Run(timedCycle,target with{ShopVid=23,RequestId="later"});Check(timed.Sends==1&&timedCycle.Stopped,"Zaman aşımında bütün denemeler durur; yeniden gönderim yok");
  var success=new Probe{Response=new("OK","Bought",true,true)};var successCycle=Cycle(success);Check(Run(successCycle)?.Result=="Success"&&successCycle.Stopped,"Başarılı satın alma bütün denemeleri durdurur");
  var candidate=new Probe{Response=new("UNKNOWN","[Kişisel premium dükkan] Dükkan şu anda hazırlanıyor.",false)};Check(Run(Cycle(candidate))?.Result=="Unknown","Sadece mesaj veya zaman yakınlığı kesin Preparing üretmez");
  var verifiedPreparing=new Probe{Response=new("PREMIUM_PREPARING","Dükkan şu anda hazırlanıyor.",true)};
  var prepared=Run(Cycle(verifiedPreparing));Check(prepared is {Result:"Preparing",Correlation:"request-and-shop",Scope:"shop",Sent:true,StopAll:false},"Bağlamı doğrulanmış hazırlık yanıtı yalnız ilgili pazara Preparing kaydı üretir");
  var unboundPreparing=new Probe{Response=new("PREMIUM_PREPARING","Dükkan şu anda hazırlanıyor.",false)};var unboundCycle=Cycle(unboundPreparing);
  Check(Run(unboundCycle) is {Result:"Unknown",Scope:"listing"}&&unboundCycle.Stopped,"Hazırlık kodu zaman yakınlığıyla doğrulanmaz; belirsizlik tüm denemeleri durdurur");
  var zeroBudget=new PurchaseCheckBudget(0);var zeroProbe=new Probe();Check(Run(Cycle(zeroProbe,zeroBudget))?.Result=="NotEnoughMoney"&&zeroProbe.Reads==2&&zeroBudget.RemainingYang==0,"Sıfır bütçe eski sıfır bakiye yolunu korur");
  var budget=new PurchaseCheckBudget(126588);var fundedProbe=new Probe{Ready=_=>new(true,126588,0,true,true,""),Response=new("OK","Bought",true,true,ActualYangSpent:1000)};
  var fundedCycle=Cycle(fundedProbe,budget);Check(Run(fundedCycle)?.Result=="Success"&&fundedProbe.Reads==2&&fundedProbe.Sends==1&&fundedCycle.Stopped&&budget.RemainingYang==125588&&!budget.Halted,"126588 Yang bütçe iki taze okumayla tek alıma izin verir ve gerçek harcamayı düşer");
  var nextBudgetProbe=new Probe{Ready=_=>new(true,126588,0,true,true,"")};var nextBudgetCycle=Cycle(nextBudgetProbe,budget);Run(nextBudgetCycle);
  Check(nextBudgetProbe.Sends==0&&nextBudgetCycle.Stopped&&budget.RemainingYang==125588&&budget.Halted,"Yeni döngü aynı bütçeyi yenilemez; kalan üstü bakiye tüm bütçeyi durdurur");
  foreach(var invalidWallet in new[]{new PurchasePreflight(true,126588,1,true,true,""),new PurchasePreflight(true,126589,0,true,true,""),new PurchasePreflight(false,0,0,true,true,"")}){
   var invalidBudget=new PurchaseCheckBudget(126588);var invalidProbe=new Probe{Ready=_=>invalidWallet};var invalidCycle=Cycle(invalidProbe,invalidBudget);Run(invalidCycle);
   Check(invalidProbe.Sends==0&&invalidCycle.Stopped&&invalidBudget.Halted,"Pozitif bütçede Won/doğrulama/bütçe üstü bakiye engeli");
  }
  var changedBudget=new PurchaseCheckBudget(126588);var changedProbe=new Probe{Ready=n=>new(true,n==1?126588:126587,0,true,true,"")};Run(Cycle(changedProbe,changedBudget));
  Check(changedProbe.Sends==0&&changedBudget.Halted,"Bütçe içinde bile iki okuma arasında değişen bakiye manuel müdahale sayılır");
  var mismatchBudget=new PurchaseCheckBudget(126588);var mismatchProbe=new Probe{Ready=_=>new(true,126588,0,true,true,""),Response=new("OK","Bought",true,true,ActualYangSpent:1001)};
  Check(Run(Cycle(mismatchProbe,mismatchBudget))?.Result=="Unknown"&&mismatchBudget.RemainingYang==125587&&mismatchBudget.Halted,"Fiyatla farklı gerçek debit kaydedilir, belirsiz sonuçla tüm bütçe durdurulur");
  var missingDebitBudget=new PurchaseCheckBudget(126588);var missingDebitProbe=new Probe{Ready=_=>new(true,126588,0,true,true,""),Response=new("OK","Bought",true,true)};
  Check(Run(Cycle(missingDebitProbe,missingDebitBudget))?.Result=="Unknown"&&missingDebitBudget.Halted,"Pozitif bütçede doğrulanmış debit olmadan başarı kabul edilmez");
  var inconsistentBudget=new PurchaseCheckBudget(126588);var inconsistentProbe=new Probe{Ready=_=>new(true,126588,0,true,true,""),Response=new("PREMIUM_PREPARING","Preparing",true,ActualYangSpent:5)};
  Check(Run(Cycle(inconsistentProbe,inconsistentBudget)) is {Result:"Unknown",Scope:"listing",StopAll:true}&&inconsistentBudget.RemainingYang==126583&&inconsistentBudget.Halted,"Hazırlık mesajıyla birlikte debit varsa FAKE üretilmez; gerçek kayıp kaydedilip durulur");
  var uncertainBudget=new PurchaseCheckBudget(126588);var uncertainProbe=new Probe{Timeout=true,Ready=_=>new(true,126588,0,true,true,"")};Run(Cycle(uncertainProbe,uncertainBudget));
  var afterTimeout=new Probe();Check(Run(Cycle(afterTimeout,uncertainBudget)) is null&&afterTimeout.Reads==0&&uncertainBudget.Halted,"Belirsiz gönderim sonrası bütçe başka döngüde yeniden kullanılamaz");
  foreach(var (name,id)in new[]{("[SAPPHIRE] Safir, CH1","safir"),("[DIA] Nite","nite"),("[SAPPHIRE] Star","star"),("[RUBY] Lucifer","lucifer"),("[RUBY] Charon","charon"),("Bagjanamu","bagjanamu"),("Arkadaşlar","arkadaslar"),("Marmara","marmara"),("Ezel","ezel"),("Barbaros","barbaros"),("Dandanakan","dandanakan")})Check(PurchaseServerIds.Resolve(name)==id,"Sunucu eşleşmesi: "+id);
  Check(PurchaseServerIds.Resolve("Bilinmeyen Sunucu") is null,"Bilinmeyen sunucu Safir'e varsayılmaz");
 }
}
