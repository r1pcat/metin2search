const crypto=require('node:crypto'),zlib=require('node:zlib');
const SCHEMA=`CREATE TABLE IF NOT EXISTS shop_scene_current(channel INTEGER NOT NULL,map TEXT NOT NULL,world_session TEXT NOT NULL,observed TEXT NOT NULL,body_hash TEXT NOT NULL,body BLOB NOT NULL,PRIMARY KEY(channel,map));`;
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
function decodeScene(row,server,now=Date.now()){
 if(!row||row.channel!==1||row.map!=='metin2_map_privateshop'||typeof row.body!=='string'||row.body.length>2*1024*1024||!/^([A-Za-z0-9+/]{4})*([A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(row.body)||!/^[a-f0-9]{64}$/.test(row.bodyHash||''))throw Error('Pazar sahnesi zarfı geçersiz');
 const bytes=Buffer.from(row.body,'base64');if(hash(bytes)!==row.bodyHash)throw Error('Pazar sahnesi hash uyuşmazlığı');
 const s=JSON.parse(zlib.brotliDecompressSync(bytes,{maxOutputLength:4*1024*1024}));
 const date=Date.parse(s.observedAt);
 if(s.schemaVersion!==1||s.server!==server||s.channel!==row.channel||s.map!==row.map||s.observedAt!==row.observed||!Number.isFinite(date)||date>now+300000||typeof s.worldSession!=='string'||s.worldSession.length>100||s.expiresAfterSeconds!==900||s.coordinateSystem!=='map-local-api-pixel'||!Array.isArray(s.shops)||s.shops.length>10000||!s.appearances||typeof s.appearances!=='object'||Array.isArray(s.appearances))throw Error('Pazar sahnesi bağlamı geçersiz');
 for(const key of ['width','height'])if(!Number.isInteger(s[key])||s[key]<25600||s[key]>256000)throw Error('Harita sınırı geçersiz');
 for(const key of ['baseX','baseY'])if(!Number.isSafeInteger(s[key])||s[key]<0)throw Error('Harita tabanı geçersiz');
 const ids=new Set();
 for(const [key,value] of Object.entries(s.appearances))if(!/^[a-f0-9]{64}$/.test(key)||!value||!Number.isInteger(value.race)||value.race<0||value.race>0xffffffff||Buffer.byteLength(JSON.stringify(value))>8192)throw Error('Pazar görünümü geçersiz');
 for(const p of s.shops){
  if(!Number.isSafeInteger(p.vid)||p.vid<1||p.vid>0xffffffff||ids.has(p.vid)||typeof p.owner!=='string'||p.owner.length>64||!(p.title===null||typeof p.title==='string'&&p.title.length<=256)||!Object.hasOwn(s.appearances,p.appearance)||![p.x,p.y,p.z].every(Number.isFinite)||p.x<0||p.y<0||p.x>s.width||p.y>s.height||Math.abs(p.z)>1000000)throw Error('Pazar konumu geçersiz');
  const seen=Date.parse(p.observedAt);if(!Number.isFinite(seen)||seen>date||seen<date-900000)throw Error('Pazar gözlem zamanı geçersiz');ids.add(p.vid);
 }
 return {scene:s,bytes};
}
function putScene(db,row,server,now=Date.now()){
 const {scene,bytes}=decodeScene(row,server,now);
 const prior=db.prepare('SELECT observed FROM shop_scene_current WHERE channel=? AND map=?').get(scene.channel,scene.map);
 if(prior&&Date.parse(prior.observed)>Date.parse(scene.observedAt))return false;
 db.prepare('INSERT INTO shop_scene_current VALUES(?,?,?,?,?,?) ON CONFLICT(channel,map) DO UPDATE SET world_session=excluded.world_session,observed=excluded.observed,body_hash=excluded.body_hash,body=excluded.body').run(scene.channel,scene.map,scene.worldSession,scene.observedAt,row.bodyHash,bytes);return true;
}
module.exports={SCHEMA,decodeScene,putScene};
