require('./test-cleanup.cjs');
const fs=require('fs'),path=require('path'),os=require('os'),assert=require('assert/strict'),{DatabaseSync}=require('node:sqlite');
const dir=fs.mkdtempSync(path.join(os.tmpdir(),'arch-')),cfg=path.join(dir,'c.json');fs.writeFileSync(cfg,JSON.stringify({enabled:true,storage:'local',localRoot:path.join(dir,'store')}));process.env.ARCHIVE_CONFIG=cfg;
const {readConfig,r2Store}=require('./archive-storage.cjs'),{exportSource,createArchiveReader}=require('./history-archive.cjs');
(async()=>{const c=readConfig(__dirname),store=r2Store(c,dir),root=path.join(__dirname,'..','scanner-data','Sunucular');
const f=fs.readdirSync(root,{withFileTypes:true}).filter(d=>d.isDirectory()).map(d=>path.join(root,d.name,'market.sqlite')).find(fs.existsSync);
const db=new DatabaseSync(f,{readOnly:true});db.exec('PRAGMA query_only=ON;PRAGMA busy_timeout=1000');
const r=await exportSource(db,'safir',store,{maxRows:300,batchSize:150});console.log(JSON.stringify(r).slice(0,200));
const reader=createArchiveReader(store,path.join(dir,'cache'),{maxBytes:50e6});const ids=db.prepare('SELECT id FROM compact_versions ORDER BY id LIMIT 300').all().map(x=>x.id);
const rows=await reader.getMany('safir',r.dataset,ids);assert.equal(rows.size,300);
for(const id of ids.slice(0,50)){const row=db.prepare('SELECT v.key,p.bytes FROM compact_versions v JOIN compact_payloads p ON p.hash=v.payload_hash WHERE v.id=?').get(id);assert.equal(rows.get(id).key,row.key);assert(Buffer.from(rows.get(id).bytes).equals(Buffer.from(row.bytes)))}
console.log('yerel depo: 300 kayıt yazıldı ve birebir okundu; dosya sayısı',fs.readdirSync(path.join(dir,'store'),{recursive:true}).length);reader.close();db.close();fs.rmSync(dir,{recursive:true,force:true})})().catch(e=>{console.error(e);process.exitCode=1});

