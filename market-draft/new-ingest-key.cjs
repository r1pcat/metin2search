const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const id=process.argv[2],dir=path.resolve(process.argv[3]||'.');
if(!/^[A-Za-z0-9_-]{1,32}$/.test(id||'')){console.error('kullanım: node new-ingest-key.cjs <anahtar-kimliği> [klasör]');process.exit(2)}
const secret=crypto.randomBytes(32).toString('hex');
fs.mkdirSync(dir,{recursive:true});
const keyFile=path.join(dir,'uplink.key'),serverFile=path.join(dir,'ingest-keys.json');
for(const f of [keyFile,serverFile])if(fs.existsSync(f)){console.error(f+' zaten var; üzerine yazılmadı. Başka klasör seçin.');process.exit(1)}
fs.writeFileSync(keyFile,secret+'\n',{mode:0o600});
fs.writeFileSync(serverFile,JSON.stringify({keys:[{id,secret,status:'active'}]},null,2)+'\n',{mode:0o600});
console.log(`Anahtar "${id}" oluşturuldu:\n  ${keyFile}\n  ${serverFile}\nBu dosyaları sohbete/rapora koymayın; sunucuya güvenli kanaldan (scp) taşıyın.`);
