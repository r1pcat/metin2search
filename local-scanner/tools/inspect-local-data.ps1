param([string]$Database = "$env:LOCALAPPDATA\Metin2Search\Data\market.sqlite", [switch]$EventsOnly, [string]$QuerySql)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class MarketSqlRead {
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db,int flags,IntPtr vfs);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int size,out IntPtr stmt,IntPtr tail);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_name(IntPtr stmt,int column);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt,int column);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
 public static List<Dictionary<string,string>> Query(string path,string sql) {
  IntPtr db,stmt=IntPtr.Zero;
  if(sqlite3_open_v2(path,out db,1,IntPtr.Zero)!=0) throw new Exception("Read-only database open failed");
  try {
   if(sqlite3_prepare_v2(db,sql,-1,out stmt,IntPtr.Zero)!=0) throw new Exception("Query prepare failed");
   var rows=new List<Dictionary<string,string>>(); int status;
   while((status=sqlite3_step(stmt))==100) {
    var row=new Dictionary<string,string>();
    for(int i=0;i<sqlite3_column_count(stmt);i++)row[Marshal.PtrToStringUTF8(sqlite3_column_name(stmt,i))]=Marshal.PtrToStringUTF8(sqlite3_column_text(stmt,i));
    rows.Add(row);
   }
   if(status!=101)throw new Exception("Query read failed");
   return rows;
  } finally { if(stmt!=IntPtr.Zero)sqlite3_finalize(stmt);sqlite3_close_v2(db); }
 }
}
'@
if ($QuerySql) {
 [MarketSqlRead]::Query($Database,$QuerySql) | ConvertTo-Json -Depth 10
 exit
}
if ($EventsOnly) {
 [MarketSqlRead]::Query($Database,'SELECT occurred,kind,message FROM events ORDER BY id DESC LIMIT 20') | ConvertTo-Json -Depth 10
 exit
}
$data = @{
 captures = [MarketSqlRead]::Query($Database,'SELECT id,source,observed,shop,item_count,slot_count,metadata_json FROM captures ORDER BY observed DESC LIMIT 5')
 items = [MarketSqlRead]::Query($Database,'SELECT o.capture_id,o.tab,o.slot,o.vnum,o.count,hex(b.bytes) AS raw_hex FROM observations o JOIN raw_blobs b ON b.hash=o.raw_hash WHERE o.vnum<>0 ORDER BY o.rowid DESC LIMIT 12')
 events = [MarketSqlRead]::Query($Database,'SELECT occurred,kind,message FROM events ORDER BY id DESC LIMIT 8')
}
$data | ConvertTo-Json -Depth 10
