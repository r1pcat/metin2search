using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Metin2Search.Local;

public sealed record GameClientIdentity(int Pid,long StartedUtcTicks,string Executable,bool Ready);
public interface IClientLauncher
{
    IReadOnlyList<GameClientIdentity> Clients();
    void ClickMetin2Play(CancellationToken token);
}
public sealed class ClientRecovery(IClientLauncher launcher,Func<TimeSpan,CancellationToken,Task>? delay=null)
{
    public Task<GameClientIdentity> Start(Action<string> progress,CancellationToken token)
        =>Launch(null,progress,token);
    public Task<GameClientIdentity> Restart(string expectedExecutable,Action<string> progress,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(expectedExecutable))throw new IOException("Eski istemcinin EXE yolu bilinmiyor; yanlış istemciye bağlanılmadı.");
        return Launch(expectedExecutable,progress,token);
    }
    private async Task<GameClientIdentity> Launch(string? expectedExecutable,Action<string> progress,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var baseline=launcher.Clients().Select(c=>(c.Pid,c.StartedUtcTicks)).ToHashSet();
        progress("Oyun açılıyor • Gameforge Metin2 Oyna düğmesi bekleniyor");
        await Task.Run(()=>launcher.ClickMetin2Play(token),token);
        for(var poll=0;poll<240;poll++){
            token.ThrowIfCancellationRequested();
            var fresh=launcher.Clients().Where(c=>!baseline.Contains((c.Pid,c.StartedUtcTicks))&&!string.IsNullOrWhiteSpace(c.Executable)
                &&(expectedExecutable is null||string.Equals(c.Executable,expectedExecutable,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(fresh.Length>1)throw new IOException("Birden fazla yeni Metin2 istemcisi açıldı; hedef belirsiz.");
            if(fresh is [{Ready:true} client])return client;
            progress(fresh.Length>0?"Yeni istemcinin Python/oyun penceresi yükleniyor":"Gameforge'dan yeni istemci bekleniyor");
            await (delay??Task.Delay)(TimeSpan.FromSeconds(1),token);
        }
        throw new IOException("Oyna isteğinden sonra istemci 4 dakikada hazır olmadı. İstek tekrarlanmadı; Gameforge'u kontrol edin.");
    }
}

public sealed class GameforgeLauncher : IClientLauncher
{
    public IReadOnlyList<GameClientIdentity> Clients()
    {
        var result=new List<GameClientIdentity>();
        foreach(var process in Process.GetProcessesByName("metin2client"))using(process){
            try{
                var identity=Identity(process.Id);
                var ready=false;
                try{ready=process.MainWindowHandle!=IntPtr.Zero&&process.Modules.Cast<ProcessModule>().Any(m=>string.Equals(m.ModuleName,"python27.dll",StringComparison.OrdinalIgnoreCase));}
                catch(System.ComponentModel.Win32Exception error)when(error.NativeErrorCode==5){throw new IOException("Yeni Metin2 bulundu, ancak Python modülüne erişim reddedildi. Tarayıcı için Windows yönetici iznini onaylayın.",error);}
                catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException){}
                result.Add(identity with{Ready=ready});
            }catch(System.ComponentModel.Win32Exception error)when(error.NativeErrorCode==5){throw new IOException("Metin2 sürecine erişim reddedildi. Tarayıcı için Windows yönetici iznini onaylayın.",error);}
            catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException){}
        }
        return result;
    }
    public static GameClientIdentity Identity(int pid)
    {
        using var handle=OpenProcess(0x1000,false,pid);
        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"İstemcinin süreç kimliği okunamadı.");
        var path=new System.Text.StringBuilder(32768);var length=path.Capacity;
        if(!QueryFullProcessImageName(handle,0,path,ref length)||!GetProcessTimes(handle,out var started,out _,out _,out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"İstemcinin EXE yolu/başlangıç zamanı okunamadı.");
        return new(pid,DateTime.FromFileTimeUtc(started).Ticks,path.ToString(),false);
    }
    public static bool Alive(GameClientIdentity client)
    {
        try{using var process=Process.GetProcessById(client.Pid);return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==client.StartedUtcTicks;}
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
    }
    public void ClickMetin2Play(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var windows=new List<(int Pid,IntPtr Handle)>();
        var family=new HashSet<int>();
        foreach(var p in Process.GetProcessesByName("gfclient"))using(p){
            try{family.Add(p.Id);if(p.MainWindowHandle!=IntPtr.Zero)windows.Add((p.Id,p.MainWindowHandle));}catch(InvalidOperationException){}
        }
        if(windows.Count!=1)throw new IOException("Tek bir Gameforge Client penceresi bulunamadı; açıp Metin2 sayfasını seçin.");
        var target=windows[0];
        ShowWindow(target.Handle,9);
        for(var layout=0;layout<2;layout++){
            var scale=Math.Max(96u,GetDpiForWindow(target.Handle))/96d;
            if(!SetWindowPos(target.Handle,IntPtr.Zero,0,0,(int)Math.Round(1000*scale),(int)Math.Round(690*scale),0x0040))
                throw new IOException("Gameforge penceresi 0,0 konumuna yerleştirilemedi.");
        }
        Raise(target.Handle);
        var ready=false;
        for(var attempt=0;attempt<30;attempt++){
            token.ThrowIfCancellationRequested();
            if(GetForegroundWindow()==target.Handle&&GetWindowRect(target.Handle,out var bounds)&&bounds.Left==0&&bounds.Top==0){ready=true;break;}
            token.WaitHandle.WaitOne(100);
        }
        if(!ready)throw new IOException("Gameforge öne getirilemedi; Oyna tıklanmadı.");
        token.ThrowIfCancellationRequested();
        GetWindowThreadProcessId(target.Handle,out var pid);
        if(pid!=target.Pid||!GetWindowRect(target.Handle,out var rect))throw new IOException("Gameforge pencere bağlamı değişti.");
        var point=GameforgePlayGeometry.Point(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
        Raise(target.Handle);
        token.WaitHandle.WaitOne(150);token.ThrowIfCancellationRequested();
        EnsureClickOwner(target.Pid,family,point);
        if(!SetCursorPos(point.X,point.Y))throw new IOException("Oyna konumuna fare taşınamadı.");
        token.WaitHandle.WaitOne(100);
        token.ThrowIfCancellationRequested();
        EnsureClickOwner(target.Pid,family,point);
        if(!GetWindowRect(target.Handle,out var finalRect)||!rect.Equals(finalRect))throw new IOException("Tıklama öncesi Gameforge konumu değişti.");
        var inputs=new[]{new MouseInput{Type=0,Data=new(){Flags=0x0002}},new MouseInput{Type=0,Data=new(){Flags=0x0004}}};
        var sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<MouseInput>());
        if(sent!=2){
            if(sent==1)SendInput(1,[new MouseInput{Type=0,Data=new(){Flags=0x0004}}],Marshal.SizeOf<MouseInput>());
            throw new IOException("Gameforge fare tıklaması tamamlanamadı; Oyna isteği tekrarlanmadı.");
        }
    }
    private static void EnsureClickOwner(int targetPid,IReadOnlySet<int> family,System.Drawing.Point point)
    {
        GetWindowThreadProcessId(GetForegroundWindow(),out var foregroundPid);
        GetWindowThreadProcessId(WindowFromPoint(point),out var pointPid);
        if(!GameforgePlayGeometry.OwnsClick(targetPid,family,foregroundPid,pointPid))
            throw new IOException($"Oyna noktasında Gameforge odağı doğrulanamadı (GF {targetPid}, öndeki {foregroundPid}, nokta {pointPid}); tıklanmadı.");
    }
    private static void Raise(IntPtr handle)
    {
        if(SetForegroundWindow(handle)&&GetForegroundWindow()==handle)return;
        var foreground=GetForegroundWindow();
        var foregroundThread=GetWindowThreadProcessId(foreground,out _);
        var currentThread=GetCurrentThreadId();
        var attached=foregroundThread!=0&&foregroundThread!=currentThread&&AttachThreadInput(currentThread,foregroundThread,true);
        try{BringWindowToTop(handle);SetForegroundWindow(handle);}
        finally{if(attached)AttachThreadInput(currentThread,foregroundThread,false);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseData{public int X,Y;public uint Mouse,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseInput{public uint Type;public MouseData Data;}
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,MouseInput[] inputs,int size);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint current,uint target,bool attach);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",SetLastError=true)]private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle handle,uint flags,System.Text.StringBuilder path,ref int length);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetProcessTimes(Microsoft.Win32.SafeHandles.SafeProcessHandle handle,out long started,out long exited,out long kernel,out long user);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int processId);
}

internal static class GameforgePlayGeometry
{
    public static bool OwnsClick(int targetPid,IReadOnlySet<int> family,int foregroundPid,int pointPid)
        =>targetPid>0&&family.Contains(targetPid)&&family.Contains(foregroundPid)&&family.Contains(pointPid);
    public static System.Drawing.Point Point(int left,int top,int width,int height)
    {
        if(left!=0||top!=0||width<1000||width>4000||Math.Abs(height-width*690d/1000)>2)
            throw new IOException("Gameforge pencere konumu/boyutu sabit Oyna düzeniyle eşleşmiyor.");
        return new((int)Math.Round(width*370d/1000),(int)Math.Round(height*104d/690));
    }
}
