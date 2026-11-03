const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
const file=path.resolve(process.argv[2]||''),apply=process.argv.includes('--apply');
if(!process.argv[2]||!fs.existsSync(file)){console.error('kullanım: node vacuum-web.cjs <sqlite-dosyası> [--apply]');process.exit(2)}
const mb=b=>(b/1048576).toFixed(0)+' MB',size=fs.statSync(file).size,temp=file+'.vacuum.tmp';
const free=(()=>{try{const s=fs.statfsSync(path.dirname(file));return Number(s.bavail)*Number(s.bsize)}catch{return Infinity}})();
if(free<size*1.3){console.error(`Yetersiz disk alanı: ${mb(free)} boş, en az ${mb(size*1.3)} gerekli.`);process.exit(1)}
if(apply&&(fs.existsSync(file+'-wal')&&fs.statSync(file+'-wal').size>0)){console.error('Veritabanının -wal dosyası dolu: site hâlâ çalışıyor olabilir. Önce siteyi durdurun.');process.exit(1)}
if(fs.existsSync(temp))fs.rmSync(temp);
const db=new DatabaseSync(file,{readOnly:!apply});db.exec('PRAGMA busy_timeout=30000');
db.exec(`VACUUM INTO '${temp.replace(/'/g,"''")}'`);db.close();
const check=new DatabaseSync(temp,{readOnly:true});const ok=check.prepare('PRAGMA integrity_check').get().integrity_check;const items=check.prepare('SELECT COUNT(*) n FROM items').get().n;check.close();
if(ok!=='ok'){fs.rmSync(temp);console.error('Sıkıştırılmış kopya bütünlük denetiminden geçmedi; hiçbir şey değiştirilmedi.');process.exit(1)}
const after=fs.statSync(temp).size;console.log(`Önce ${mb(size)} → sonra ${mb(after)} (kazanç ${mb(size-after)}), ilan satırı ${items}, bütünlük ok.`);
if(!apply){fs.rmSync(temp);console.log('Deneme modu: hiçbir dosya değişmedi. Uygulamak için --apply ekleyin (site durdurulmuşken).');process.exit(0)}
fs.renameSync(file,file+'.before-vacuum');for(const s of ['-wal','-shm'])if(fs.existsSync(file+s))fs.renameSync(file+s,file+s+'.before-vacuum');
fs.renameSync(temp,file);console.log('Uygulandı. Eski dosya: '+file+'.before-vacuum (siteyi başlatıp doğruladıktan sonra silebilirsiniz).');
