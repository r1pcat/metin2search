using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Metin2Search.Local;

internal static class StoredStoragePassword
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes,bool protect)
    {
        var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;
        try{
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            var ok=protect?CryptProtectData(ref input,"Metin2Search storage password",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new CryptographicException("Depo şifresinin Windows koruması açılamadı; aynı Windows kullanıcısıyla tekrar girin.");
            var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }finally{
            for(var i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);
            Marshal.FreeHGlobal(input.Data);
            if(output.Data!=IntPtr.Zero){for(var i=0;i<output.Length;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}
        }
    }
    public static string Read(string path)
    {
        if(!File.Exists(path))return "";
        var bytes=Transform(File.ReadAllBytes(path),false);
        try{return Encoding.UTF8.GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static void Save(string path,string password)
    {
        if(password.Length==0){if(File.Exists(path))File.Delete(path);return;}
        var clear=Encoding.UTF8.GetBytes(password);byte[] cipher;
        try{cipher=Transform(clear,true);}finally{CryptographicOperations.ZeroMemory(clear);}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(cipher);file.Flush(true);}File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
