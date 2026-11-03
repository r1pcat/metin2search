require('./test-cleanup.cjs');
const assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),{spawnSync}=require('node:child_process'),{DatabaseSync}=require('node:sqlite');
const work=fs.mkdtempSync(path.join(os.tmpdir(),'m2-boot-')),dataset='ds-bootstrap-1';
function scanner(file,{pending=0,revisions={'ch1:0:3:2':3}}={}){
 const db=new DatabaseSync(file);
 db.exec(`CREATE TABLE trade_metadata(key TEXT PRIMARY KEY,value TEXT);CREATE TABLE category_outbox(batch_id TEXT PRIMARY KEY,category_key TEXT,revision INTEGER,scan_id TEXT,completed_at TEXT,encoding TEXT,body_hash TEXT,body BLOB,acked_at TEXT);CREATE TABLE category_delivery_state(category_key TEXT PRIMARY KEY,revision INTEGER,acked_revision INTEGER);`);
 db.prepare("INSERT INTO trade_metadata VALUES('server','Safir'),('dataset_id',?)").run(dataset);
 for(const [k,v] of Object.entries(revisions))db.prepare('INSERT INTO category_delivery_state VALUES(?,?,?)').run(k,v,v);
 for(let i=0;i<pending;i++)db.prepare("INSERT INTO category_outbox VALUES(?,?,?,?,?,?,?,?,NULL)").run('b'+i,'ch1:0:3:2',10+i,'s'+i,'t','br','h',Buffer.from('x'));
 db.close();
}
function web(file,{ds=dataset,received=3}={}){
 const db=new DatabaseSync(file);
 db.exec(`CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT);CREATE TABLE category_received(dataset TEXT,category_key TEXT,revision INTEGER,batch_id TEXT,body_hash TEXT,completed_at TEXT,PRIMARY KEY(dataset,category_key,revision));
  CREATE TABLE items(id TEXT PRIMARY KEY,listing_key TEXT,name TEXT);CREATE TABLE listing_lifecycle(listing_key TEXT PRIMARY KEY,is_active INTEGER);CREATE TABLE observations(feature_key TEXT,observed TEXT);
  CREATE VIEW active_items AS SELECT i.* FROM items i JOIN listing_lifecycle l ON l.listing_key=i.listing_key WHERE l.is_active=1;`);
 db.prepare("INSERT INTO meta VALUES('source_dataset',?)").run(ds);
 for(let r=1;r<=received;r++)db.prepare("INSERT INTO category_received VALUES(?,?,?,?,?,?)").run(dataset,'ch1:0:3:2',r,'b'+r,'h','t');
 for(let i=0;i<500;i++){db.prepare('INSERT INTO items VALUES(?,?,?)').run('i'+i,'k'+i,'Eşya '+i);db.prepare('INSERT INTO listing_lifecycle VALUES(?,?)').run('k'+i,i%2);db.prepare('INSERT INTO observations VALUES(?,?)').run('f'+i,'2026-10-17')}
 db.close();
}
const run=(script,...a)=>spawnSync(process.execPath,[path.join(__dirname,script),...a],{encoding:'utf8'});
const src=path.join(work,'market.sqlite'),live=path.join(work,'live.sqlite'),out=path.join(work,'out');

scanner(src);web(live);
let r=run('make-bootstrap.cjs','--server','safir','--source',src,'--web',live,'--out',out);
assert.equal(r.status,0,r.stderr+r.stdout);
const bundle=path.join(out,fs.readdirSync(out)[0]),manifest=JSON.parse(fs.readFileSync(path.join(bundle,'manifest.json'),'utf8'));
assert.deepEqual([manifest.serverId,manifest.dataset,manifest.counts.items,manifest.counts.active,manifest.counts.observations,manifest.counts.categories],['safir',dataset,500,250,500,1]);
assert.ok(fs.existsSync(path.join(bundle,'web.sqlite.br'))&&!fs.existsSync(path.join(bundle,'web.sqlite.tmp')),'geçici açık dosya bırakılmaz');

const target=path.join(work,'server-live');
r=run('restore-bootstrap.cjs','--bundle',bundle,'--target',target);assert.equal(r.status,0,r.stderr+r.stdout);
const restored=new DatabaseSync(path.join(target,'safir.sqlite'),{readOnly:true});
assert.equal(restored.prepare('SELECT COUNT(*) n FROM items').get().n,500);assert.equal(restored.prepare('SELECT COUNT(*) n FROM active_items').get().n,250);assert.equal(restored.prepare("SELECT value FROM meta WHERE key='source_dataset'").get().value,dataset);restored.close();
r=run('restore-bootstrap.cjs','--bundle',bundle,'--target',target);assert.notEqual(r.status,0,'mevcut veritabanının üzerine yazılmaz');assert.match(r.stderr,/zaten var/);
r=run('restore-bootstrap.cjs','--bundle',bundle,'--target',target,'--force');assert.equal(r.status,0,r.stderr);assert.ok(fs.existsSync(path.join(target,'safir.sqlite.before-restore')),'--force eski dosyayı kenara alır');

const bad=path.join(work,'bad');fs.cpSync(bundle,bad,{recursive:true});const bytes=fs.readFileSync(path.join(bad,'web.sqlite.br'));bytes[bytes.length>>1]^=0xff;fs.writeFileSync(path.join(bad,'web.sqlite.br'),bytes);
const target2=path.join(work,'server2');r=run('restore-bootstrap.cjs','--bundle',bad,'--target',target2);assert.notEqual(r.status,0);assert.match(r.stderr,/özet|boyut/);assert.ok(!fs.existsSync(path.join(target2,'safir.sqlite')),'bozuk paket hiçbir dosya bırakmaz');

const pendingSrc=path.join(work,'pending.sqlite');scanner(pendingSrc,{pending:2});
r=run('make-bootstrap.cjs','--server','safir','--source',pendingSrc,'--web',live,'--out',path.join(work,'o2'));assert.notEqual(r.status,0);assert.match(r.stderr,/onay bekleyen 2 paket/);
const behindWeb=path.join(work,'behind.sqlite');web(behindWeb,{received:2});
r=run('make-bootstrap.cjs','--server','safir','--source',src,'--web',behindWeb,'--out',path.join(work,'o3'));assert.notEqual(r.status,0);assert.match(r.stderr,/gerisinde/);
const otherDs=path.join(work,'other.sqlite');web(otherDs,{ds:'baska'});
r=run('make-bootstrap.cjs','--server','safir','--source',src,'--web',otherDs,'--out',path.join(work,'o4'));assert.notEqual(r.status,0);assert.match(r.stderr,/Veri kümesi uyuşmuyor/);
r=run('make-bootstrap.cjs','--server','safir','--source',pendingSrc,'--web',live,'--out',path.join(work,'o5'),'--force');assert.equal(r.status,0,'--force bilerek tutarsız anlık görüntüye izin verir');
assert.equal(JSON.parse(fs.readFileSync(path.join(work,'o5',fs.readdirSync(path.join(work,'o5'))[0],'manifest.json'),'utf8')).forced,true);
r=run('make-bootstrap.cjs','--server','yok','--source',src,'--web',live);assert.notEqual(r.status,0);
try{fs.rmSync(work,{recursive:true,force:true,maxRetries:3})}catch{}
console.log('Bootstrap: snapshot, verified restore, overwrite/corruption/pending/behind/dataset refusals passed.');
