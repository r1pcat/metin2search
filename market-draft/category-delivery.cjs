const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),zlib=require('node:zlib'),{DatabaseSync}=require('node:sqlite');
const {resolveServer}=require('./servers.cjs');
const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
function categoryDeliveryEnabled(source){return !!source.prepare("SELECT 1 FROM sqlite_master WHERE name='category_outbox' AND type='table'").get()&&source.prepare("SELECT value FROM trade_metadata WHERE key='category_delivery_protocol'").get()?.value==='1'}
function prepareCategoryReceiver(db){db.exec(`CREATE TABLE IF NOT EXISTS category_received(dataset TEXT,category_key TEXT,revision INTEGER,batch_id TEXT,body_hash TEXT,completed_at TEXT,PRIMARY KEY(dataset,category_key,revision));CREATE TABLE IF NOT EXISTS category_active_dataset(server TEXT PRIMARY KEY,dataset TEXT);CREATE TABLE IF NOT EXISTS category_lifecycle_clock(listing_key TEXT PRIMARY KEY,event_id INTEGER,version_id INTEGER);`)}
function unpackBatch(row,serverName){
 if(row.encoding!=='br'||!row.body||row.body.length>32*1024*1024||sha(row.body)!==row.body_hash.toLowerCase())throw Error('Kategori paket boyutu/hash doğrulaması başarısız');
 const raw=zlib.brotliDecompressSync(row.body,{maxOutputLength:128*1024*1024}),batch=JSON.parse(raw.toString('utf8'));
 const incremental=batch.complete===false&&batch.kind==='incremental'&&Array.isArray(batch.tombstones)&&batch.tombstones.length===0&&(batch.membershipRemovals===undefined||Array.isArray(batch.membershipRemovals)&&batch.membershipRemovals.length===0),
  complete=batch.complete===true&&(batch.kind===undefined||batch.kind==='complete');
 if(!incremental&&!complete)throw Error('Kategori paket sözleşmesi uyuşmuyor');
 batch.incremental=incremental;
 if(batch.schemaVersion!==1||!resolveServer(batch.server)||resolveServer(batch.server)?.id!==resolveServer(serverName)?.id||typeof batch.datasetId!=='string'||batch.datasetId.length>200||batch.categoryKey!==row.category_key||batch.revision!==row.revision||batch.scanId!==row.scan_id||!Number.isSafeInteger(batch.revision)||batch.previousRevision!==batch.revision-1||!Number.isFinite(Date.parse(batch.completedAt))||!Array.isArray(batch.upserts)||batch.upserts.length>200000||!Array.isArray(batch.tombstones)||!batch.payloads)throw Error('Kategori paket sözleşmesi uyuşmuyor');
 if(sha(Buffer.from(batch.datasetId+'|'+batch.categoryKey+'|'+batch.revision))!==row.batch_id.toLowerCase())throw Error('Kategori paket kimliği uyuşmuyor');
 return batch;
}
function applyCategoryBatch(state,row,storeRow,afterUpsert){
 const db=state.db,batch=unpackBatch(row,state.name),dataset=db.prepare('SELECT dataset FROM category_active_dataset WHERE server=?').get(state.id);
 if(dataset&&dataset.dataset!==batch.datasetId)throw Error('Kaynak veri kümesi değişti; kontrollü yeniden eşitleme gerekli');
 const receipt=db.prepare('SELECT batch_id,body_hash FROM category_received WHERE dataset=? AND category_key=? AND revision=?').get(batch.datasetId,batch.categoryKey,batch.revision);
 if(receipt){if(receipt.batch_id!==row.batch_id||receipt.body_hash!==row.body_hash)throw Error('Aynı kategori revision için farklı paket');return {batch,duplicate:true,changed:0}}
 const previous=db.prepare('SELECT COALESCE(MAX(revision),0) revision FROM category_received WHERE dataset=? AND category_key=?').get(batch.datasetId,batch.categoryKey).revision;
 if(previous!==batch.previousRevision)throw Error('Kategori paket sırası eksik; önceki revision bekleniyor');
 const lifecycle=db.prepare(`INSERT INTO listing_lifecycle VALUES(?,?,?,?,?,?) ON CONFLICT(listing_key) DO UPDATE SET is_active=excluded.is_active,last_seen=COALESCE(excluded.last_seen,listing_lifecycle.last_seen),removed_at=excluded.removed_at,source_scope=excluded.source_scope,revision=excluded.revision`);
 const clock=db.prepare('INSERT OR REPLACE INTO category_lifecycle_clock VALUES(?,?,?)');
 function updateLifecycle(key,event,version,active,observed,removed){
  const old=db.prepare('SELECT event_id,version_id FROM category_lifecycle_clock WHERE listing_key=?').get(key),fallbackEvent=Number(db.prepare("SELECT value FROM meta WHERE key='lifecycle_event'").get()?.value||0),latest=Number(db.prepare('SELECT MAX(source_version) version FROM observations WHERE listing_key=?').get(key)?.version||0);
  if(old&&(event<old.event_id||event===old.event_id&&version<old.version_id)||!old&&db.prepare('SELECT 1 FROM listing_lifecycle WHERE listing_key=?').get(key)&&(event<fallbackEvent||version<latest))return;
  lifecycle.run(key,Number(active),observed,removed,'category-completed',version);clock.run(key,event,version);
 }
 let changed=0,cursor=state.cursor;
 db.exec('SAVEPOINT category_batch');try{
  db.prepare('INSERT OR IGNORE INTO category_active_dataset VALUES(?,?)').run(state.id,batch.datasetId);
  for(const item of batch.upserts){
   const payload=batch.payloads[item.payloadHash];if(typeof item.listingKey!=='string'||!Number.isSafeInteger(item.versionId)||item.versionId<1||!Number.isSafeInteger(item.lifecycleEventId)||!Number.isFinite(Date.parse(item.observed))||typeof item.isActive!=='boolean'||typeof payload!=='string'||Buffer.byteLength(payload)>4*1024*1024||sha(Buffer.from(payload))!==item.payloadHash)throw Error('Kategori ürün verisi doğrulanamadı');
   const previousVersion=Number(db.prepare('SELECT MAX(source_version) version FROM observations WHERE listing_key=?').get(item.listingKey)?.version||0);
   const applied=item.versionId>=previousVersion;
   if(applied){storeRow({key:item.listingKey,id:item.versionId,observed:item.observed,category:JSON.stringify(item.category||{}),payload_hash:item.payloadHash,rawPayload:Buffer.from(payload)});changed++}
   if(!batch.incremental||item.isActive)updateLifecycle(item.listingKey,item.lifecycleEventId,item.versionId,item.isActive,item.observed,item.isActive?null:batch.completedAt);
   if(applied&&afterUpsert)afterUpsert(item,payload);cursor=Math.max(cursor,item.versionId);
  }
  for(const item of batch.tombstones){if(typeof item.listingKey!=='string'||!Number.isSafeInteger(item.lifecycleEventId)||!Number.isSafeInteger(item.versionId))throw Error('Kategori kaldırma kanıtı eksik');updateLifecycle(item.listingKey,item.lifecycleEventId,item.versionId,false,null,item.removedAt||batch.completedAt)}
  db.prepare('INSERT INTO category_received VALUES(?,?,?,?,?,?)').run(batch.datasetId,batch.categoryKey,batch.revision,row.batch_id,row.body_hash,batch.completedAt);
  db.prepare("INSERT OR REPLACE INTO meta VALUES('revision',?)").run(String(Number(db.prepare("SELECT value FROM meta WHERE key='revision'").get()?.value||0)+1));
  db.prepare("INSERT OR REPLACE INTO meta VALUES('category_publication_pending','1')").run();
  if(!batch.incremental)db.prepare("INSERT INTO meta VALUES('lifecycle_managed','1') ON CONFLICT(key) DO UPDATE SET value=excluded.value WHERE meta.value IS NOT excluded.value").run();
  db.prepare("INSERT OR REPLACE INTO meta VALUES('cursor',?)").run(String(cursor));db.exec('RELEASE category_batch');state.cursor=cursor;
 }catch(e){db.exec('ROLLBACK TO category_batch; RELEASE category_batch');throw e}
 return {batch,duplicate:false,changed};
}
function makeArchive(sourceFile){
 const base=path.join(path.dirname(sourceFile),'Gunler');let index=null;
 function build(){index=new Map();let days=[];try{days=fs.readdirSync(base).filter(d=>/^\d{4}-\d{2}-\d{2}$/.test(d)).sort().reverse().slice(0,3)}catch{return}
  for(const day of days){const file=path.join(base,day,'daily.sqlite');if(!fs.existsSync(file))continue;let db;
   try{db=new DatabaseSync(file,{readOnly:true});db.exec('PRAGMA busy_timeout=3000');
    for(const e of db.prepare("SELECT source_key,metadata_json,blob_hash FROM events WHERE kind='category' AND blob_hash IS NOT NULL").all()){if(index.has(e.source_key))continue;let m;try{m=JSON.parse(e.metadata_json)}catch{continue}
     if(m&&typeof m.batchId==='string'&&typeof m.categoryKey==='string'&&Number.isSafeInteger(m.revision)&&typeof m.scanId==='string'&&typeof m.bodyHash==='string')index.set(e.source_key,{meta:m,blobHash:e.blob_hash,file,day})}
   }catch{}finally{try{db?.close()}catch{}}}}
 function missing(state,limit=400){if(!index)build();const ds=state.db.prepare('SELECT dataset FROM category_active_dataset WHERE server=?').get(state.id);if(!ds||!index.size)return [];
  const prev=state.db.prepare('SELECT COALESCE(MAX(revision),0) revision FROM category_received WHERE dataset=? AND category_key=?'),cats=new Set([...index.values()].map(e=>e.meta.categoryKey)),rows=[],open=new Map();
  try{for(const key of cats){let r=prev.get(ds.dataset,key).revision+1;
    while(rows.length<limit){const e=index.get(sha(Buffer.from(ds.dataset+'|'+key+'|'+r)));if(!e||e.meta.categoryKey!==key||e.meta.revision!==r)break;
     let db=open.get(e.file);if(!db){db=new DatabaseSync(e.file,{readOnly:true});db.exec('PRAGMA busy_timeout=3000');open.set(e.file,db)}
     const blob=db.prepare('SELECT bytes FROM blobs WHERE hash=?').get(e.blobHash);if(!blob)break;
     rows.push({batch_id:e.meta.batchId,category_key:key,revision:r,scan_id:e.meta.scanId,completed_at:e.meta.completedAt,encoding:e.meta.encoding||'br',body_hash:e.meta.bodyHash,body:Buffer.from(blob.bytes)});r++}}
  }finally{for(const db of open.values())try{db.close()}catch{}}
  return rows}
 return {missing};
}
function acknowledge(sourceFile,row,batch){const dir=path.join(path.dirname(sourceFile),'category-sync','acks');fs.mkdirSync(dir,{recursive:true});const file=path.join(dir,sha(Buffer.from(batch.categoryKey))+'.json'),temp=file+'.tmp';fs.writeFileSync(temp,JSON.stringify({schemaVersion:1,datasetId:batch.datasetId,server:batch.server,categoryKey:batch.categoryKey,revision:batch.revision,batchId:row.batch_id,bodyHash:row.body_hash}));fs.renameSync(temp,file)}
module.exports={categoryDeliveryEnabled,prepareCategoryReceiver,unpackBatch,applyCategoryBatch,acknowledge,makeArchive};

