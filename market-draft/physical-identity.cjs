const REASON='SupersededByRelist';
const evidence=x=>{
 const raw=x?.IdentityEvidence?.Raw05,count=Number(x?.Count||1);
 return Number.isSafeInteger(raw)&&raw>0&&Number.isSafeInteger(x?.Vnum)&&x.Vnum>0&&count===1?{raw05:raw,vnum:x.Vnum,count}:null;
};
function ensure(db){
 db.exec('CREATE TABLE IF NOT EXISTS item_raw05(listing_key TEXT PRIMARY KEY,raw05 INTEGER NOT NULL,vnum INTEGER NOT NULL,count INTEGER NOT NULL,seen TEXT NOT NULL);CREATE INDEX IF NOT EXISTS ix_item_raw05 ON item_raw05(raw05,vnum);');
}
function retire(db,listingKey,at){
 const r=db.prepare("UPDATE listing_lifecycle SET is_active=0,removed_at=?,source_scope=? WHERE listing_key=? AND is_active=1").run(at,REASON,listingKey);
 return Number(r.changes)>0;
}
function observe(db,{listingKey,x,observed,isActive}){
 const e=evidence(x);if(!e)return {superseded:[]};
 const old=db.prepare('SELECT seen FROM item_raw05 WHERE listing_key=?').get(listingKey);
 db.prepare('INSERT INTO item_raw05 VALUES(?,?,?,?,?) ON CONFLICT(listing_key) DO UPDATE SET raw05=excluded.raw05,vnum=excluded.vnum,count=excluded.count,seen=MAX(item_raw05.seen,excluded.seen)').run(listingKey,e.raw05,e.vnum,e.count,observed);
 if(!isActive)return {superseded:[]};
 const others=db.prepare(`SELECT r.listing_key k FROM item_raw05 r JOIN listing_lifecycle l ON l.listing_key=r.listing_key
  WHERE r.raw05=? AND r.vnum=? AND r.count=1 AND r.listing_key<>? AND l.is_active=1 AND r.seen<?`).all(e.raw05,e.vnum,listingKey,observed).map(r=>r.k);
 const superseded=others.filter(k=>retire(db,k,observed));
 return {superseded,previous:old?.seen??null};
}
function groupKeys(db,listingKey){
 try{
  const me=db.prepare('SELECT raw05,vnum,count FROM item_raw05 WHERE listing_key=?').get(listingKey);
  if(!me||me.count!==1)return [listingKey];
  const keys=db.prepare('SELECT listing_key k FROM item_raw05 WHERE raw05=? AND vnum=? AND count=1 ORDER BY seen').all(me.raw05,me.vnum).map(r=>r.k);
  return keys.length?keys:[listingKey];
 }catch{return [listingKey]}
}
function backfill(db,unpack,{limit=20000}={}){
 ensure(db);
 if(db.prepare("SELECT value FROM meta WHERE key='physical_backfill_done'").get()?.value==='1')return {done:true,scanned:0,superseded:0};
 const rows=db.prepare('SELECT i.id,i.listing_key,i.observed,i.detail FROM items i WHERE i.listing_key NOT IN (SELECT listing_key FROM item_raw05) LIMIT ?').all(limit);
 let scanned=0;
 db.exec('BEGIN IMMEDIATE');
 try{
  const put=db.prepare('INSERT OR IGNORE INTO item_raw05 VALUES(?,?,?,?,?)'),skip=db.prepare('INSERT OR IGNORE INTO item_raw05 VALUES(?,0,0,0,?)');
  for(const r of rows){
   scanned++;let e=null;try{e=evidence(unpack(db,r.detail))}catch{}
   if(e)put.run(r.listing_key,e.raw05,e.vnum,e.count,r.observed);else skip.run(r.listing_key,r.observed);
  }
  db.exec('COMMIT');
 }catch(error){try{db.exec('ROLLBACK')}catch{}throw error}
 if(rows.length===limit)return {done:false,scanned,superseded:0};
 let superseded=0;
 db.exec('BEGIN IMMEDIATE');
 try{
  const groups=db.prepare(`SELECT r.raw05,r.vnum FROM item_raw05 r JOIN listing_lifecycle l ON l.listing_key=r.listing_key
   WHERE r.raw05>0 AND r.count=1 AND l.is_active=1 GROUP BY r.raw05,r.vnum HAVING COUNT(*)>1`).all();
  const members=db.prepare(`SELECT r.listing_key k,r.seen s FROM item_raw05 r JOIN listing_lifecycle l ON l.listing_key=r.listing_key
   WHERE r.raw05=? AND r.vnum=? AND r.count=1 AND l.is_active=1 ORDER BY r.seen DESC`);
  for(const g of groups){const list=members.all(g.raw05,g.vnum);for(const old of list.slice(1))if(retire(db,old.k,list[0].s))superseded++}
  db.prepare("INSERT OR REPLACE INTO meta VALUES('physical_backfill_done','1')").run();
  db.exec('COMMIT');
 }catch(error){try{db.exec('ROLLBACK')}catch{}throw error}
 return {done:true,scanned,superseded};
}
module.exports={REASON,evidence,ensure,observe,groupKeys,backfill};
