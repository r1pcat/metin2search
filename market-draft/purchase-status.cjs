const MIN_FRESH_MS=30*60*1000;
const ANCHOR_RETRY_MS=15*60*1000;
const RESULTS=['Preparing','NotEnoughMoney','Unknown','Success','Available'];
function freshMs(db){const env=Number(process.env.PURCHASE_FRESH_MS);if(Number.isFinite(env)&&env>0)return env;
 let round=0;try{round=Number(db?.prepare("SELECT value FROM meta WHERE key='last_full_round_ms'").get()?.value)||0}catch{}
 return Math.max(MIN_FRESH_MS,3*round)}
function ensure(db){db.exec(`CREATE TABLE IF NOT EXISTS web_purchase_checks(id INTEGER PRIMARY KEY,payload_json TEXT NOT NULL);CREATE TABLE IF NOT EXISTS web_purchase_status(item_id TEXT PRIMARY KEY,payload_json TEXT NOT NULL);
 CREATE TABLE IF NOT EXISTS web_purchase_shops(server TEXT NOT NULL,session TEXT NOT NULL,shop_vid INTEGER NOT NULL,seller TEXT NOT NULL,checked_at INTEGER NOT NULL,expires_at INTEGER NOT NULL,payload_json TEXT NOT NULL,PRIMARY KEY(server,session,shop_vid,seller));
 CREATE INDEX IF NOT EXISTS web_purchase_shops_expiry ON web_purchase_shops(expires_at);`);
 if(db.prepare("SELECT 1 FROM sqlite_master WHERE name='meta'").get()&&db.prepare("SELECT value FROM meta WHERE key='purchase_shop_scope_version'").get()?.value!=='3'){
  db.prepare("DELETE FROM meta WHERE key='purchase_status_revision'").run();
  db.prepare("INSERT OR REPLACE INTO meta VALUES('purchase_shop_scope_version','3')").run();
 }
}
function validate(p,server,now=Date.now()){
 if(!p||['session','requestId','listingKey','seller'].some(k=>typeof p[k]!=='string'||!p[k]||p[k].length>1024))return false;
 if(p.schemaVersion!==1||p.server!==server||!p.session||!p.requestId||!p.listingKey||!p.seller||!Number.isSafeInteger(p.shopVid)||p.shopVid<=0)return false;
 const time=Date.parse(p.checkedAt);return Number.isFinite(time)&&time<=now+5000&&RESULTS.includes(p.result)&&['listing','shop'].includes(p.scope)&&typeof p.reason==='string'&&p.reason.length<=2000&&typeof p.evidenceSource==='string'&&!!p.evidenceSource;
}
function sync(state){const {source,db}=state;ensure(db);if(!source.prepare("SELECT 1 FROM sqlite_master WHERE name='purchase_checks' AND type='table'").get())return 0;
 const now=Date.now(),revision=Number(state.revision??db.prepare("SELECT value FROM meta WHERE key='revision'").get()?.value??0);
 const reapply=db.prepare("SELECT value FROM meta WHERE key='purchase_status_revision'").get()?.value!==String(revision);
 const cursor=Number(db.prepare("SELECT value FROM meta WHERE key='purchase_check_cursor'").get()?.value||0),rows=source.prepare('SELECT id,payload_json FROM purchase_checks WHERE id>? ORDER BY id LIMIT 500').all(cursor);if(!rows.length&&!reapply)return 0;
 const hasPresence=!!source.prepare("SELECT 1 FROM sqlite_master WHERE name='compact_presence'").get(),{unpackItem}=require('./packed-details.cjs');
 const hasLifecycle=!!db.prepare("SELECT 1 FROM sqlite_master WHERE name='listing_lifecycle'").get();
 const presenceQuery=hasPresence?source.prepare('SELECT session,vid FROM compact_presence WHERE key=?'):null;
 const matches=(r,p,requireSession=false)=>{const presence=presenceQuery?.get(r.listing_key);return !!presence&&(!requireSession||presence.session===p.session)&&presence.vid===p.shopVid&&(presence.session===p.session||unpackItem(db,r.detail).SellerVid===p.shopVid)};
 const targets=p=>db.prepare('SELECT id,detail,listing_key FROM items WHERE seller=?').all(p.seller).filter(r=>matches(r,p));
 let changed=0,processed=cursor;
 const apply=(r,p,payload,onlyExisting=false)=>{const old=db.prepare('SELECT payload_json FROM web_purchase_status WHERE item_id=?').get(r.id);if(!old&&onlyExisting)return;if(old&&Date.parse(JSON.parse(old.payload_json).checkedAt)>=Date.parse(p.checkedAt))return;db.prepare('INSERT OR REPLACE INTO web_purchase_status VALUES(?,?)').run(r.id,payload);changed++};
 const dropShop=row=>{db.prepare('DELETE FROM web_purchase_shops WHERE server=? AND session=? AND shop_vid=? AND seller=?').run(row.server,row.session,row.shop_vid,row.seller);
  db.prepare("DELETE FROM web_purchase_status WHERE item_id IN (SELECT id FROM items WHERE seller=?) AND json_extract(payload_json,'$.shopVid')=? AND json_extract(payload_json,'$.session')=?").run(row.seller,row.shop_vid,row.session)};
 const upsertShop=(p,raw)=>{const checked=Date.parse(p.checkedAt);
  const others=db.prepare('SELECT * FROM web_purchase_shops WHERE server=? AND seller=? AND NOT(session=? AND shop_vid=?)').all(p.server,p.seller,p.session,p.shopVid);
  if(others.some(o=>o.checked_at>=checked))return false;
  for(const o of others)dropShop(o);
  db.prepare(`INSERT INTO web_purchase_shops VALUES(?,?,?,?,?,?,?) ON CONFLICT(server,session,shop_vid,seller) DO UPDATE SET checked_at=excluded.checked_at,expires_at=excluded.expires_at,payload_json=excluded.payload_json WHERE excluded.checked_at>web_purchase_shops.checked_at`).run(p.server,p.session,p.shopVid,p.seller,checked,checked,raw);return true};
 const shopGone=row=>{if(!hasPresence)return false;let p;try{p=JSON.parse(row.payload_json)}catch{return true}
  const items=db.prepare('SELECT id,detail,listing_key FROM items WHERE seller=?').all(p.seller),mine=items.filter(r=>matches(r,p));
  if(!mine.length)return items.some(r=>presenceQuery.get(r.listing_key));
  if(!hasLifecycle)return false;const keys=new Set(mine.map(r=>r.listing_key));keys.add(p.listingKey);
  const states=[...keys].map(k=>db.prepare('SELECT is_active FROM listing_lifecycle WHERE listing_key=?').get(k)).filter(Boolean);
  return states.length>0&&states.every(s=>!s.is_active)};
 db.exec('SAVEPOINT purchase_import');try{
 if(!db.prepare("SELECT value FROM meta WHERE key='purchase_shops_schema'").get()){
  for(const row of db.prepare('SELECT DISTINCT payload_json FROM web_purchase_status').all()){
   let p;try{p=JSON.parse(row.payload_json)}catch{continue}
   if(!validate(p,state.id,now)||p.correlation!=='request-and-shop'||p.scope!=='shop'||!['Preparing','Available'].includes(p.result))continue;
   upsertShop(p,row.payload_json);
  }
  db.prepare("INSERT OR REPLACE INTO meta VALUES('purchase_shops_schema','1')").run();
 }
 for(const row of rows){processed=row.id;let p;try{p=JSON.parse(row.payload_json)}catch{continue}if(!validate(p,state.id,now))continue;
 db.prepare('INSERT OR IGNORE INTO web_purchase_checks VALUES(?,?)').run(row.id,row.payload_json);
 if(p.correlation!=='request-and-shop'||!['Preparing','NotEnoughMoney','Available','Success'].includes(p.result))continue;
 const anchor=db.prepare('SELECT id,detail,listing_key FROM items WHERE listing_key=? AND seller=?').get(p.listingKey,p.seller);if(!anchor){if(Date.now()-Date.parse(p.checkedAt)<ANCHOR_RETRY_MS){processed=row.id-1;break}continue;}
 if(!hasPresence)continue;
 if(!matches(anchor,p,true))continue;
 const clear=p.result==='NotEnoughMoney';
 if((p.scope==='shop'&&p.result!=='Success')||p.result==='NotEnoughMoney'){
  if(!upsertShop(p,row.payload_json))continue;
  for(const r of targets(p))apply(r,p,row.payload_json,clear);
 }else apply(anchor,p,row.payload_json,clear);
 }
 if(rows.length||reapply){const newerClean=source.prepare("SELECT payload_json FROM purchase_checks WHERE json_extract(payload_json,'$.seller')=? AND json_extract(payload_json,'$.result')='NotEnoughMoney' AND json_extract(payload_json,'$.correlation')='request-and-shop' AND json_extract(payload_json,'$.checkedAt')>? ORDER BY id DESC LIMIT 1");
  for(const row of db.prepare('SELECT * FROM web_purchase_shops WHERE server=?').all(state.id)){let old;try{old=JSON.parse(row.payload_json)}catch{continue}if(old.result!=='Preparing')continue;
   const hit=newerClean.get(row.seller,old.checkedAt);if(!hit)continue;let p;try{p=JSON.parse(hit.payload_json)}catch{continue}if(!validate(p,state.id,now))continue;
   db.prepare('DELETE FROM web_purchase_shops WHERE server=? AND session=? AND shop_vid=? AND seller=?').run(row.server,row.session,row.shop_vid,row.seller);
   db.prepare('INSERT OR REPLACE INTO web_purchase_shops VALUES(?,?,?,?,?,?,?)').run(p.server,p.session,p.shopVid,p.seller,Date.parse(p.checkedAt),Date.parse(p.checkedAt),hit.payload_json);
   for(const r of db.prepare('SELECT id FROM items WHERE seller=?').all(p.seller))apply(r,p,hit.payload_json,true);changed++}}
 if(reapply&&hasPresence)for(const row of db.prepare('SELECT * FROM web_purchase_shops WHERE server=?').all(state.id)){
  if(shopGone(row)){dropShop(row);continue}
  const p=JSON.parse(row.payload_json);if(!validate(p,state.id,now)||p.correlation!=='request-and-shop')continue;
  for(const r of targets(p))apply(r,p,row.payload_json,p.result==='NotEnoughMoney');
 }
 db.prepare("INSERT OR REPLACE INTO meta VALUES('purchase_status_revision',?)").run(String(revision));
 db.prepare("INSERT OR REPLACE INTO meta VALUES('purchase_check_cursor',?)").run(String(processed));db.exec('RELEASE purchase_import');return changed;
 }catch(e){db.exec('ROLLBACK TO purchase_import;RELEASE purchase_import');throw e}}
function status(p,now=Date.now(),fresh=MIN_FRESH_MS){if(!p||p.correlation!=='request-and-shop')return null;const checked=Date.parse(p.checkedAt),age=now-checked;if(!Number.isFinite(age)||age< -5000)return null;
 const preparing=p.result==='Preparing',known=preparing||['NotEnoughMoney','Available','Success'].includes(p.result);
 return {isFake:preparing,state:!known?'unknown':preparing?(age<=fresh?'fake':'fake-stale'):'clear',result:p.result,reason:p.reason,checkedAt:p.checkedAt,ageSeconds:Math.max(0,Math.round(age/1000)),staleAt:new Date(checked+fresh).toISOString(),scope:p.scope}}
const lookups=new WeakMap(),freshCache=new WeakMap();
function freshFor(db,now){const c=freshCache.get(db);if(c&&now-c.at<60000)return c.value;const value=freshMs(db);freshCache.set(db,{at:now,value});return value}
const shopLookups=new WeakMap();
function shopEvidence(db,item){let q=shopLookups.get(db);if(q===undefined){q=db.prepare("SELECT 1 FROM sqlite_master WHERE name='web_purchase_shops'").get()?db.prepare('SELECT payload_json FROM web_purchase_shops WHERE server=? AND seller=? ORDER BY checked_at DESC LIMIT 1'):null;if(q)shopLookups.set(db,q)}
 if(!q||!item.Seller||!item.serverId)return null;
 const row=q.get(item.serverId,item.Seller);if(!row)return null;try{return JSON.parse(row.payload_json)}catch{return null}}
function attach(db,item){let lookup=lookups.get(db);if(lookup===undefined){lookup=db.prepare("SELECT 1 FROM sqlite_master WHERE name='web_purchase_status'").get()?db.prepare('SELECT payload_json FROM web_purchase_status WHERE item_id=?'):null;if(lookup)lookups.set(db,lookup)}const row=item.id&&lookup?.get(item.id);let p=row?JSON.parse(row.payload_json):null;
 const shop=shopEvidence(db,item);let fromShop=false;if(shop&&(!p||Date.parse(shop.checkedAt)>=Date.parse(p.checkedAt))){p=shop;fromShop=true}
 const now=Date.now();item.purchaseStatus=p&&(fromShop||p.shopVid===item.SellerVid)&&p.seller===item.Seller&&p.server===item.serverId?status(p,now,freshFor(db,now)):null;
 const current=item.purchaseStatus,known=current&&current.state!=='unknown';
 item.IsFake=known?current.isFake:null;
 item.MarketStatus=!known?'Doğrulanmadı':current.isFake?'FAKE':'Doğrulandı';
 return item}
module.exports={MIN_FRESH_MS,freshMs,ensure,validate,sync,status,attach};
