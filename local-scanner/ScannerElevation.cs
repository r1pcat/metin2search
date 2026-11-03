using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Metin2Search.Local;

internal static class ScannerElevation
{
    public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    internal static ProcessStartInfo StartInfo(string executable,IEnumerable<string> arguments)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas",WorkingDirectory=AppContext.BaseDirectory};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        return start;
    }

    public static int Relaunch(string[] arguments)
    {
        try{
            using var child=Process.Start(StartInfo(Environment.ProcessPath??throw new IOException("Tarayıcı EXE yolu okunamadı."),arguments));
            if(child is null)throw new IOException("Yönetici yetkili tarayıcı başlatılamadı.");
            return 0;
        }catch(Win32Exception error)when(error.NativeErrorCode==1223){
            System.Windows.MessageBox.Show("Yönetici izni verilmedi. Gameforge'un açtığı yönetici yetkili Metin2 istemcisine bağlanmak için tarayıcıyı yeniden açıp Windows izin penceresini onaylayın.","Metin2Search • bağlantı izni");
            return 1;
        }
    }
}
