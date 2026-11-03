const assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite'),{pruneRemoved,retentionHours}=require('./retention.cjs');
function fresh(managed=true){
 const db=new DatabaseSync(':memory:');
 db.exec(`CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT);
  CREATE TABLE items(id TEXT PRIMARY KEY,listing_key TEXT UNIQUE,observed TEXT,detail TEXT);
  CREATE TABLE web_search_items(id TEXT,listing_key TEXT);
  CREATE TRIGGER ws_item_delete AFTER DELETE ON items BEGIN DELETE FROM web_search_items WHERE id=OLD.id;END;
  CREATE TABLE listing_lifecycle(listing_key TEXT PRIMARY KEY,is_active INTEGER NOT NULL,last_seen TEXT,removed_at TEXT,source_scope TEXT,revision INTEGER);
  CREATE TABLE item_bonuses(item_id TEXT,type INTEGER,slot INTEGER,value REAL);
  CREATE TABLE web_purchase_status(item_id TEXT PRIMARY KEY,payload_json TEXT);
  CREATE TABLE web_shop_positions(item_id TEXT PRIMARY KEY,position INTEGER,source_version INTEGER);
  CREATE TABLE observations(listing_key TEXT,observed TEXT,yang INTEGER);
  CREATE VIEW active_items AS SELECT i.* FROM items i LEFT JOIN listing_lifecycle l ON l.listing_key=i.listing_key WHERE CASE WHEN (SELECT value FROM meta WHERE key='lifecycle_managed')='1' THEN l.is_active=1 ELSE COALESCE(l.is_active,1)=1 END;`);
 if(managed)db.prepare("INSERT INTO meta VALUES('lifecycle_managed','1')").run();return db;
}
const now=Date.parse('2026-10-18T12:00:00Z'),ago=h=>new Date(now-h*3600e3).toISOString();
function add(db,id,{active=false,removedHoursAgo=null,observedHoursAgo=1,lifecycle=true}={}){
 db.prepare('INSERT INTO items VALUES(?,?,?,?)').run(id,'key-'+id,ago(observedHoursAgo),'{}');db.prepare('INSERT INTO web_search_items VALUES(?,?)').run(id,'key-'+id);
 if(lifecycle)db.prepare('INSERT INTO listing_lifecycle VALUES(?,?,?,?,?,?)').run('key-'+id,active?1:0,ago(observedHoursAgo),active?null:ago(removedHoursAgo??observedHoursAgo),'s',1);
 db.prepare('INSERT INTO item_bonuses VALUES(?,?,?,?)').run(id,71,0,5);db.prepare('INSERT INTO web_purchase_status VALUES(?,?)').run(id,'{}');db.prepare('INSERT INTO web_shop_positions VALUES(?,?,?)').run(id,1,1);
 db.prepare('INSERT INTO observations VALUES(?,?,?)').run('key-'+id,ago(observedHoursAgo),100);
}
const count=(db,sql)=>db.prepare(sql).get().n;

{
 const db=fresh(false);add(db,'x',{removedHoursAgo:100});assert.equal(pruneRemoved(db,{hours:24,now}),0);assert.equal(count(db,'SELECT COUNT(*) n FROM items'),1);
}
{
 const db=fresh();
 add(db,'active',{active:true,observedHoursAgo:500});
 add(db,'sold-old',{removedHoursAgo:25});
 add(db,'sold-new',{removedHoursAgo:2});
 add(db,'nolife-old',{lifecycle:false,observedHoursAgo:72});
 add(db,'nolife-new',{lifecycle:false,observedHoursAgo:1});
 assert.equal(pruneRemoved(db,{hours:24,now}),2,'yalnız 24 saati geçen kaldırılmış ilanlar silinir');
 assert.deepEqual(db.prepare('SELECT id FROM items ORDER BY id').all().map(r=>r.id),['active','nolife-new','sold-new']);
 for(const t of ['item_bonuses','web_purchase_status','web_shop_positions'])assert.equal(count(db,`SELECT COUNT(*) n FROM ${t} WHERE item_id IN ('sold-old','nolife-old')`),0,t+' temizlendi');
 assert.equal(count(db,"SELECT COUNT(*) n FROM web_search_items WHERE id IN ('sold-old','nolife-old')"),0,'arama satırı tetikleyiciyle silindi');
 assert.equal(count(db,"SELECT COUNT(*) n FROM observations WHERE listing_key IN ('key-sold-old','key-nolife-old')"),2,'fiyat geçmişi (observations) korunur');
 assert.equal(count(db,"SELECT COUNT(*) n FROM listing_lifecycle WHERE listing_key='key-sold-old'"),1,'yaşam döngüsü kaydı korunur (eski sürüm/olay saati)');
 assert.equal(count(db,'SELECT COUNT(*) n FROM active_items'),1,'aktif ilan sayısı değişmez');
 assert.equal(pruneRemoved(db,{hours:24,now}),0,'ikinci çalıştırma yeni bir şey silmez (idempotent)');
 db.prepare('INSERT INTO items VALUES(?,?,?,?)').run('sold-old','key-sold-old',ago(0),'{}');db.prepare("UPDATE listing_lifecycle SET is_active=1,removed_at=NULL WHERE listing_key='key-sold-old'").run();
 assert.equal(count(db,'SELECT COUNT(*) n FROM active_items'),2,'silinen ilan yeniden görülünce geri gelir');
 assert.equal(count(db,"SELECT COUNT(*) n FROM observations WHERE listing_key='key-sold-old'"),1,'geçmiş noktası yeniden görülen ilanda hâlâ var');
}
{
 const db=fresh();add(db,'sold',{removedHoursAgo:500});assert.equal(pruneRemoved(db,{hours:0,now}),0);assert.equal(count(db,'SELECT COUNT(*) n FROM items'),1);
 assert.equal(retentionHours({}),24);assert.equal(retentionHours({RETENTION_REMOVED_HOURS:'off'}),0);assert.equal(retentionHours({RETENTION_REMOVED_HOURS:'48'}),48);assert.equal(retentionHours({RETENTION_REMOVED_HOURS:'abc'}),24);assert.equal(retentionHours({RETENTION_REMOVED_HOURS:'-3'}),24);
}
{
 const db=fresh();db.exec('BEGIN');for(let i=0;i<12000;i++)add(db,'b'+i,{removedHoursAgo:100});db.exec('COMMIT');
 assert.equal(pruneRemoved(db,{hours:24,now,maxBatches:2}),10000);assert.equal(count(db,'SELECT COUNT(*) n FROM items'),2000);
 assert.equal(pruneRemoved(db,{hours:24,now,maxBatches:2}),2000);assert.equal(count(db,'SELECT COUNT(*) n FROM items'),0);assert.equal(count(db,'SELECT COUNT(*) n FROM observations'),12000);
}
console.log('Retention: 24 h window, active/unmanaged untouched, price points + lifecycle kept, re-listing restores, batches and switch-off passed.');
