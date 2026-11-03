const DEFAULT_HOURS=24,BATCH=5000;

function retentionHours(env=process.env){
 const raw=env.RETENTION_REMOVED_HOURS;if(raw===undefined||raw==='')return DEFAULT_HOURS;
 if(String(raw).toLowerCase()==='off')return 0;const n=Number(raw);return Number.isFinite(n)&&n>=0?n:DEFAULT_HOURS;
}

function pruneRemoved(db,{hours=retentionHours(),now=Date.now(),maxBatches=40}={}){
 if(!hours)return 0;
 if(db.prepare("SELECT value FROM meta WHERE key='lifecycle_managed'").get()?.value!=='1')return 0;
 const cutoff=new Date(now-hours*3600*1000).toISOString();
 const pick=db.prepare(`SELECT i.id FROM items i LEFT JOIN listing_lifecycle l ON l.listing_key=i.listing_key
  WHERE l.is_active IS NOT 1 AND COALESCE(l.removed_at,i.observed)<? LIMIT ?`);
 const del=['DELETE FROM item_bonuses WHERE item_id=?','DELETE FROM web_purchase_status WHERE item_id=?','DELETE FROM web_shop_positions WHERE item_id=?','DELETE FROM items WHERE id=?']
  .map(sql=>{try{return db.prepare(sql)}catch{return null}}).filter(Boolean);
 let total=0;
 for(let batch=0;batch<maxBatches;batch++){
  const ids=pick.all(cutoff,BATCH).map(r=>r.id);if(!ids.length)break;
  db.exec('BEGIN IMMEDIATE');
  try{for(const id of ids)for(const statement of del)statement.run(id);db.exec('COMMIT')}
  catch(error){try{db.exec('ROLLBACK')}catch{}throw error}
  total+=ids.length;if(ids.length<BATCH)break;
 }
 return total;
}

const DEFAULT_HISTORY_DAYS=30;
function historyDays(env=process.env){
 const raw=env.HISTORY_DAYS;if(raw===undefined||raw==='')return DEFAULT_HISTORY_DAYS;
 if(String(raw).toLowerCase()==='off')return 0;const n=Number(raw);return Number.isFinite(n)&&n>=1?Math.floor(n):DEFAULT_HISTORY_DAYS;
}
function ensurePriceDaily(db){
 db.exec('CREATE TABLE IF NOT EXISTS price_daily(feature_key TEXT NOT NULL,day TEXT NOT NULL,n INTEGER NOT NULL,sellers INTEGER NOT NULL,min_unit REAL NOT NULL,median_unit REAL NOT NULL,max_unit REAL NOT NULL,PRIMARY KEY(feature_key,day)) WITHOUT ROWID');
}
const UNIT='(won*100000000.0+yang)/MAX(quantity,1)';
function pruneHistory(db,{days=historyDays(),now=Date.now(),maxDays=10}={}){
 const result={days:0,deleted:0,summaries:0,minRetainedVersion:null};
 if(!days)return result;
 ensurePriceDaily(db);
 const cutoffDay=new Date(now-days*86400000).toISOString().slice(0,10);
 const oldDays=db.prepare('SELECT substr(observed,1,10) d FROM observations WHERE observed<? GROUP BY d ORDER BY d LIMIT ?').all(cutoffDay,maxDays).map(r=>r.d);
 const rows=db.prepare(`SELECT feature_key f,${UNIT} u,seller s FROM observations WHERE observed>=? AND observed<? ORDER BY feature_key,u`);
 const put=db.prepare('INSERT OR REPLACE INTO price_daily VALUES(?,?,?,?,?,?,?)'),remove=db.prepare('DELETE FROM observations WHERE observed>=? AND observed<?');
 for(const day of oldDays){
  const next=new Date(Date.parse(day+'T00:00:00Z')+86400000).toISOString().slice(0,10);
  db.exec('BEGIN IMMEDIATE');
  try{
   let current=null,values=[],sellers=new Set();
   const flush=()=>{if(current!==null&&values.length>=2){put.run(current,day,values.length,sellers.size,values[0],values.length%2?values[(values.length-1)/2]:(values[values.length/2-1]+values[values.length/2])/2,values.at(-1));result.summaries++}};
   for(const r of rows.iterate(day,next)){if(r.f!==current){flush();current=r.f;values=[];sellers=new Set()}values.push(Number(r.u));sellers.add(r.s)}
   flush();
   result.deleted+=Number(remove.run(day,next).changes);db.exec('COMMIT');result.days++;
  }catch(error){try{db.exec('ROLLBACK')}catch{}throw error}
 }
 try{db.prepare("DELETE FROM item_raw05 WHERE seen<? AND listing_key NOT IN (SELECT listing_key FROM listing_lifecycle WHERE is_active=1)").run(cutoffDay)}catch{ }
 result.minRetainedVersion=db.prepare('SELECT MIN(source_version) v FROM observations').get().v??null;
 return result;
}
module.exports={pruneRemoved,retentionHours,DEFAULT_HOURS,pruneHistory,historyDays,ensurePriceDaily,DEFAULT_HISTORY_DAYS};
