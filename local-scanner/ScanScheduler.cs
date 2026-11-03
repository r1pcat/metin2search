using System.Globalization;

namespace Metin2Search.Local;

public sealed class ScanScheduler : IScanScheduler
{
    private sealed class CategoryState
    {
        public uint Total;
        public bool Known;
        public DateTimeOffset LastFull=DateTimeOffset.MinValue;
        public long Disappeared;
        public DateTimeOffset FirstRead=DateTimeOffset.MinValue;
        public DateTimeOffset LastChange=DateTimeOffset.MinValue;
        public DateTimeOffset NextDue=DateTimeOffset.MinValue;
        public int Failures;
    }
    private static readonly TimeZoneInfo Zone=FindZone();
    private static TimeZoneInfo FindZone(){try{return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");}catch(TimeZoneNotFoundException){return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");}}

    private readonly Func<DateTimeOffset> clock;
    private readonly Dictionary<string,CategoryState> categories=[];
    private readonly HashSet<string> needsCompleteRead=[];
    private string[] plan=[];
    private string? lastFullSlot;
    private ScanKind last=ScanKind.Sweep;

    public ScanScheduler(Func<DateTimeOffset>? clock=null)=>this.clock=clock??(()=>DateTimeOffset.UtcNow);
    public bool FullOnStart {get;init;}
    private bool initialDecision=true;
    public int[] FullTourHours {get;init;}=[0,6,12,18];
    public Func<string,bool>? HasCompleteRead {get;init;}
    public bool EnableSweeps {get;init;}
    public TimeSpan MaxStale {get;init;}=TimeSpan.FromMinutes(60);
    public TimeSpan MinSweepGap {get;init;}=TimeSpan.FromMinutes(5);
    public int SweepPageBudget {get;init;}=400;
    public TimeSpan ActiveWindow {get;init;}=TimeSpan.FromMinutes(10);
    public TimeSpan WarmWindow {get;init;}=TimeSpan.FromMinutes(60);
    public TimeSpan WarmInterval {get;init;}=TimeSpan.FromMinutes(2);
    public TimeSpan ColdInterval {get;init;}=TimeSpan.FromMinutes(8);
    public TimeSpan IdleAfter {get;init;}=TimeSpan.FromHours(4);
    public TimeSpan IdleInterval {get;init;}=TimeSpan.FromMinutes(30);
    public TimeSpan FailureBackoff {get;init;}=TimeSpan.FromMinutes(3);
    public TimeSpan MaxBackoff {get;init;}=TimeSpan.FromMinutes(30);
    public TimeSpan Lookahead {get;init;}=TimeSpan.FromSeconds(20);

    public static string Key(TradeCategory category)=>$"{category.Job}:{category.Category}:{category.Sub}";
    public static string Day(DateTimeOffset moment)=>TimeZoneInfo.ConvertTime(moment,Zone).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    internal string Slot(DateTimeOffset moment)
    {
        var local=TimeZoneInfo.ConvertTime(moment,Zone);
        var hour=FullTourHours.Where(h=>h<=local.Hour).DefaultIfEmpty(FullTourHours.Min()).Max();
        return Day(moment)+"/"+hour.ToString(CultureInfo.InvariantCulture);
    }

    public void SetPlan(IEnumerable<string> categoryKeys)
    {
        plan=categoryKeys.ToArray();
        needsCompleteRead.Clear();
        if(HasCompleteRead is not null)foreach(var key in plan)if(!HasCompleteRead(key))needsCompleteRead.Add(key);
    }

    public ScanDecision Next()
    {
        var now=clock();var slot=Slot(now);
        if(initialDecision){initialDecision=false;if(FullOnStart){lastFullSlot=slot;last=ScanKind.Full;needsCompleteRead.Clear();return new ScanDecision(ScanKind.Full);}}
        lastFullSlot??=slot;
        if(lastFullSlot!=slot){
            lastFullSlot=slot;last=ScanKind.Full;needsCompleteRead.Clear();
            return new ScanDecision(ScanKind.Full);
        }
        if(needsCompleteRead.Count>0){
            last=ScanKind.Sweep;
            return new ScanDecision(ScanKind.Sweep,plan.Where(needsCompleteRead.Contains).ToList());
        }
        if(last!=ScanKind.Hot){last=ScanKind.Hot;return new ScanDecision(ScanKind.Hot,PickHot(now));}
        var picked=EnableSweeps?PickSweep(now):[];
        if(picked.Count>0){last=ScanKind.Sweep;return new ScanDecision(ScanKind.Sweep,picked);}
        return new ScanDecision(ScanKind.Hot,PickHot(now));
    }

    private TimeSpan Interval(CategoryState state,DateTimeOffset now)
    {
        if(state.Failures>0){
            var backoff=TimeSpan.FromTicks(FailureBackoff.Ticks*(1L<<Math.Min(state.Failures-1,20)));
            return backoff>MaxBackoff?MaxBackoff:backoff;
        }
        if(state.LastChange>DateTimeOffset.MinValue){
            var changeAge=now-state.LastChange;
            if(changeAge<=ActiveWindow)return TimeSpan.Zero;
            if(changeAge<=WarmWindow)return WarmInterval;
        }
        var quietSince=state.LastChange>DateTimeOffset.MinValue?state.LastChange:state.FirstRead;
        return now-quietSince>=IdleAfter?IdleInterval:ColdInterval;
    }

    private List<string> PickHot(DateTimeOffset now)
    {
        var due=new List<(string key,int order,DateTimeOffset dueAt,DateTimeOffset change)>();
        foreach(var key in plan){
            if(!categories.TryGetValue(key,out var state)){due.Add((key,0,DateTimeOffset.MinValue,DateTimeOffset.MinValue));continue;}
            if(state.NextDue>now+Lookahead)continue;
            var interval=Interval(state,now);
            due.Add((key,interval==TimeSpan.Zero?0:interval<=WarmInterval?1:2,state.NextDue,state.LastChange));
        }
        return due.OrderBy(d=>d.order).ThenByDescending(d=>d.change).ThenBy(d=>d.dueAt).Select(d=>d.key).ToList();
    }

    private List<string> PickSweep(DateTimeOffset now)
    {
        var due=new List<(string key,long disappeared,DateTimeOffset read)>();
        foreach(var key in plan){
            categories.TryGetValue(key,out var state);
            var read=state?.LastFull??DateTimeOffset.MinValue;var age=now-read;
            var dirty=state is {Disappeared:>0}&&age>=MinSweepGap;
            if(dirty||age>=MaxStale)due.Add((key,state?.Disappeared??0,read));
        }
        var picked=new List<string>();var pages=0;
        foreach(var entry in due.OrderByDescending(e=>e.disappeared).ThenBy(e=>e.read)){
            var cost=categories.TryGetValue(entry.key,out var state)&&state.Known?(int)((state.Total+10)/11):50;
            if(picked.Count>0&&pages+cost>SweepPageBudget)continue;
            picked.Add(entry.key);pages+=cost;
        }
        return picked;
    }

    public void CategoryRead(ScanKind kind,string categoryKey,uint total,int appeared,int changed,bool completed)
    {
        var now=clock();
        if(!categories.TryGetValue(categoryKey,out var state))categories[categoryKey]=state=new CategoryState();
        if(state.FirstRead==DateTimeOffset.MinValue)state.FirstRead=now;
        state.Failures=0;
        if(changed>0)state.LastChange=now;
        if(kind==ScanKind.Hot){
            if(state.Known){var gone=(long)state.Total+appeared-(long)total;if(gone>0)state.Disappeared+=gone;}
            state.Total=total;state.Known=true;
        }
        else if(completed){
            state.Total=total;state.Known=true;state.LastFull=now;state.Disappeared=0;
            needsCompleteRead.Remove(categoryKey);
        }
        state.NextDue=now+Interval(state,now);
    }

    public void CategoryFailed(ScanKind kind,string categoryKey)
    {
        var now=clock();
        if(!categories.TryGetValue(categoryKey,out var state))categories[categoryKey]=state=new CategoryState();
        if(state.FirstRead==DateTimeOffset.MinValue)state.FirstRead=now;
        state.Failures++;
        needsCompleteRead.Remove(categoryKey);
        state.NextDue=now+Interval(state,now);
    }

    public (uint total,long disappeared,DateTimeOffset lastFull)? State(string categoryKey)=>
        categories.TryGetValue(categoryKey,out var s)?(s.Total,s.Disappeared,s.LastFull):null;
}
