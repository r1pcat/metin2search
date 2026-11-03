using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Metin2Search.Local;

public sealed partial class LocalStore
{
    private sealed record StorageColumn(string Name,string Type,bool Intern=false);
    public void OptimizeMetadata()
    {
        lock(this){
            if(Query("SELECT value FROM trade_metadata WHERE key='storage_layout'").FirstOrDefault()?[0] as string=="interned-v1")return;
            Exec("BEGIN IMMEDIATE");
            try{
                Exec("CREATE TABLE IF NOT EXISTS storage_strings(id INTEGER PRIMARY KEY,value TEXT NOT NULL UNIQUE)");
                NormalizeTable("compact_versions",[
                    new("id","INTEGER PRIMARY KEY"),new("key","INTEGER NOT NULL",true),new("observed","TEXT NOT NULL"),new("version_hash","TEXT NOT NULL"),new("payload_hash","TEXT NOT NULL REFERENCES compact_payloads(hash)"),
                    new("seller","INTEGER NOT NULL",true),new("name","INTEGER NOT NULL",true),new("vnum","INTEGER NOT NULL"),new("search_text","INTEGER NOT NULL",true),new("reason","INTEGER NOT NULL",true)], ["id"]);
                NormalizeTable("compact_pages",[new("id","TEXT PRIMARY KEY"),new("observed","TEXT NOT NULL"),new("category","INTEGER NOT NULL",true),new("page","INTEGER NOT NULL"),new("changed","INTEGER NOT NULL"),new("unchanged","INTEGER NOT NULL"),new("error","TEXT")],["id"]);
                NormalizeTable("scope_memberships",[new("scope_id","INTEGER NOT NULL",true),new("key","INTEGER NOT NULL",true),new("last_seen","TEXT NOT NULL"),new("last_run","INTEGER NOT NULL",true),new("misses","INTEGER NOT NULL"),new("is_active","INTEGER NOT NULL")],["scope_id","key"]);
                Exec("CREATE INDEX ix_compact_key ON storage_compact_versions(key); CREATE INDEX ix_compact_vnum ON storage_compact_versions(vnum); CREATE INDEX ix_memberships_key ON storage_scope_memberships(key,is_active); CREATE INDEX ix_storage_pages_time ON storage_compact_pages(observed);");
                Execute("INSERT OR REPLACE INTO trade_metadata VALUES('storage_layout','interned-v1')");
                Exec("COMMIT");
            }catch{try{Exec("ROLLBACK");}catch{}throw;}
            Exec("PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
        }
    }
    private void NormalizeTable(string name,StorageColumn[] cols,string[] primary)
    {
        var physical="storage_"+name;
        foreach(var c in cols.Where(c=>c.Intern))Exec($"INSERT OR IGNORE INTO storage_strings(value) SELECT DISTINCT {c.Name} FROM {name}");
        var definition=string.Join(",",cols.Select(c=>c.Name+" "+c.Type));
        if(primary.Length>1)definition+=",PRIMARY KEY("+string.Join(",",primary)+")";
        Exec($"CREATE TABLE {physical}({definition})");
        string Intern(string prefix,StorageColumn c)=>c.Intern?$"(SELECT id FROM storage_strings WHERE value={prefix}{c.Name})":prefix+c.Name;
        Exec($"INSERT INTO {physical} SELECT {string.Join(",",cols.Select(c=>Intern(name+".",c)))} FROM {name}");
        Exec($"DROP TABLE {name}");
        var interned=cols.Where(c=>c.Intern).ToArray();
        string Field(StorageColumn c)=>c.Intern?"s_"+c.Name+".value":"n."+c.Name;
        Exec($"CREATE VIEW {name} AS SELECT {string.Join(",",cols.Select(c=>Field(c)+" AS "+c.Name))} FROM {physical} n "+string.Join(" ",interned.Select(c=>$"JOIN storage_strings s_{c.Name} ON s_{c.Name}.id=n.{c.Name}")));
        var seed=string.Join(";",interned.Select(c=>$"INSERT INTO storage_strings(value) SELECT NEW.{c.Name} WHERE NOT EXISTS(SELECT 1 FROM storage_strings WHERE value=NEW.{c.Name})"));
        var values=string.Join(",",cols.Select(c=>Intern("NEW.",c)));
        Exec($"CREATE TRIGGER insert_{name} INSTEAD OF INSERT ON {name} BEGIN {seed}; INSERT INTO {physical} VALUES({values}); END");
        string MatchOld()=>string.Join(" AND ",primary.Select(p=>p+"="+Intern("OLD.",cols.Single(c=>c.Name==p))));
        Exec($"CREATE TRIGGER update_{name} INSTEAD OF UPDATE ON {name} BEGIN {seed}; UPDATE {physical} SET {string.Join(",",cols.Select(c=>c.Name+"="+Intern("NEW.",c)))} WHERE {MatchOld()}; END");
        Exec($"CREATE TRIGGER delete_{name} INSTEAD OF DELETE ON {name} BEGIN DELETE FROM {physical} WHERE {MatchOld()}; END");
    }
    public static object CreateOptimizedCopy(string sourceFile,string destinationDirectory)
    {
        var sourcePath=Path.GetFullPath(sourceFile);var destination=Path.Combine(Path.GetFullPath(destinationDirectory),"market.sqlite");
        if(sourcePath.Equals(destination,StringComparison.OrdinalIgnoreCase)||File.Exists(destination))throw new IOException("Ayrı ve yeni bir hedef gerekli; kaynak veya mevcut dosya üzerine yazılmaz.");
        Directory.CreateDirectory(destinationDirectory);var timer=Stopwatch.StartNew();
        using var source=OpenReadOnly(sourcePath);source.Exec("BEGIN");source.Query("SELECT COUNT(*) FROM compact_versions");
        IntPtr target=IntPtr.Zero;
        try{
            if(Native.sqlite3_open_v2(destination,out target,2|4|0x10000,IntPtr.Zero)!=0)throw new IOException("Kopya veritabanı açılamadı.");
            var backup=Native.sqlite3_backup_init(target,"main",source.db,"main");if(backup==IntPtr.Zero)throw new IOException("Tutarlı yedek başlatılamadı.");
            int result;
            try{result=Native.sqlite3_backup_step(backup,-1);}finally{Native.sqlite3_backup_finish(backup);}
            if(result!=101)throw new IOException("Tutarlı yedek tamamlanamadı: "+result);
        }finally{if(target!=IntPtr.Zero)Native.sqlite3_close_v2(target);}
        var baselineBytes=new FileInfo(destination).Length;
        using var copy=new LocalStore(destinationDirectory,false,false);copy.OptimizeMetadata();
        var checks=new List<object>();
        foreach(var name in new[]{"compact_versions","compact_payloads","compact_listings","compact_pages","scope_memberships","scope_snapshots","lifecycle_runs","listing_lifecycle","lifecycle_events","version_identity"}){
            var order=name switch{"compact_versions"=>"id","compact_payloads"=>"hash","compact_listings"=>"key","compact_pages"=>"id","scope_memberships"=>"scope_id,key","scope_snapshots"=>"run_id,scope_id","lifecycle_runs"=>"run_id","listing_lifecycle"=>"key","lifecycle_events"=>"id",_=>"version_id"};
            var left=DigestTable(source,name,order);var right=DigestTable(copy,name,order);
            if(left!=right)throw new InvalidDataException("Kopya eşdeğerliği başarısız: "+name);
            checks.Add(new{table=name,rows=left.Rows,sha256=left.Hash});
        }
        if(copy.Integrity!="ok"||copy.Query("PRAGMA foreign_key_check").Count!=0)throw new InvalidDataException("Kopya bütünlüğü doğrulanamadı.");
        copy.Exec("PRAGMA wal_checkpoint(TRUNCATE)");
        var afterBytes=new FileInfo(destination).Length;
        var manifest=new{source=sourcePath,destination,snapshotBytes=baselineBytes,optimizedBytes=afterBytes,savedBytes=baselineBytes-afterBytes,savedPercent=100d*(baselineBytes-afterBytes)/Math.Max(1,baselineBytes),seconds=timer.Elapsed.TotalSeconds,sourceReadOnly=true,sourcePreserved=true,format="V1 Brotli payloads; compatibility SQL views; unchanged source_version IDs",checks};
        File.WriteAllText(Path.Combine(destinationDirectory,"optimization-manifest.json"),JsonData.Encode(manifest));
        return manifest;
    }
    public static object OptimizeAndInstall(string sourceFile,string destinationDirectory)
    {
        var sourcePath=Path.GetFullPath(sourceFile);var folder=Path.GetDirectoryName(sourcePath)!;
        var archive=sourcePath+".pre-v060-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
        if(Path.GetDirectoryName(archive)!=folder)throw new InvalidDataException("Arşiv kaynak klasöründe olmalı.");
        var source=new LocalStore(folder,false);using var lease=source.AcquireScannerWriter();
        object manifest;
        try{
            var checkpoint=source.Query("PRAGMA wal_checkpoint(TRUNCATE)");
            if((long)checkpoint[0][0]! != 0)throw new IOException("Kaynak okuyucu/yazıcı WAL checkpoint'i engelliyor; kesim yapılmadı.");
            source.Exec("BEGIN IMMEDIATE");
            try{manifest=CreateOptimizedCopy(sourcePath,destinationDirectory);source.Exec("COMMIT");}
            catch{try{source.Exec("ROLLBACK");}catch{}throw;}
        }finally{source.Dispose();}
        var staged=Path.Combine(Path.GetFullPath(destinationDirectory),"market.sqlite");
        var moved=new List<(string From,string To)>();
        try{
            foreach(var suffix in new[]{"","-wal","-shm"}){
                var from=sourcePath+suffix;var to=archive+suffix;
                if(File.Exists(from)){File.Move(from,to);moved.Add((from,to));}
            }
            File.Move(staged,sourcePath);
        }catch{
            if(!File.Exists(sourcePath))foreach(var entry in moved.AsEnumerable().Reverse())if(File.Exists(entry.To)&&!File.Exists(entry.From))File.Move(entry.To,entry.From);
            throw;
        }
        var result=new{installedAt=DateTimeOffset.UtcNow,source=sourcePath,archive,manifest,sourcePreserved=true,payloadFormat="V1",sourceVersionIdsPreserved=true,importerMustReconnect=true};
        File.WriteAllText(Path.Combine(destinationDirectory,"cutover.json"),JsonData.Encode(result));
        return result;
    }
    private static (long Rows,string Hash) DigestTable(LocalStore database,string table,string order)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long rows=0;
        using var statement=database.Prepare($"SELECT * FROM {table} ORDER BY {order}",[]);int status;
        while((status=Native.sqlite3_step(statement.Pointer))==100){var row=ReadRow(statement.Pointer);var bytes=Encoding.UTF8.GetBytes(JsonData.Encode(row));hash.AppendData(BitConverter.GetBytes(bytes.Length));hash.AppendData(bytes);rows++;}
        if(status!=101)throw new IOException(database.Error());
        return(rows,Convert.ToHexString(hash.GetHashAndReset()));
    }
}
