const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib'),crypto=require('node:crypto'),{pipeline}=require('node:stream/promises'),{DatabaseSync}=require('node:sqlite');
const args=(()=>{const a=process.argv.slice(2),o={force:a.includes('--force')};for(let i=0;i<a.length;i++)if(a[i].startsWith('--')&&a[i]!=='--force')o[a[i].slice(2)]=a[++i];return o})();
const fail=m=>{console.error('HATA: '+m);process.exit(1)};
if(!args.bundle)fail('--bundle <klasör> gerekli');
const bundle=path.resolve(args.bundle),manifestFile=path.join(bundle,'manifest.json'),packed=path.join(bundle,'web.sqlite.br');
if(!fs.existsSync(manifestFile)||!fs.existsSync(packed))fail('Paket eksik (manifest.json / web.sqlite.br).');
const m=JSON.parse(fs.readFileSync(manifestFile,'utf8'));
if(m.schemaVersion!==1||!/^[a-z0-9-]{1,64}$/.test(m.serverId||'')||!/^[0-9a-f]{64}$/.test(m.rawSha256||'')||!/^[0-9a-f]{64}$/.test(m.packedSha256||''))fail('manifest.json geçersiz.');
const target=path.resolve(args.target||path.join(__dirname,'data','live')),dest=path.join(target,m.serverId+'.sqlite'),temp=dest+'.restore.tmp';
if(fs.existsSync(dest)&&!args.force)fail(`${dest} zaten var; üzerine yazılmadı. Bilerek değiştirmek için --force (eski dosya .before-restore olarak kalır).`);
const hash=file=>new Promise((res,rej)=>{const h=crypto.createHash('sha256');fs.createReadStream(file).on('data',d=>h.update(d)).on('end',()=>res(h.digest('hex'))).on('error',rej)});
(async()=>{
 if(fs.statSync(packed).size!==m.packedBytes||await hash(packed)!==m.packedSha256)fail('Sıkıştırılmış dosyanın boyutu/özeti manifestle uyuşmuyor (kopyalama bozulmuş olabilir).');
 fs.mkdirSync(target,{recursive:true});
 const free=(()=>{try{const s=fs.statfsSync(target);return Number(s.bavail)*Number(s.bsize)}catch{return Infinity}})();if(free<m.rawBytes*1.2)fail('Yeterli disk alanı yok.');
 try{await pipeline(fs.createReadStream(packed),zlib.createBrotliDecompress(),fs.createWriteStream(temp))}catch(e){fs.rmSync(temp,{force:true});fail('Açma hatası: '+e.message)}
 if(fs.statSync(temp).size!==m.rawBytes||await hash(temp)!==m.rawSha256){fs.rmSync(temp,{force:true});fail('Açılan veritabanının özeti manifestle uyuşmuyor.')}
 const db=new DatabaseSync(temp,{readOnly:true});
 const ok=db.prepare('PRAGMA integrity_check').get().integrity_check,dataset=db.prepare("SELECT value FROM meta WHERE key='source_dataset'").get()?.value,items=Number(db.prepare('SELECT COUNT(*) n FROM items').get().n);db.close();
 if(ok!=='ok'||dataset!==m.dataset||items!==m.counts.items){fs.rmSync(temp,{force:true});fail('Veritabanı doğrulaması başarısız (bütünlük/veri kümesi/ilan sayısı).')}
 if(fs.existsSync(dest)){fs.renameSync(dest,dest+'.before-restore');for(const s of ['-wal','-shm'])if(fs.existsSync(dest+s))fs.renameSync(dest+s,dest+s+'.before-restore')}
 fs.renameSync(temp,dest);
 console.log(`Geri yüklendi: ${dest}\n  ${m.server}: ilan ${m.counts.items} (aktif ${m.counts.active}), geçmiş ${m.counts.observations}, veri kümesi ${m.dataset}\n  Sıradaki: ingest servisini başlatın, tarayıcı makinesinde uplink ayarını açın, siteyi başlatın.`);
})().catch(e=>fail(e.message));
