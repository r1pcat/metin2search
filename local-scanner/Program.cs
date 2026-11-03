using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Metin2Search.Local;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if(args.Contains("--self-test-cycle"))return CharacterCycleTests.RunIsolated(Option(args,"--data-dir")??Path.Combine(AppContext.BaseDirectory,"cycle-test"));
            if(Option(args,"--import-purchase-canary") is {} canary){
                PurchaseCanaryImport.Run(canary,Option(args,"--context")??throw new InvalidDataException("--context gerekli"),Option(args,"--data-dir")??throw new InvalidDataException("--data-dir gerekli"));return 0;
            }
            if(Option(args,"--probe-open-shop") is {} probePid){
                var report=Option(args,"--report")??throw new InvalidDataException("--report gerekli");
                using var readOnlyClient=new ExternalReader(int.Parse(probePid),new ReaderProfile());
                var capture=readOnlyClient.CaptureAsync(Option(args,"--server")??"Belirtilmedi","Salt okunur tanı","Yerleşim doğrulama",false,CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(report,JsonData.Encode(new{readOnly=true,diagnosticOnly=true,geometryVerified=false,capture}));return 0;
            }
            if(args.Contains("--install-optimized")){
                var source=Option(args,"--source")??throw new InvalidDataException("--source gerekli");
                var target=Option(args,"--data-dir")??throw new InvalidDataException("--data-dir gerekli");
                LocalStore.OptimizeAndInstall(source,target);return 0;
            }
            if(args.Contains("--profile-client")){
                var pid=int.Parse(Option(args,"--profile-client")!);
                var file=Option(args,"--database")??throw new InvalidDataException("--database gerekli");
                var report=Option(args,"--report")??throw new InvalidDataException("--report gerekli");
                LiveProfile.Run(pid,file,int.Parse(Option(args,"--seconds")??"60"),report).GetAwaiter().GetResult();return 0;
            }
            if(args.Contains("--optimize-copy")){
                var source=Option(args,"--source")??throw new InvalidDataException("--source gerekli");
                var target=Option(args,"--data-dir")??throw new InvalidDataException("--data-dir gerekli");
                LocalStore.CreateOptimizedCopy(source,target);return 0;
            }
            if(args.Contains("--compact-data"))
            {
                var dataDirectory=Option(args,"--data-dir")??throw new InvalidDataException("--data-dir gerekli");
                var file=Path.Combine(dataDirectory,"market.sqlite");var before=new FileInfo(file).Length;var timer=System.Diagnostics.Stopwatch.StartNew();
                using var compact=new LocalStore(dataDirectory);
                File.WriteAllText(Path.Combine(dataDirectory,"compact-report.json"),JsonData.Encode(new{beforeBytes=before,afterBytes=new FileInfo(file).Length,seconds=timer.Elapsed.TotalSeconds,items=compact.TradeObservationCount,pages=compact.TradePageCount,integrity=compact.Integrity}));
                return 0;
            }
            if (args.Contains("--self-test")) return SelfTest.Run(Option(args, "--data-dir") ?? Path.Combine(AppContext.BaseDirectory, "self-test"));
            if(!args.Contains("--preview")&&!ScannerElevation.IsElevated)return ScannerElevation.Relaunch(args);
            var directory = Option(args, "--data-dir") ?? (File.Exists(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","PROJECT_LOCATION.json"))) ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","scanner-data")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Metin2Search", "Data"));
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MainWindow(directory);
            if(Option(args,"--start-client") is {} client)window.Loaded+=(_,_)=>window.BeginTradeClient(int.Parse(client));
            var preview = Option(args, "--preview");
            if (preview != null)
            {
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        window.PreviewTrade();
                        await Task.Delay(400);
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(preview); encoder.Save(file);
                    }
                    finally { window.Close(); }
                };
            }
            app.Run(window);
            return 0;
        }
        catch (Exception error)
        {
            var report = Option(args, "--error-file");
            if (report != null) File.WriteAllText(report, error.ToString());
            else MessageBox.Show(error.Message, "Metin2Search • başlatma hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
