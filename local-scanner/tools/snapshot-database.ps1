param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Destination){throw 'Hedef mevcut; üzerine yazılmaz'}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReadOnlyMarketSnapshot {
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string p,out IntPtr d,int f,IntPtr v);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr d);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_backup_init(IntPtr dst,[MarshalAs(UnmanagedType.LPUTF8Str)] string dn,IntPtr src,[MarshalAs(UnmanagedType.LPUTF8Str)] string sn);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_backup_step(IntPtr b,int n);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_backup_finish(IntPtr b);
 public static void Copy(string source,string destination){IntPtr src=IntPtr.Zero,dst=IntPtr.Zero,b=IntPtr.Zero;
  try{if(sqlite3_open_v2(source,out src,1,IntPtr.Zero)!=0)throw new Exception("Kaynak readonly açılamadı");
   if(sqlite3_open_v2(destination,out dst,2|4,IntPtr.Zero)!=0)throw new Exception("Hedef açılamadı");
   b=sqlite3_backup_init(dst,"main",src,"main");if(b==IntPtr.Zero)throw new Exception("Snapshot başlatılamadı");
   var code=sqlite3_backup_step(b,-1);if(code!=101)throw new Exception("Snapshot başarısız: "+code);
   var finish=sqlite3_backup_finish(b);b=IntPtr.Zero;if(finish!=0)throw new Exception("Snapshot kapanamadı");
  }finally{if(b!=IntPtr.Zero)sqlite3_backup_finish(b);if(dst!=IntPtr.Zero)sqlite3_close_v2(dst);if(src!=IntPtr.Zero)sqlite3_close_v2(src);}
 }
}
'@
[ReadOnlyMarketSnapshot]::Copy((Resolve-Path -LiteralPath $Source).Path,[IO.Path]::GetFullPath($Destination))
Get-Item -LiteralPath $Destination | Select-Object FullName,Length
