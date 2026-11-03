const assert=require('node:assert/strict'),crypto=require('node:crypto'),zlib=require('node:zlib'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {DatabaseSync}=require('node:sqlite'),{SCHEMA,putScene,decodeScene}=require('./shop-scene.cjs'),{createIngestServer,signRequest}=require('./ingest.cjs');
const now=Date.now(),appearance='a'.repeat(64),secret='42'.repeat(32);
function scene(x=1000){return {schemaVersion:1,server:'Safir',channel:1,map:'metin2_map_privateshop',worldSession:'fixture-epoch',observedAt:new Date(now).toISOString(),expiresAfterSeconds:900,coordinateSystem:'map-local-api-pixel',baseX:0,baseY:358400,width:51200,height:51200,appearances:{[appearance]:{race:30000,titleStyle:19,board:{class:'PrivateShopTitleBar'}}},shops:[{vid:42,owner:'fixture',x,y:2000,z:16383.5,title:'fixture title',appearance,observedAt:new Date(now).toISOString()}]}}
function packet(s){const b=zlib.brotliCompressSync(Buffer.from(JSON.stringify(s)));return {channel:s.channel,map:s.map,observed:s.observedAt,bodyHash:crypto.createHash('sha256').update(b).digest('hex'),body:b.toString('base64')}}
(async()=>{
 const db=new DatabaseSync(':memory:');db.exec(SCHEMA);
 putScene(db,packet(scene()),'Safir',now);putScene(db,packet(scene(1200)),'Safir',now);
 assert.equal(db.prepare('SELECT COUNT(*) n FROM shop_scene_current').get().n,1);
 assert.equal(decodeScene(packet(scene(1200)),'Safir',now).scene.shops[0].x,1200);
 const stale=scene(500);stale.observedAt=new Date(now-1000).toISOString();stale.shops[0].observedAt=stale.observedAt;
 assert.equal(putScene(db,packet(stale),'Safir',now),false);
 assert.throws(()=>decodeScene({...packet(scene()),bodyHash:'0'.repeat(64)},'Safir',now));
 assert.throws(()=>decodeScene(packet(scene()),'Ruby',now));
 const duplicate=scene();duplicate.shops.push(duplicate.shops[0]);assert.throws(()=>decodeScene(packet(duplicate),'Safir',now));
 const nan=scene();nan.shops[0].x=null;assert.throws(()=>decodeScene(packet(nan),'Safir',now));
 const old=scene();old.shops[0].observedAt=new Date(now-901000).toISOString();assert.throws(()=>decodeScene(packet(old),'Safir',now));
 const work=fs.mkdtempSync(path.join(os.tmpdir(),'m2-shop-scene-'));const keys=path.join(work,'keys.json');fs.writeFileSync(keys,JSON.stringify({keys:[{id:'fixture',secret,status:'active'}]}));
 const {server,ingest}=createIngestServer({sourceRoot:path.join(work,'source'),keysFile:keys,stateDir:path.join(work,'state'),now:()=>now});
 try{
  await new Promise(r=>server.listen(0,'127.0.0.1',r));const url=`http://127.0.0.1:${server.address().port}`;
  const call=async(method,target,body)=>{const b=body?zlib.gzipSync(Buffer.from(JSON.stringify(body))):Buffer.alloc(0);return fetch(url+target,{method,body:method==='POST'?b:undefined,headers:{'X-M2-Key-Id':'fixture',...signRequest(secret,method,target,b,{timestamp:Math.floor(now/1000)}),...(body?{'Content-Encoding':'gzip'}:{})}})};
  const state=await(await call('GET','/ingest/v1/state?server=Safir')).json();assert.equal(state.shopSceneProtocol,1);
  for(const x of [1000,1300])assert.equal((await call('POST','/ingest/v1/rows',{server:'Safir',table:'shop_scene_current',rows:[packet(scene(x))]})).status,200);
  const mirror=ingest.mirror('Safir').db;assert.equal(mirror.prepare('SELECT COUNT(*) n FROM shop_scene_current').get().n,1);
  const row=mirror.prepare('SELECT body FROM shop_scene_current').get();assert.equal(JSON.parse(zlib.brotliDecompressSync(row.body)).shops[0].x,1300);
  assert.equal((await call('POST','/ingest/v1/rows',{server:'Safir',table:'shop_scene_current',rows:[{...packet(scene()),bodyHash:'0'.repeat(64)}]})).status,400);
  assert.equal(mirror.prepare('SELECT COUNT(*) n FROM shop_scene_current').get().n,1);
 }finally{await new Promise(r=>server.close(r));ingest.close();db.close();fs.rmSync(work,{recursive:true,force:true})}
 console.log('Shop scene: latest-only replacement, Brotli/hash, context, stale packet, invalid coordinates, duplicate VID and signed HTTP roundtrip passed.');
})().catch(e=>{console.error(e);process.exitCode=1});
