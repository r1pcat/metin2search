require('./test-cleanup.cjs');
const assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),zlib=require('node:zlib'),{spawnSync}=require('node:child_process'),{DatabaseSync}=require('node:sqlite');
const root=fs.mkdtempSync(path.join(os.tmpdir(),'m2-bk-')),live=path.join(root,'data','live'),out=path.join(root,'backups');fs.mkdirSync(live,{recursive:true});
const db=new DatabaseSync(path.join(live,'safir.sqlite'));db.exec('PRAGMA journal_mode=WAL;CREATE TABLE t(a INTEGER,b TEXT)');for(let i=0;i<2000;i++)db.prepare('INSERT INTO t VALUES(?,?)').run(i,'x'.repeat(50));
const run=()=>spawnSync(process.execPath,[path.join(__dirname,'backup-web.cjs')],{encoding:'utf8',env:{...process.env,DATA_ROOT:root,BACKUP_DIR:out,BACKUP_KEEP:'2'}});
let r=run();assert.equal(r.status,0,r.stderr+r.stdout);
const files=()=>fs.readdirSync(out).filter(n=>n.endsWith('.sqlite.br')).sort();
assert.equal(files().length,1);const name=files()[0];assert.ok(fs.existsSync(path.join(out,name+'.sha256')));assert.ok(!fs.readdirSync(out).some(n=>n.endsWith('.tmp')),'geçici dosya kalmaz');
const raw=zlib.brotliDecompressSync(fs.readFileSync(path.join(out,name)));fs.writeFileSync(path.join(root,'restored.sqlite'),raw);
const back=new DatabaseSync(path.join(root,'restored.sqlite'),{readOnly:true});assert.equal(back.prepare('SELECT COUNT(*) n FROM t').get().n,2000);assert.equal(back.prepare('PRAGMA integrity_check').get().integrity_check,'ok');back.close();
for(const stamp of ['20200101-0100','20200102-0100','20200103-0100']){fs.writeFileSync(path.join(out,`safir-${stamp}.sqlite.br`),'old');fs.writeFileSync(path.join(out,`safir-${stamp}.sqlite.br.sha256`),'x')}
r=run();assert.equal(r.status,0,r.stderr);assert.equal(files().length,2,'yalnız en yeni 2 yedek kalır');assert.deepEqual(files().map(n=>n.startsWith('safir-20200103')),[true,false],'en yeni gerçek yedek ve ondan önceki en yeni kalır');
assert.equal(fs.readdirSync(out).filter(n=>/safir-2020010[12]/.test(n)).length,0,'eski yedeklerin özet dosyaları da silinir');
const empty=fs.mkdtempSync(path.join(os.tmpdir(),'m2-bk0-'));r=spawnSync(process.execPath,[path.join(__dirname,'backup-web.cjs')],{encoding:'utf8',env:{...process.env,DATA_ROOT:empty,BACKUP_DIR:path.join(empty,'b')}});assert.notEqual(r.status,0);
db.close();for(const d of [root,empty])try{fs.rmSync(d,{recursive:true,force:true,maxRetries:3})}catch{}
console.log('Web backup: consistent packed copy, checksum, restore round trip, rotation passed.');
