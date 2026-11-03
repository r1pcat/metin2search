const fs=require('node:fs'),path=require('node:path');
const base=process.argv[2]||'http://127.0.0.1:4180',server='safir';
const get=async u=>{const r=await fetch(base+u);if(!r.ok)throw new Error(u+' -> '+r.status);return r.json()};
(async()=>{
  const meta=await get('/api/meta?server='+server);
  const pairs=(meta.categories||[]).filter(r=>r.count>0&&Number.isInteger(r.category)&&r.sub!==undefined&&r.sub!==null&&r.sub!=='').map(r=>[r.category,String(r.sub)]);
  const uniq=[...new Map(pairs.map(p=>[p.join(':'),p])).values()].sort((a,b)=>a[0]-b[0]||Number(a[1])-Number(b[1]));
  const icons={};let missing=0;
  for(const [c,s] of uniq){
    const r=await get('/api/items?'+new URLSearchParams({server,category:c,sub:s,sort:'total-desc',page:1}));
    const it=r.items?.[0];if(!it){missing++;continue}
    icons[c+':'+s]=it.icon||String(it.Vnum).padStart(5,'0')+'.png';
  }
  const body='/* Otomatik üretildi: node build-sub-icons.cjs  ('+new Date().toISOString().slice(0,10)+', '+Object.keys(icons).length+' alt kategori). Elle düzenleyebilirsin: "kategori:alt" -> /assets/items/ altındaki dosya adı. */\n'
   +'window.M2SubIcons='+JSON.stringify(icons,null,0).replace(/,"/g,',\n"')+';\n';
  fs.writeFileSync(path.join(__dirname,'sub-icons.js'),body);
  console.log('yazildi: sub-icons.js |',Object.keys(icons).length,'alt kategori |',missing,'bos (ilan yok)');
})().catch(e=>{console.error('HATA',e.message);process.exit(1)});
