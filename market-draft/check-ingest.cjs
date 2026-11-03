require('./test-cleanup.cjs');
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),zlib=require('node:zlib'),assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite');
const {createIngestServer,signRequest}=require('./ingest.cjs'),{LiveSync}=require('./live-sync.cjs');
const fixture=JSON.parse(fs.readFileSync(path.join(__dirname,'../local-scanner/research/category-outbox-fixture.json'),'utf8'));
const SECRET_A='000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f',SECRET_B='ff'.repeat(32);

(async()=>{
 assert.equal(signRequest(SECRET_A,'POST','/ingest/v1/batch?x=1',Buffer.from('hello'),{timestamp:1760000000,nonce:'0123456789abcdef'})['X-M2-Signature'],'c020387e493c671f27271a1677d88cf79c703e0b609a13caef472f22b6d8ed69');

 const work=fs.mkdtempSync(path.join(os.tmpdir(),'m2-ingest-')),sourceRoot=path.join(work,'source'),keysFile=path.join(work,'keys.json');
 const writeKeys=keys=>fs.writeFileSync(keysFile,JSON.stringify({keys}));
 writeKeys([{id:'k1',secret:SECRET_A,status:'active'},{id:'k2',secret:SECRET_B,status:'next'}]);
 let clock=Date.now();const events=[];
 const {server}=createIngestServer({sourceRoot,keysFile,stateDir:path.join(work,'state'),now:()=>clock,log:(e,d)=>events.push([e,d])});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const base=`http://127.0.0.1:${server.address().port}`;
 const call=async(method,target,body=Buffer.alloc(0),{keyId='k1',secret=SECRET_A,headers={},sign={}}={})=>{
  const auth=signRequest(secret,method,target,body,{timestamp:Math.floor(clock/1000),...sign});
  const response=await fetch(base+target,{method,body:method==='GET'?undefined:body,headers:{'X-M2-Key-Id':keyId,...auth,...headers}});
  return {status:response.status,json:await response.json().catch(()=>null)};
 };
 const batchHeaders=(row,extra={})=>({'X-M2-Server':fixture.metadata.server,'X-M2-Dataset':fixture.metadata.dataset_id,'X-M2-Category':row.category_key,'X-M2-Revision':String(row.revision),'X-M2-Batch':row.batch_id,'X-M2-Scan':row.scan_id,'X-M2-Completed':row.completed_at,'X-M2-Body-Hash':row.body_hash,...extra});
 const send=(row,options={})=>call('POST','/ingest/v1/batch',Buffer.from(row.bodyBase64,'base64'),{...options,headers:batchHeaders(row,options.headers||{})});
 const server_='Safir';

 assert.equal((await call('GET','/ingest/v1/ping')).status,200,'doğru imza kabul edilir');
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{keyId:'k2',secret:SECRET_B})).status,200,'ikinci (sonraki) anahtar da kabul edilir: dönüşüm');
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{secret:SECRET_B})).status,401,'yanlış anahtarla imza reddedilir');
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{keyId:'nope'})).status,401,'bilinmeyen anahtar kimliği reddedilir');
 const replay={timestamp:Math.floor(clock/1000),nonce:'aaaaaaaaaaaaaaaa'};
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{sign:replay})).status,200);
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{sign:replay})).status,401,'aynı nonce ile tekrarlanan istek reddedilir');
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{sign:{timestamp:Math.floor(clock/1000)-600}})).status,401,'600 sn eski zaman damgası reddedilir');
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{sign:{timestamp:Math.floor(clock/1000)+600}})).status,401,'gelecekteki zaman damgası reddedilir');
 const tampered=signRequest(SECRET_A,'POST','/ingest/v1/rows',Buffer.from('{}'));
 assert.equal((await fetch(base+'/ingest/v1/rows',{method:'POST',body:'{"x":1}',headers:{'X-M2-Key-Id':'k1',...tampered}})).status,401,'gövde değişirse imza tutmaz');
 assert.equal((await fetch(base+'/ingest/v1/ping')).status,401,'başlıksız istek reddedilir');
 assert.equal((await fetch(base+'/baska')).status,404,'ingest dışı yol 404');
 writeKeys([{id:'k1',secret:SECRET_A,status:'retired'},{id:'k2',secret:SECRET_B,status:'active'}]);fs.utimesSync(keysFile,new Date(),new Date(Date.now()+5000));
 assert.equal((await call('GET','/ingest/v1/ping')).status,401,'emekli anahtar yeniden başlatmadan reddedilir');
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),{keyId:'k2',secret:SECRET_B})).status,200,'yeni anahtar kabul edilir');
 const useK2=(extra={})=>({keyId:'k2',secret:SECRET_B,...extra});

 for(let i=0;i<31;i++)await call('GET','/ingest/v1/ping',Buffer.alloc(0),{keyId:'k2',secret:SECRET_A});
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),useK2())).status,429,'çok sayıda başarısız denemeden sonra istemci kısıtlanır');
 clock+=61000;
 assert.equal((await call('GET','/ingest/v1/ping',Buffer.alloc(0),useK2())).status,200,'kısıtlama bir dakika sonra kalkar');

 const rows=fixture.batches;
 const bad=await send({...rows[0],body_hash:'0'.repeat(64)},useK2());assert.equal(bad.status,422,'gövde özeti uyuşmayan paket reddedilir');
 assert.equal((await send({...rows[0],batch_id:'1'.repeat(64)},useK2())).status,422,'batch_id türetimi uyuşmayan paket reddedilir');
 assert.equal((await call('POST','/ingest/v1/batch',Buffer.alloc(0),useK2({headers:batchHeaders(rows[0])}))).status,413,'boş gövde reddedilir');
 const first=await send(rows[0],useK2());assert.deepEqual([first.status,first.json.status],[200,'stored']);
 assert.deepEqual((await send(rows[0],useK2())).json.status,'duplicate','aynı paket tekrar gelirse yinelenen sayılır (idempotent)');
 const mirror=new DatabaseSync(path.join(sourceRoot,'safir','market.sqlite'),{readOnly:true});
 assert.equal(mirror.prepare('SELECT COUNT(*) n FROM category_outbox').get().n,1,'yinelenen paket ikinci satır üretmez');
 const other=Buffer.from(rows[0].bodyBase64,'base64');other[other.length-1]^=1;
 assert.equal((await call('POST','/ingest/v1/batch',other,useK2({headers:batchHeaders(rows[0])}))).status,422,'bozulmuş gövde reddedilir');
 assert.equal((await send(rows[0],useK2({headers:{'X-M2-Dataset':'baska-veri-kumesi'}}))).status,422,'başlıktaki veri kümesi pakettekiyle uyuşmalı');
 assert.ok(fs.existsSync(path.join(sourceRoot,'safir','category-sync','ready.json')),'alıcı uyandırma dosyası yazıldı');
 assert.equal(mirror.prepare("SELECT value FROM trade_metadata WHERE key='category_delivery_protocol'").get().value,'1');
 assert.equal(mirror.prepare("SELECT value FROM trade_metadata WHERE key='dataset_id'").get().value,fixture.metadata.dataset_id);
 const large=Buffer.alloc(33*1024*1024,1);
 assert.equal((await call('POST','/ingest/v1/batch',large,useK2({headers:batchHeaders(rows[0])}))).status,413,'32 MB üstü paket reddedilir');

 const post=(payload,gzip=false)=>{const raw=Buffer.from(JSON.stringify(payload));return call('POST','/ingest/v1/rows',gzip?zlib.gzipSync(raw):raw,useK2(gzip?{headers:{'Content-Encoding':'gzip'}}:{}))};
 const check=id=>({id,payload:JSON.stringify({seller:'Satıcı'+id,shopVid:10+id,result:'Preparing',checkedAt:new Date().toISOString()})});
 assert.equal((await post({server:server_,table:'purchase_checks',rows:[check(1),check(2)]})).json.accepted,2);
 assert.equal((await post({server:server_,table:'purchase_checks',rows:[check(2),check(3)]},true)).json.accepted,2,'gzip gövde kabul edilir');
 assert.equal(mirror.prepare('SELECT COUNT(*) n FROM purchase_checks').get().n,3,'aynı kontrol kaydı tekrar gelirse çiftlenmez');
 assert.equal((await post({server:server_,table:'purchase_checks',rows:[{id:9,payload:'{'}]})).status,400,'bozuk kontrol kaydı reddedilir');
 assert.equal((await post({server:server_,table:'bilinmeyen',rows:[]})).status,400,'bilinmeyen tablo reddedilir');
 assert.equal((await post({server:'Yok',table:'purchase_checks',rows:[]})).status,400,'bilinmeyen sunucu reddedilir');
 await post({server:server_,table:'compact_presence',rows:[{key:'a',session:'s1',vid:22,lastSeen:'2026-10-17T10:00:00Z'},{key:'b',session:'s1',vid:22,lastSeen:'2026-10-17T10:00:01Z'}]});
 await post({server:server_,table:'compact_presence',rows:[{key:'a',session:'s2',vid:23,lastSeen:'2026-10-17T11:00:00Z'}]});
 assert.deepEqual({...mirror.prepare("SELECT session,vid FROM compact_presence WHERE key='a'").get()},{session:'s2',vid:23},'varlık kaydı son görülene güncellenir');
 await post({server:server_,table:'trade_runs',rows:[{started:'2026-10-17T10:00:00Z',planJson:JSON.stringify({categoryTree:{jobs:{}}})}]});
 const state=(await call('GET','/ingest/v1/state?server=Safir',Buffer.alloc(0),useK2())).json;
 assert.equal(state.purchaseChecksCursor,3);assert.deepEqual({...state.presenceCursor},{lastSeen:'2026-10-17T11:00:00Z',key:'a'});assert.equal(state.categories[rows[0].category_key].received,1);assert.equal(state.datasetId,fixture.metadata.dataset_id);

 const web=fs.mkdtempSync(path.join(__dirname,'data/ingest-e2e-')),incoming=path.join(web,'incoming');fs.mkdirSync(incoming);
 fs.cpSync(path.join(sourceRoot,'safir'),path.join(incoming,'safir'),{recursive:true});
 mirror.close();
 server.close();
 const second=createIngestServer({sourceRoot:incoming,keysFile,stateDir:path.join(work,'state2'),now:()=>clock});
 await new Promise(r=>second.server.listen(0,'127.0.0.1',r));const base2=`http://127.0.0.1:${second.server.address().port}`;
 const call2=async(method,target,body=Buffer.alloc(0),headers={})=>{const auth=signRequest(SECRET_B,method,target,body,{timestamp:Math.floor(clock/1000)});const r=await fetch(base2+target,{method,body:method==='GET'?undefined:body,headers:{'X-M2-Key-Id':'k2',...auth,...headers}});return {status:r.status,json:await r.json()}};
 const live=new LiveSync(web,incoming,()=>{},__dirname);clearInterval(live.timer);const importer=live.states.get('safir'),observations=[];
 for(let i=0;i<rows.length;i++){
  const r=rows[i];
  if(i>0)assert.equal((await call2('POST','/ingest/v1/batch',Buffer.from(r.bodyBase64,'base64'),batchHeaders(r))).json.status,'stored');
  live.tick();assert.equal(live.metrics.errors,0,'içe aktarıcı hatasız çalıştı');
  const acks=(await call2('GET','/ingest/v1/acks?server=Safir')).json.acks;
  assert.ok(acks.some(a=>a.categoryKey===r.category_key&&a.revision===r.revision&&a.batchId===r.batch_id&&a.bodyHash===r.body_hash),'importer commit sonrası ACK sunucudan alınabilir: revision '+r.revision);
  observations.push({active:importer.db.prepare('SELECT COUNT(*) n FROM active_items').get().n,history:importer.db.prepare('SELECT COUNT(*) n FROM observations').get().n});
 }
 const freed=importer.source.prepare('SELECT COUNT(*) n FROM category_outbox WHERE body IS NULL').get().n;
 assert.equal(freed,rows.length-0,'onaylanan paketlerin gövdesi sunucu aynasında boşaltılır (disk)');
 assert.deepEqual(observations.map(r=>r.active),[1,1,1,2,2,0],'aynalanmış kaynaktan web, yerel kaynakla aynı aktif sayıları üretir');
 assert.deepEqual(observations.map(r=>r.history),[1,1,2,3,3,3],'geçmiş sayıları da aynı');
 importer.source.close();importer.db.close();second.server.close();
 for(const dir of [web,work])try{fs.rmSync(dir,{recursive:true,force:true,maxRetries:3})}catch{ }
 console.log('Ingest: signing vector, key rotation, replay/clock/throttle, size limits, idempotent batches, rows, ACK fetch and importer end-to-end passed.');
})().catch(error=>{console.error(error);process.exit(1)});
