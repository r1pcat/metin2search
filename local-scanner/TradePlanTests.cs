namespace Metin2Search.Local;

internal static class TradePlanTests
{
    internal static void Run(string directory,List<string> results)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add("PASS: "+name);}
        var source=new[]{new TradeCategory(0,2,0,"Savaşçı / Silahlar / Tek elli"),new(1,2,0,"Ninja / Silahlar / Tek elli"),new(2,2,0,"Sura / Silahlar / Tek elli"),new(1,2,1,"Ninja / Silahlar / Hançer"),new(0,3,0,"Savaşçı / Zırhlar"),new(1,3,0,"Ninja / Zırhlar"),new(0,4,0,"Savaşçı / Mücevher / Bilezik"),new(1,4,0,"Ninja / Mücevher / Bilezik"),new(0,9,0,"Savaşçı / Kostüm"),new(1,9,0,"Ninja / Kostüm"),new(0,10,0,"Savaşçı / Beceri"),new(1,10,0,"Ninja / Beceri")};
        source=source.Concat(new[]{(Category:3,Sub:2),(Category:3,Sub:3),(Category:9,Sub:3)}.SelectMany(kind=>Enumerable.Range(0,5).Select(job=>new TradeCategory(job,kind.Category,kind.Sub,"Karakter / Ortak / Alt kategori")))).ToArray();
        var plan=TradePlan.Optimize(source);
        Check(plan.Length==12&&plan.Count(c=>c.Category==2&&c.Sub==0)==2&&plan.Any(c=>c.Category==2&&c.Sub==0&&c.Job==2)&&plan.Count(c=>c.Category==4)==1,"Ninja ortak kılıcı birleştirilir; Sura'ya özel kılıçlar korunur");
        Check(plan.Count(c=>c.Category==3&&c.Sub==2)==1&&plan.Count(c=>c.Category==3&&c.Sub==3)==1&&plan.Count(c=>c.Category==9&&c.Sub==3)==1,"Kalkan, eldiven ve omuz kuşağı beş karakter için yalnız birer sorgudur");
        Check(plan.Sum(c=>TradePlan.Covered(c).Count())==source.Length,"Birleşen sorguların tüm eski sınıf kapsamları korunur");
        using var db=new LocalStore(Path.Combine(directory,"shared-plan"));db.ConfigureServer("PlanTest, CH1");
        var shared=plan[0];var raw=new byte[187];raw[13]=1;raw[17]=19;raw[46]=(byte)'A';
        void Tour(bool item){
            var id=Guid.NewGuid().ToString("N");var aliases=TradePlan.Covered(shared).ToArray();
            db.TradeAudit(new("RunStart",id,"",null,0,aliases.Length,"test"));
            foreach(var alias in aliases){db.TradeAudit(new("Pending",id,id+":"+alias.Job,alias,0,0,""));db.TradeAudit(new("Running",id,id+":"+alias.Job,alias,0,0,""));}
            db.SaveTrade(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,shared,1,item?1u:0u,item?1u:0u,item?raw:[]){RunId=id,QueryId=id});
            foreach(var alias in aliases)db.TradeAudit(new("Complete",id,id+":"+alias.Job,alias,1,item?1:0,""));
            db.TradeAudit(new("RunEnd",id,"",null,aliases.Length,aliases.Length,"test"));
        }
        Tour(true);
        Check(db.ScanRuns()[0].Status=="Tamamlandı"&&db.TradeCoverage().Count==2&&db.TradeItems()[0].IsActive==true,"Tek paylaşılan cevap iki ortak sınıf kapsamını eksiksiz tamamlar");
        Tour(false);Check(db.TradeItems()[0].IsActive==true,"Ortak kategoride bir tur yokluk ilanı kaldırmaz");
        Tour(false);Check(db.TradeItems()[0].IsActive==false&&db.ScanRuns().Count==3,"Ortak kategoride iki eksiksiz yokluk turu kaldırmayı ve kalıcı tur geçmişini korur");
        Check(LocalStore.FormatScanDuration(TimeSpan.FromSeconds(5403))=="90 dk 03 sn","Tur süresi 60 dakikadan sonra da toplam dakika ve saniye olarak gösterilir");
        var assembly=typeof(TradePlanTests).Assembly;
        using var fixture=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("live-category-inspection.json")))!;
        using var json=System.Text.Json.JsonDocument.Parse(fixture);
        var live=TradePlan.Optimize(json.RootElement.GetProperty("plan").EnumerateArray().Select(p=>new TradeCategory(p.GetProperty("job").GetInt32(),p.GetProperty("category").GetInt32(),p.GetProperty("sub").GetInt32(),p.GetProperty("label").GetString()!)));
        Check(live.Length==80&&!live.SelectMany(TradePlan.Covered).Any(c=>(c.Category,c.Sub) is (2,8) or (6,1) or (7,4) or (7,0) or (7,1) or (12,3) or (11,1)),"Gerçek istemci ağacı 80 sorgudur; saç renkleri/oltalar/kazmalar/özel eşya diğerleri ve önceki hariç dallar bütün sınıf kapsamlarından çıkarılır");
        Check(new[]{(6,0),(6,2),(7,2),(7,3),(11,0)}.All(key=>live.Any(c=>(c.Category,c.Sub)==key)),"İksir kullanım malları/diğerleri, balıklar/madenler ve özel eşyanın kalan alt dalı korunur");
        Check(live.Count(c=>c.Category==10&&c.Sub is 1 or 2)==10&&live.Count(c=>c.Category==10&&c.Sub is 0 or 3 or 4)==3,"Beceri ve unutkanlık kitapları beş sınıf; misilleme/diğer/at kitapları tek sorgudur");
        Check(live.Count(c=>c.Category==9&&c.Sub==0)==4&&live.Count(c=>c.Category==9&&c.Sub==2)==5&&live.Count(c=>c.Category==9&&c.Sub is 1 or 3 or 4 or 5)==4,"Kostüm silahları dört sınıf sorgusu; saçlar sınıfa özel, gövde/kuşak/diğer/aura ortak kalır");
        var again=TradePlan.Optimize(live.Concat(live));
        Check(again.Length==80&&again.Sum(c=>TradePlan.Covered(c).Count())==live.Sum(c=>TradePlan.Covered(c).Count()),"Tekrarlı veya önceden optimize edilmiş plan kapsam kaybetmeden aynı sorgu sayısını korur");
        var unknown=TradePlan.Optimize(Enumerable.Range(0,5).Select(job=>new TradeCategory(job,4,99,"Yeni dal")));
        Check(unknown.Length==5,"Bilinmeyen yeni alt dal kanıt olmadan ortak sayılıp sınıf kapsamı kaybettirmez");
        var allInspection=System.Text.Json.JsonSerializer.SerializeToElement(new{allCharacterJob=5,categoryTree=new{jobs=new Dictionary<string,string>{{"0","Warrior"},{"1","Ninja"},{"2","Sura"},{"3","Shaman"},{"4","Lycan"},{"5","Hepsi"}}},plan=new[]{new{job=5,category=9,sub=2,label="Ortak / Kostümler / Saç"}}});
        var allHair=TradePlan.FromInspection(allInspection);
        Check(allHair.Length==1&&allHair[0].QueryJob==5&&TradePlan.Covered(allHair[0]).Select(c=>c.Job).SequenceEqual(Enumerable.Range(0,5)),"İstemcinin gerçek tüm-karakter saç sorgusu tek istekte beş eski sınıf kapsamını korur");
        var optimizedAll=TradePlan.Optimize(allHair.Concat(allHair));
        Check(optimizedAll.Length==1&&optimizedAll[0].QueryJob==5&&TradePlan.Covered(optimizedAll[0]).Count()==5,"Tüm-karakter sorgusu tekrar optimize edilince tekrar beş fiziksel isteğe bölünmez");
        var absentAll=System.Text.Json.JsonSerializer.SerializeToElement(new{allCharacterJob=(int?)null,plan=Enumerable.Range(0,5).Select(job=>new{job,category=9,sub=2,label="Hair"})});
        Check(TradePlan.FromInspection(absentAll).Length==5,"Tüm-karakter seçeneği yoksa null kimlik uydurulmaz ve sınıfa özel saçlar kaybedilmez");
    }
}

