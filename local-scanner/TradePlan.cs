namespace Metin2Search.Local;

internal static class TradePlan
{
    internal static bool Excluded(TradeCategory c)=>(c.Category,c.Sub) is (2,8) or (6,1) or (7,4) or (7,0) or (7,1) or (12,3) or (11,1);
    private static bool Shared(TradeCategory c)=>(c.Category,c.Sub) is
        (1,>=0 and <=3) or (4,>=0 and <=5) or (5,>=0 and <=3)
        or (6,>=0 and <=2) or (7,>=0 and <=3) or (8,>=0 and <=7)
        or (11,>=0 and <=1) or (12,0 or 1 or 2 or 4 or 5 or 6 or 7)
        || (c.Category==3 && c.Sub is 2 or 3)
        || (c.Category==9 && c.Sub is 1 or 3 or 4 or 5)
        || (c.Category==10 && c.Sub is 0 or 3 or 4);
    private static int GroupJob(TradeCategory c){
        if(c.QueryJob.HasValue)return -1;
        if(Shared(c))return -1;
        if(c.Category==2&&c.Sub==0&&c.Job is 0 or 1)return 0;
        if(c.Category==9&&c.Sub==0&&c.Job is 0 or 2)return 0;
        return c.Job;
    }
    internal static TradeCategory[] Optimize(IEnumerable<TradeCategory> source)=>source
        .SelectMany(Covered).Where(c=>!Excluded(c))
        .GroupBy(c=>(c.Job,c.Category,c.Sub))
        .Select(g=>g.OrderByDescending(c=>c.QueryJob.HasValue).First())
        .GroupBy(c=>(c.Category,c.Sub,Job:GroupJob(c)))
        .Select(g=>{
            var entries=g.OrderBy(c=>c.Job).ToArray();var first=entries[0];
            return first with{CoveredCategories=entries,Label=entries.Length>1?"Ortak / "+string.Join(" / ",first.Label.Split(" / ").Skip(1)):first.Label};
        }).ToArray();
    internal static IEnumerable<TradeCategory> Covered(TradeCategory category)=>category.CoveredCategories??[category];
    internal static TradeCategory[] FromInspection(System.Text.Json.JsonElement inspection)
    {
        var source=inspection.GetProperty("plan").EnumerateArray().Select(p=>new TradeCategory(p.GetProperty("job").GetInt32(),p.GetProperty("category").GetInt32(),p.GetProperty("sub").GetInt32(),p.GetProperty("label").GetString()!)).ToArray();
        if(inspection.TryGetProperty("allCharacterJob",out var all)&&all.ValueKind==System.Text.Json.JsonValueKind.Number&&all.TryGetInt32(out var queryJob)&&inspection.TryGetProperty("categoryTree",out var tree)&&tree.TryGetProperty("jobs",out var jobs)){
            var classes=jobs.EnumerateObject().Select(p=>int.TryParse(p.Name,out var id)?id:-1).Where(id=>id>=0&&id!=queryJob).Distinct().Order().ToArray();
            if(classes.Length>0)source=source.Select(c=>c.Job==queryJob?c with{Job=classes[0],QueryJob=queryJob,CoveredCategories=classes.Select(job=>c with{Job=job,QueryJob=queryJob}).ToArray()}:c).ToArray();
        }
        return Optimize(source);
    }
}
