const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),zlib=require('node:zlib'),http=require('node:http'),{DatabaseSync}=require('node:sqlite');
const {resolveServer}=require('./servers.cjs');
const {unpackBatch}=require('./category-delivery.cjs');
const shopScene=require('./shop-scene.cjs');

const SKEW_SECONDS=300,NONCE_TTL_SECONDS=900,MAX_BATCH_BYTES=32*1024*1024,MAX_ROWS_BYTES=8*1024*1024,MAX_ROWS_DECOMPRESSED=32*1024*1024;
const MIN_FREE_BYTES=Number(process.env.INGEST_MIN_FREE_BYTES||1024*1024*1024);
const sha256=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
const HEX=/^[0-9a-f]+$/i;

function canonical(method,target,timestamp,nonce,body){return [String(method).toUpperCase(),target,String(timestamp),nonce,sha256(body)].join('\n')}
function signRequest(secretHex,method,target,body=Buffer.alloc(0),{timestamp=Math.floor(Date.now()/1000),nonce=crypto.randomBytes(16).toString('hex')}={}){
 const signature=crypto.createHmac('sha256',Buffer.from(secretHex,'hex')).update(canonical(method,target,timestamp,nonce,body)).digest('hex');
 return {'X-M2-Timestamp':String(timestamp),'X-M2-Nonce':nonce,'X-M2-Signature':signature};
}

class KeyStore{
 constructor(file){this.file=file;this.keys=new Map();this.mtime=0;this.reload()}
 reload(){
  const stat=fs.statSync(this.file);if(stat.mtimeMs===this.mtime)return;
  const parsed=JSON.parse(fs.readFileSync(this.file,'utf8').replace(/^﻿/,'')),keys=new Map();
  if(!Array.isArray(parsed.keys)||!parsed.keys.length)throw Error('Anahtar dosyası boş');
  for(const k of parsed.keys){
   if(typeof k.id!=='string'||!/^[A-Za-z0-9_-]{1,32}$/.test(k.id)||typeof k.secret!=='string'||!HEX.test(k.secret)||k.secret.length<64||k.secret.length%2)throw Error('Anahtar tanımı geçersiz');
   if(!['active','next','retired'].includes(k.status))throw Error('Anahtar durumu geçersiz');
   keys.set(k.id,{secret:Buffer.from(k.secret,'hex'),status:k.status});
  }
  this.keys=keys;this.mtime=stat.mtimeMs;
 }
 find(id){try{this.reload()}catch{ }const key=this.keys.get(id);return key&&key.status!=='retired'?key:null}
}

const MIRROR_SCHEMA=`PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;PRAGMA busy_timeout=5000;
CREATE TABLE IF NOT EXISTS trade_metadata(key TEXT PRIMARY KEY,value TEXT);
CREATE TABLE IF NOT EXISTS trade_runs(started TEXT PRIMARY KEY,plan_json TEXT);
CREATE TABLE IF NOT EXISTS category_outbox(batch_id TEXT PRIMARY KEY,category_key TEXT NOT NULL,revision INTEGER NOT NULL,scan_id TEXT NOT NULL UNIQUE,completed_at TEXT NOT NULL,encoding TEXT NOT NULL,body_hash TEXT NOT NULL,body BLOB,acked_at TEXT,UNIQUE(category_key,revision));
CREATE INDEX IF NOT EXISTS ix_category_pending ON category_outbox(acked_at,category_key,revision);
CREATE TABLE IF NOT EXISTS purchase_checks(id INTEGER PRIMARY KEY,payload_json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS compact_presence(key TEXT PRIMARY KEY,session TEXT,vid INTEGER,last_seen TEXT);
CREATE INDEX IF NOT EXISTS ix_presence_seen ON compact_presence(last_seen,key);`;

class HttpError extends Error{constructor(status,message,code){super(message);this.status=status;this.code=code||'error'}}

function createIngest({sourceRoot,keysFile,stateDir,now=()=>Date.now(),log=()=>{}}){
 fs.mkdirSync(sourceRoot,{recursive:true});fs.mkdirSync(stateDir,{recursive:true});
 const keys=new KeyStore(keysFile);
 const nonceDb=new DatabaseSync(path.join(stateDir,'ingest-nonces.sqlite'));
 nonceDb.exec('PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;CREATE TABLE IF NOT EXISTS nonces(nonce TEXT PRIMARY KEY,expires INTEGER NOT NULL);CREATE INDEX IF NOT EXISTS ix_nonce_exp ON nonces(expires);');
 const insertNonce=nonceDb.prepare('INSERT INTO nonces VALUES(?,?)'),purgeNonces=nonceDb.prepare('DELETE FROM nonces WHERE expires<?');
 let lastPurge=0;
 const failures=new Map();
 const mirrors=new Map();

 function clientIp(req){const peer=req.socket.remoteAddress||'';const loopback=peer==='127.0.0.1'||peer==='::1'||peer==='::ffff:127.0.0.1';const forwarded=loopback&&req.headers['x-real-ip'];return String(forwarded||peer).slice(0,64)}
 function throttled(ip){const f=failures.get(ip);if(!f)return false;if(now()-f.start>60000){failures.delete(ip);return false}return f.count>=30}
 function recordFailure(ip){const f=failures.get(ip);if(!f||now()-f.start>60000)failures.set(ip,{count:1,start:now()});else f.count++}

 function mirror(serverName){
  const server=resolveServer(serverName);if(!server)throw new HttpError(400,'Bilinmeyen sunucu','unknown-server');
  let m=mirrors.get(server.id);if(m)return m;
  const dir=path.join(sourceRoot,server.id);fs.mkdirSync(dir,{recursive:true});
  const info=path.join(dir,'sunucu.json');if(!fs.existsSync(info))fs.writeFileSync(info,JSON.stringify({server:server.name}));
  const db=new DatabaseSync(path.join(dir,'market.sqlite'));db.exec(MIRROR_SCHEMA);db.exec(shopScene.SCHEMA);
  db.prepare("INSERT OR IGNORE INTO trade_metadata VALUES('server',?)").run(server.name);
  m={server,dir,db};mirrors.set(server.id,m);return m;
 }
 function meta(m,key){return m.db.prepare('SELECT value FROM trade_metadata WHERE key=?').get(key)?.value}
 function writeReady(m,row){
  const folder=path.join(m.dir,'category-sync');fs.mkdirSync(folder,{recursive:true});
  const temp=path.join(folder,'ready-'+crypto.randomBytes(6).toString('hex')+'.tmp');
  fs.writeFileSync(temp,JSON.stringify({schemaVersion:1,datasetId:meta(m,'dataset_id'),server:m.server.name,batchId:row.batch_id,categoryKey:row.category_key,revision:row.revision,completedAt:row.completed_at}));
  fs.renameSync(temp,path.join(folder,'ready.json'));
 }
 function readAcks(m){
  const folder=path.join(m.dir,'category-sync','acks'),out=[];
  if(!fs.existsSync(folder))return out;
  for(const name of fs.readdirSync(folder)){
   if(!name.endsWith('.json'))continue;const file=path.join(folder,name);
   try{if(fs.statSync(file).size>4096)continue;const ack=JSON.parse(fs.readFileSync(file,'utf8'));if(ack&&ack.schemaVersion===1&&typeof ack.categoryKey==='string'&&Number.isSafeInteger(ack.revision))out.push(ack)}catch{ }
  }
  return out;
 }
 function freeSpaceOk(dir){try{const s=fs.statfsSync(dir);return Number(s.bavail)*Number(s.bsize)>=MIN_FREE_BYTES}catch{return true}}

 function handleState(url){
  const m=mirror(url.searchParams.get('server')||'');
  const categories={};
  for(const r of m.db.prepare('SELECT category_key,MAX(revision) revision FROM category_outbox GROUP BY category_key').all())categories[r.category_key]={received:Number(r.revision),acked:0};
  for(const a of readAcks(m)){if(a.datasetId!==meta(m,'dataset_id'))continue;(categories[a.categoryKey]??={received:0,acked:0}).acked=Math.max(categories[a.categoryKey]?.acked||0,a.revision)}
  const lastPresence=m.db.prepare('SELECT last_seen,key FROM compact_presence ORDER BY last_seen DESC,key DESC LIMIT 1').get();
  return {server:m.server.name,datasetId:meta(m,'dataset_id')||null,categories,shopSceneProtocol:1,
   purchaseChecksCursor:Number(m.db.prepare('SELECT COALESCE(MAX(id),0) id FROM purchase_checks').get().id),
   presenceCursor:lastPresence?{lastSeen:lastPresence.last_seen,key:lastPresence.key}:null,
   latestRun:m.db.prepare('SELECT MAX(started) started FROM trade_runs').get().started||null,serverTime:Math.floor(now()/1000)};
 }
 function handleBatch(req,body){
  const h=n=>String(req.headers[n]||'');
  const serverName=h('x-m2-server'),dataset=h('x-m2-dataset'),category=h('x-m2-category'),revision=Number(h('x-m2-revision')),batchId=h('x-m2-batch').toLowerCase(),scan=h('x-m2-scan'),completed=h('x-m2-completed'),bodyHash=h('x-m2-body-hash').toLowerCase();
  if(!body.length||body.length>MAX_BATCH_BYTES)throw new HttpError(413,'Paket boyutu geçersiz','size');
  if(!dataset||dataset.length>200||!/^ch\d+:\d+:\d+:\d+$/.test(category)||!Number.isSafeInteger(revision)||revision<1||!/^[0-9a-f]{64}$/.test(batchId)||!/^[0-9a-f]{64}$/.test(bodyHash)||!scan||scan.length>200||!Number.isFinite(Date.parse(completed)))throw new HttpError(400,'Paket üst bilgisi geçersiz','headers');
  if(!freeSpaceOk(sourceRoot))throw new HttpError(507,'Sunucuda yeterli disk alanı yok','disk');
  const m=mirror(serverName),row={batch_id:batchId,category_key:category,revision,scan_id:scan,completed_at:completed,encoding:'br',body_hash:bodyHash,body};
  try{const parsed=unpackBatch(row,m.server.name);if(parsed.datasetId!==dataset)throw Error('veri kümesi başlığı paketle uyuşmuyor')}catch(error){throw new HttpError(422,'Paket doğrulaması başarısız: '+error.message,'invalid-batch')}
  const existing=m.db.prepare('SELECT batch_id,body_hash FROM category_outbox WHERE category_key=? AND revision=?').get(category,revision);
  if(existing){if(existing.batch_id!==batchId||existing.body_hash!==bodyHash)throw new HttpError(409,'Aynı revision için farklı paket','conflict');return {status:'duplicate',revision}}
  const activeDataset=meta(m,'dataset_id');
  if(activeDataset&&activeDataset!==dataset)throw new HttpError(409,'Veri kümesi değişti; kontrollü yeniden eşitleme gerekli','dataset');
  m.db.exec('BEGIN IMMEDIATE');
  try{
   m.db.prepare("INSERT OR IGNORE INTO trade_metadata VALUES('dataset_id',?)").run(dataset);
   m.db.prepare("INSERT OR REPLACE INTO trade_metadata VALUES('category_delivery_protocol','1')").run();
   m.db.prepare("INSERT OR REPLACE INTO trade_metadata VALUES('category_delivery_trigger','category-complete')").run();
   m.db.prepare('INSERT INTO category_outbox VALUES(?,?,?,?,?,?,?,?,NULL)').run(batchId,category,revision,scan,completed,'br',bodyHash,body);
   m.db.exec('COMMIT');
  }catch(error){try{m.db.exec('ROLLBACK')}catch{}throw error.message?.includes('UNIQUE')?new HttpError(409,'Paket çakışması','conflict'):error}
  writeReady(m,row);
  return {status:'stored',revision};
 }
 function handleRows(body,gzipped){
  let raw=body;if(gzipped){try{raw=zlib.gunzipSync(body,{maxOutputLength:MAX_ROWS_DECOMPRESSED})}catch{throw new HttpError(400,'Gövde açılamadı','gzip')}}
  let payload;try{payload=JSON.parse(raw.toString('utf8'))}catch{throw new HttpError(400,'JSON geçersiz','json')}
  if(!payload||typeof payload.server!=='string'||typeof payload.table!=='string'||!Array.isArray(payload.rows))throw new HttpError(400,'Satır isteği geçersiz','rows');
  const m=mirror(payload.server);let accepted=0;
  m.db.exec('BEGIN IMMEDIATE');
  try{
   if(payload.table==='purchase_checks'){
    if(payload.rows.length>5000)throw new HttpError(413,'Çok fazla satır','rows');
    const put=m.db.prepare('INSERT OR IGNORE INTO purchase_checks VALUES(?,?)');
    for(const r of payload.rows){
     if(!Number.isSafeInteger(r.id)||r.id<1||typeof r.payload!=='string'||r.payload.length>8192)throw new HttpError(400,'Kontrol kaydı geçersiz','rows');
     let p;try{p=JSON.parse(r.payload)}catch{throw new HttpError(400,'Kontrol kaydı JSON değil','rows')}
     if(!p||typeof p.seller!=='string'||typeof p.result!=='string'||typeof p.checkedAt!=='string')throw new HttpError(400,'Kontrol kaydı eksik alan','rows');
     put.run(r.id,r.payload);accepted++;
    }
   }else if(payload.table==='compact_presence'){
    if(payload.rows.length>10000)throw new HttpError(413,'Çok fazla satır','rows');
    const put=m.db.prepare('INSERT INTO compact_presence VALUES(?,?,?,?) ON CONFLICT(key) DO UPDATE SET session=excluded.session,vid=excluded.vid,last_seen=excluded.last_seen');
    for(const r of payload.rows){
     if(typeof r.key!=='string'||!r.key||r.key.length>1024||typeof r.session!=='string'||r.session.length>200||!Number.isSafeInteger(r.vid)||typeof r.lastSeen!=='string'||r.lastSeen.length>64)throw new HttpError(400,'Varlık kaydı geçersiz','rows');
     put.run(r.key,r.session,r.vid,r.lastSeen);accepted++;
    }
   }else if(payload.table==='shop_scene_current'){
    if(payload.rows.length>4)throw new HttpError(413,'Çok fazla harita','rows');
    for(const row of payload.rows){shopScene.putScene(m.db,row,m.server.name,now());accepted++;}
   }else if(payload.table==='trade_runs'){
    if(payload.rows.length>5)throw new HttpError(413,'Çok fazla satır','rows');
    const put=m.db.prepare('INSERT OR REPLACE INTO trade_runs VALUES(?,?)');
    for(const r of payload.rows){
     if(typeof r.started!=='string'||!Number.isFinite(Date.parse(r.started))||typeof r.planJson!=='string'||r.planJson.length>4*1024*1024)throw new HttpError(400,'Plan kaydı geçersiz','rows');
     JSON.parse(r.planJson);put.run(r.started,r.planJson);accepted++;
    }
   }else throw new HttpError(400,'Bilinmeyen tablo','table');
   m.db.exec('COMMIT');
  }catch(error){try{m.db.exec('ROLLBACK')}catch{}throw error instanceof HttpError?error:new HttpError(400,'Satırlar uygulanamadı: '+error.message,'rows')}
  return {accepted,table:payload.table};
 }

 function readBody(req,limit){
  return new Promise((resolve,reject)=>{
   const declared=Number(req.headers['content-length']||0);if(declared>limit)return reject(new HttpError(413,'Gövde çok büyük','size'));
   const chunks=[];let size=0;
   req.on('data',c=>{size+=c.length;if(size>limit){reject(new HttpError(413,'Gövde çok büyük','size'));req.destroy();return}chunks.push(c)});
   req.on('end',()=>resolve(Buffer.concat(chunks)));req.on('error',reject);
  });
 }
 function authenticate(req,body){
  const keyId=String(req.headers['x-m2-key-id']||''),timestamp=String(req.headers['x-m2-timestamp']||''),nonce=String(req.headers['x-m2-nonce']||''),signature=String(req.headers['x-m2-signature']||'').toLowerCase();
  if(!keyId||!/^\d{9,12}$/.test(timestamp)||!/^[0-9a-f]{16,64}$/i.test(nonce)||!/^[0-9a-f]{64}$/.test(signature))throw new HttpError(401,'Kimlik doğrulama başlıkları eksik','auth');
  const seconds=Number(timestamp);if(Math.abs(Math.floor(now()/1000)-seconds)>SKEW_SECONDS)throw new HttpError(401,'Zaman damgası kabul penceresinin dışında','clock');
  const key=keys.find(keyId);if(!key)throw new HttpError(401,'Anahtar tanınmıyor','auth');
  const expected=crypto.createHmac('sha256',key.secret).update(canonical(req.method,req.url,timestamp,nonce,body)).digest();
  const given=Buffer.from(signature,'hex');
  if(given.length!==expected.length||!crypto.timingSafeEqual(given,expected))throw new HttpError(401,'İmza geçersiz','auth');
  const nowSeconds=Math.floor(now()/1000);
  if(nowSeconds-lastPurge>60){purgeNonces.run(nowSeconds);lastPurge=nowSeconds}
  try{insertNonce.run(nonce.toLowerCase(),nowSeconds+NONCE_TTL_SECONDS)}catch{throw new HttpError(401,'İstek tekrarı reddedildi','replay')}
  return keyId;
 }
 async function handle(req,res){
  const send=(status,obj)=>{const text=JSON.stringify(obj);res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Content-Length':Buffer.byteLength(text),'Cache-Control':'no-store'});res.end(text)};
  const ip=clientIp(req);
  try{
   if(throttled(ip))throw new HttpError(429,'Çok fazla başarısız deneme','throttle');
   const url=new URL(req.url,'http://ingest.local');
   if(!url.pathname.startsWith('/ingest/v1/'))throw new HttpError(404,'Bulunamadı','not-found');
   const post=req.method==='POST',get=req.method==='GET';
   if(!post&&!get)throw new HttpError(405,'Yöntem desteklenmiyor','method');
   const route=url.pathname.slice('/ingest/v1/'.length);
   const limit=route==='batch'?MAX_BATCH_BYTES:MAX_ROWS_BYTES;
   const body=post?await readBody(req,limit):Buffer.alloc(0);
   try{authenticate(req,body)}catch(error){if(error instanceof HttpError&&error.status===401)recordFailure(ip);throw error}
   if(get&&route==='state')return send(200,handleState(url));
   if(get&&route==='acks'){
    const m=mirror(url.searchParams.get('server')||''),acks=readAcks(m).filter(a=>a.datasetId===meta(m,'dataset_id'));
    const release=m.db.prepare('UPDATE category_outbox SET body=NULL,acked_at=COALESCE(acked_at,?) WHERE category_key=? AND revision<=? AND body IS NOT NULL');
    const stamp=new Date(now()).toISOString();for(const a of acks)release.run(stamp,a.categoryKey,a.revision);
    return send(200,{acks});
   }
   if(get&&route==='ping')return send(200,{ok:true,serverTime:Math.floor(now()/1000)});
   if(post&&route==='batch')return send(200,handleBatch(req,body));
   if(post&&route==='rows')return send(200,handleRows(body,String(req.headers['content-encoding']||'').toLowerCase()==='gzip'));
   throw new HttpError(404,'Bulunamadı','not-found');
  }catch(error){
   if(error instanceof HttpError){log('reject',{status:error.status,code:error.code,ip});return send(error.status,{error:error.message,code:error.code,serverTime:Math.floor(now()/1000)})}
   log('error',{message:error.message});return send(500,{error:'Sunucu hatası',code:'internal'});
  }
 }
 function close(){try{nonceDb.close()}catch{}for(const m of mirrors.values())try{m.db.close()}catch{}mirrors.clear()}
 return {handle,close,mirror};
}
function createIngestServer(options){
 const ingest=createIngest(options),server=http.createServer((req,res)=>{ingest.handle(req,res)});
 server.headersTimeout=10000;server.requestTimeout=120000;server.keepAliveTimeout=30000;server.maxConnections=64;
 server.on('close',()=>ingest.close());
 return {server,ingest};
}
module.exports={createIngest,createIngestServer,signRequest,canonical,sha256,KeyStore,SKEW_SECONDS};
