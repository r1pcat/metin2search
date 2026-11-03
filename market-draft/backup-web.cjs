const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib'),crypto=require('node:crypto'),{pipeline}=require('node:stream/promises'),{DatabaseSync}=require('node:sqlite');
const dataRoot=process.env.DATA_ROOT||__dirname,liveDir=path.join(dataRoot,'data','live'),outDir=path.resolve(process.env.BACKUP_DIR||path.join(dataRoot,'data','backups')),keep=Math.max(1,Number(process.env.BACKUP_KEEP)||3);
const fail=m=>{console.error('HATA: '+m);process.exit(1)};
(async()=>{
 if(!fs.existsSync(liveDir))fail('Site veritabanı klasörü yok: '+liveDir);
 fs.mkdirSync(outDir,{recursive:true});
 const stamp=new Date().toISOString().replace(/[-:]/g,'').slice(0,13).replace('T','-');
 const files=fs.readdirSync(liveDir).filter(n=>/^[a-z0-9-]+\.sqlite$/.test(n));
 if(!files.length)fail('Yedeklenecek veritabanı yok.');
 for(const name of files){
  const id=name.replace(/\.sqlite$/,''),source=path.join(liveDir,name),tmp=path.join(outDir,`${id}-${stamp}.sqlite.tmp`),packed=path.join(outDir,`${id}-${stamp}.sqlite.br`);
  const free=(()=>{try{const s=fs.statfsSync(outDir);return Number(s.bavail)*Number(s.bsize)}catch{return Infinity}})();
  if(free<fs.statSync(source).size*1.5){console.error(`${id}: yeterli disk alanı yok, atlandı`);continue}
  const db=new DatabaseSync(source,{readOnly:true});db.exec('PRAGMA busy_timeout=60000');
  try{db.exec(`VACUUM INTO '${tmp.replace(/'/g,"''")}'`)}finally{db.close()}
  const check=new DatabaseSync(tmp,{readOnly:true});const ok=check.prepare('PRAGMA integrity_check').get().integrity_check;check.close();
  if(ok!=='ok'){fs.rmSync(tmp,{force:true});console.error(`${id}: bütünlük denetimi başarısız, yedek atlandı`);continue}
  await pipeline(fs.createReadStream(tmp),zlib.createBrotliCompress({params:{[zlib.constants.BROTLI_PARAM_QUALITY]:5}}),fs.createWriteStream(packed));
  const h=crypto.createHash('sha256');await pipeline(fs.createReadStream(packed),async function*(s){for await(const c of s){h.update(c);yield c}},fs.createWriteStream(packed+'.sha256.tmp'));fs.rmSync(packed+'.sha256.tmp',{force:true});
  fs.writeFileSync(packed+'.sha256',h.digest('hex')+'  '+path.basename(packed)+'\n');fs.rmSync(tmp);
  console.log(`${id}: ${(fs.statSync(packed).size/1048576).toFixed(0)} MB → ${packed}`);
  const old=fs.readdirSync(outDir).filter(n=>n.startsWith(id+'-')&&n.endsWith('.sqlite.br')).sort().reverse().slice(keep);
  for(const n of old){fs.rmSync(path.join(outDir,n),{force:true});fs.rmSync(path.join(outDir,n+'.sha256'),{force:true});console.log('silindi (eski yedek): '+n)}
 }
})().catch(e=>fail(e.message));
