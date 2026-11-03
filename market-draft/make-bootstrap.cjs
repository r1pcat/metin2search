const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib'),crypto=require('node:crypto'),{pipeline}=require('node:stream/promises'),{DatabaseSync}=require('node:sqlite');
const {resolveServer}=require('./servers.cjs');
const args=(()=>{const a=process.argv.slice(2),o={force:a.includes('--force')};for(let i=0;i<a.length;i++)if(a[i].startsWith('--')&&a[i]!=='--force')o[a[i].slice(2)]=a[++i];return o})();
const fail=m=>{console.error('HATA: '+m);process.exit(1)};
const server=resolveServer(args.server||'');if(!server)fail('--server <safir|...> gerekli');
const root=__dirname;
const webFile=path.resolve(args.web||path.join(root,'data','live',server.id+'.sqlite'));
function findSource(){
 if(args.source)return path.resolve(args.source);
 const base=path.join(root,'..','scanner-data','Sunucular');
 for(const dir of fs.existsSync(base)?fs.readdirSync(base):[]){const file=path.join(base,dir,'market.sqlite');if(!fs.existsSync(file))continue;
  try{const db=new DatabaseSync(file,{readOnly:true});const name=db.prepare("SELECT value FROM trade_metadata WHERE key='server'").get()?.value;db.close();if(resolveServer(name)?.id===server.id)return file}catch{}}
 return null;
}
const sourceFile=findSource();if(!sourceFile||!fs.existsSync(sourceFile))fail('Tarayıcı kaynak veritabanı bulunamadı (--source verin).');
if(!fs.existsSync(webFile))fail('Site veritabanı yok: '+webFile);

const source=new DatabaseSync(sourceFile,{readOnly:true});source.exec('PRAGMA busy_timeout=20000');
const web=new DatabaseSync(webFile,{readOnly:true});web.exec('PRAGMA busy_timeout=20000');
const dataset=source.prepare("SELECT value FROM trade_metadata WHERE key='dataset_id'").get()?.value;
const webDataset=web.prepare("SELECT value FROM meta WHERE key='source_dataset'").get()?.value;
if(!dataset||dataset!==webDataset)fail(`Veri kümesi uyuşmuyor (tarayıcı: ${dataset}, site: ${webDataset}).`);
const problems=[];
const pending=Number(source.prepare("SELECT COUNT(*) n FROM category_outbox WHERE acked_at IS NULL AND body IS NOT NULL").get().n);
if(pending)problems.push(`Tarayıcı kuyruğunda onay bekleyen ${pending} paket var: taramayı durdurun, sitenin işlemesini bekleyin.`);
const scanner=new Map(source.prepare('SELECT category_key,revision FROM category_delivery_state').all().map(r=>[r.category_key,Number(r.revision)]));
const received=new Map(web.prepare('SELECT category_key,MAX(revision) r FROM category_received WHERE dataset=? GROUP BY category_key').all(dataset).map(r=>[r.category_key,Number(r.r)]));
const behind=[...scanner].filter(([k,v])=>(received.get(k)||0)<v);
if(behind.length)problems.push(`Site ${behind.length} kategoride tarayıcının gerisinde (ör. ${behind[0][0]}: tarayıcı ${behind[0][1]}, site ${received.get(behind[0][0])||0}).`);
if(problems.length&&!args.force){for(const p of problems)console.error('UYARI: '+p);fail('Anlık görüntü tutarlı değil; düzeltin veya bilerek --force kullanın.')}

const stamp=new Date().toISOString().replace(/[-:]/g,'').slice(0,13).replace('T','-');
const outBase=path.resolve(args.out||path.join(root,'data','bootstrap')),outDir=path.join(outBase,`${server.id}-${stamp}`);
fs.mkdirSync(outDir,{recursive:true});
const raw=path.join(outDir,'web.sqlite.tmp'),packed=path.join(outDir,'web.sqlite.br');
const need=fs.statSync(webFile).size*1.5,free=(()=>{try{const s=fs.statfsSync(outDir);return Number(s.bavail)*Number(s.bsize)}catch{return Infinity}})();
if(free<need)fail('Yeterli disk alanı yok.');
web.exec(`VACUUM INTO '${raw.replace(/'/g,"''")}'`);
const snap=new DatabaseSync(raw,{readOnly:true});
const integrity=snap.prepare('PRAGMA integrity_check').get().integrity_check;if(integrity!=='ok')fail('Anlık görüntü bütünlük denetiminden geçmedi.');
const n=s=>Number(snap.prepare(s).get().n);
const counts={items:n('SELECT COUNT(*) n FROM items'),active:n('SELECT COUNT(*) n FROM active_items'),observations:n('SELECT COUNT(*) n FROM observations'),categories:n('SELECT COUNT(DISTINCT category_key) n FROM category_received')};
snap.close();source.close();web.close();
const hash=file=>new Promise((res,rej)=>{const h=crypto.createHash('sha256');fs.createReadStream(file).on('data',d=>h.update(d)).on('end',()=>res(h.digest('hex'))).on('error',rej)});
(async()=>{
 const rawSha=await hash(raw);
 await pipeline(fs.createReadStream(raw),zlib.createBrotliCompress({params:{[zlib.constants.BROTLI_PARAM_QUALITY]:5,[zlib.constants.BROTLI_PARAM_SIZE_HINT]:fs.statSync(raw).size}}),fs.createWriteStream(packed));
 const manifest={schemaVersion:1,server:server.name,serverId:server.id,dataset,createdAt:new Date().toISOString(),rawBytes:fs.statSync(raw).size,rawSha256:rawSha,packedBytes:fs.statSync(packed).size,packedSha256:await hash(packed),counts,scannerRevisions:Object.fromEntries(scanner),forced:!!(problems.length&&args.force)};
 fs.writeFileSync(path.join(outDir,'manifest.json'),JSON.stringify(manifest,null,2));fs.rmSync(raw);
 console.log(`Hazır: ${outDir}\n  ${(manifest.rawBytes/1048576).toFixed(0)} MB → ${(manifest.packedBytes/1048576).toFixed(0)} MB, ilan ${counts.items} (aktif ${counts.active}), geçmiş ${counts.observations}, kategori ${counts.categories}\n  Sunucuya kopyalayın (scp -r) ve orada: node restore-bootstrap.cjs --bundle <klasör>`);
})().catch(e=>fail(e.message));
