const fs=require('node:fs'),path=require('node:path');
const EXEMPT=[/^\/yonetim(\.js)?$/,/^\/api\/yonetim(\/|$)/,/^\/api\/health$/,/^\/robots\.txt$/,/^\/favicon\./,/^\/assets\//];
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
function createMaintenance({file,now=()=>Date.now()}){
 let state={on:false,message:'',since:null};
 try{const j=JSON.parse(fs.readFileSync(file,'utf8'));state={on:!!j.on,message:String(j.message||'').slice(0,200),since:j.since||null}}catch{}
 function save(){try{fs.mkdirSync(path.dirname(file),{recursive:true});const tmp=file+'.'+process.pid+'.tmp';fs.writeFileSync(tmp,JSON.stringify(state));fs.renameSync(tmp,file)}catch(e){console.error('Bakım durumu yazılamadı:',e.message)}}
 return {
  get:()=>({...state}),
  set(on,message=''){state={on:!!on,message:on?String(message||'').replace(/\s+/g,' ').trim().slice(0,200):'',since:on?new Date(now()).toISOString():null};save();return {...state}},
  blocks:(pathname,local)=>state.on&&!local&&!EXEMPT.some(r=>r.test(pathname)),
  page(){const msg=state.message?`<p class="note">${esc(state.message)}</p>`:'';
   return `<!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex"><meta http-equiv="refresh" content="60"><title>Bakımdayız · Metin2Search</title><style>:root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#141517;color:#ecebe6;font:16px/1.6 Inter,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;padding:24px;text-align:center}main{max-width:480px}img{width:64px;height:64px;margin-bottom:18px}h1{margin:0 0 10px;font-size:26px}p{margin:0 0 10px;color:#a8a9ae}.note{color:#e8c46a;font-weight:600}small{color:#7d7f86}</style></head><body><main><img src="/assets/metin2-logo.png" alt=""><h1>Kısa bir bakımdayız</h1><p>Metin2Search şu an güncelleniyor. Birkaç dakika içinde geri döneceğiz.</p>${msg}<small>Bu sayfa her dakika kendini yeniler.</small></main></body></html>`}
 };
}
module.exports={createMaintenance,EXEMPT};
