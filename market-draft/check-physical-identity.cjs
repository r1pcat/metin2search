const assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite'),P=require('./physical-identity.cjs');
function fresh(){const db=new DatabaseSync(':memory:');
 db.exec(`CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT);
  CREATE TABLE listing_lifecycle(listing_key TEXT PRIMARY KEY,is_active INTEGER NOT NULL,last_seen TEXT,removed_at TEXT,source_scope TEXT,revision INTEGER);
  CREATE TABLE items(id TEXT PRIMARY KEY,listing_key TEXT UNIQUE,observed TEXT,detail TEXT);`);P.ensure(db);return db}
const x=(raw05,vnum=1000,count=1)=>({Vnum:vnum,Count:count,IdentityEvidence:{Raw05:raw05}});
const life=(db,key,active=1)=>db.prepare('INSERT OR REPLACE INTO listing_lifecycle VALUES(?,?,?,?,?,?)').run(key,active,'2026-10-17T10:00:00Z',null,'s',1);
const active=(db,key)=>db.prepare('SELECT is_active a FROM listing_lifecycle WHERE listing_key=?').get(key).a;
const t=h=>`2026-10-17T${String(h).padStart(2,'0')}:00:00.0000000+00:00`;

{
 const db=fresh();life(db,'A:k:0');life(db,'B:k:0');
 assert.deepEqual(P.observe(db,{listingKey:'A:k:0',x:x(930478897),observed:t(10),isActive:true}).superseded,[],'ilk ilan: kaldırılacak bir şey yok');
 const out=P.observe(db,{listingKey:'B:k:0',x:x(930478897),observed:t(11),isActive:true});
 assert.deepEqual(out.superseded,['A:k:0'],'aynı eşya başka karakterde görülünce eski ilan kalkar');
 assert.equal(active(db,'A:k:0'),0);assert.equal(active(db,'B:k:0'),1);
 const row=db.prepare("SELECT removed_at,source_scope FROM listing_lifecycle WHERE listing_key='A:k:0'").get();assert.equal(row.source_scope,P.REASON);assert.equal(row.removed_at,t(11));
 assert.deepEqual(P.groupKeys(db,'B:k:0'),['A:k:0','B:k:0'],'geçmiş iki ilanı birleştirir, eski olan önce');
 assert.deepEqual(P.groupKeys(db,'A:k:0'),['A:k:0','B:k:0']);
 assert.deepEqual(P.observe(db,{listingKey:'B:k:0',x:x(930478897),observed:t(12),isActive:true}).superseded,[]);
}
{
 const db=fresh();life(db,'A:k:0');life(db,'B:k:0');
 P.observe(db,{listingKey:'B:k:0',x:x(5),observed:t(11),isActive:true});
 assert.deepEqual(P.observe(db,{listingKey:'A:k:0',x:x(5),observed:t(10),isActive:true}).superseded,[],'daha eski gözlem yenisini kaldıramaz');
 assert.equal(active(db,'B:k:0'),1);
}
{
 const db=fresh();for(const k of ['S1','S2','V1','V2','N1','N2'])life(db,k);
 P.observe(db,{listingKey:'S1',x:x(7,1000,20),observed:t(10),isActive:true});assert.deepEqual(P.observe(db,{listingKey:'S2',x:x(7,1000,20),observed:t(11),isActive:true}).superseded,[],'yığınlar (adet>1) eşleştirilmez');
 P.observe(db,{listingKey:'V1',x:x(8,1000),observed:t(10),isActive:true});assert.deepEqual(P.observe(db,{listingKey:'V2',x:x(8,2000),observed:t(11),isActive:true}).superseded,[],'farklı vnum aynı sayıyı paylaşsa da eşleşmez');
 P.observe(db,{listingKey:'N1',x:x(9),observed:t(10),isActive:true});assert.deepEqual(P.observe(db,{listingKey:'N2',x:x(9),observed:t(11),isActive:false}).superseded,[],'kaldırıldı olarak gelen gözlem eskiyi kaldırmaz');
 assert.equal(active(db,'N1'),1);
 assert.deepEqual(P.observe(db,{listingKey:'N3',x:{Vnum:1000,Count:1,IdentityEvidence:{}},observed:t(12),isActive:true}).superseded,[],'sayı yoksa eşleştirme yok');
}
{
 const db=fresh();
 const add=(key,raw,h,vnum=1000,count=1,activeFlag=1)=>{db.prepare('INSERT INTO items VALUES(?,?,?,?)').run('id-'+key,key,t(h),JSON.stringify(x(raw,vnum,count)));life(db,key,activeFlag)};
 add('A:1',111,10);add('B:1',111,12);add('C:1',111,11);
 add('D:1',222,10);add('E:1',222,11,1000,1,0);
 add('F:1',333,10,1000,20);add('G:1',333,11,1000,20);
 const unpack=(_,detail)=>JSON.parse(detail);
 let r=P.backfill(db,unpack,{limit:4});assert.equal(r.done,false);assert.equal(r.scanned,4);
 r=P.backfill(db,unpack,{limit:4});assert.equal(r.done,true);assert.equal(r.superseded,2,'iki eski kopya kaldırıldı');
 assert.deepEqual(['A:1','B:1','C:1','D:1','F:1','G:1'].map(k=>active(db,k)),[0,1,0,1,1,1]);
 assert.equal(P.backfill(db,unpack).done,true);assert.equal(P.backfill(db,unpack).scanned,0,'bir kez çalışır');
}
console.log('Physical identity: re-list retires the old listing, history groups across sellers, stacks/other vnums/stale sightings protected, backfill resolves duplicates passed.');
