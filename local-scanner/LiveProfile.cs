using System.Diagnostics;

namespace Metin2Search.Local;

internal static class LiveProfile
{
    public static async Task Run(int pid,string database,int seconds,string report)
    {
        if(seconds is <10 or >300)throw new InvalidDataException("Kısa profil 10–300 saniye olmalı.");
        var transport=new ScanMetrics();
        await using var bridge=new NativeTradeBridge(pid,transport);
        var inspection=await bridge.Command("inspect",CancellationToken.None);
        using var store=new LocalStore(Path.GetDirectoryName(Path.GetFullPath(database))!,false);
        store.ConfigureServer(inspection.GetProperty("serverInfo").GetString()!);
        using var lease=store.AcquireScannerWriter();
        var versions=store.TradeObservationCount;var pages=store.TradePageCount;
        string? run=null;using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var timer=Stopwatch.StartNew();
        try{
            await TradeScanner.Run(bridge,p=>{store.SaveTrade(p);return Task.CompletedTask;},_=>{},stop.Token,a=>{if(a.Kind=="RunStart")run=a.RunId;store.TradeAudit(a);},metrics:transport);
        }catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        finally{
            if(run is not null)store.StopTradeRun(run,"Bounded read-only search profile ended");
            store.PublishScannerStatus(true);
            var result=new{pid,server=store.ServerName,database=store.FilePath,seconds=timer.Elapsed.TotalSeconds,pagesSaved=store.TradePageCount-pages,newVersions=store.TradeObservationCount-versions,fullTourVerified=false,purchasesSent=false,transport=transport.Snapshot(),storage=store.Metrics.Snapshot()};
            File.WriteAllText(report,JsonData.Encode(result));
        }
    }
}
