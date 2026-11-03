const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const BOT=/bot|crawl|spider|slurp|bing|google|yandex|baidu|duckduck|facebookexternalhit|preview|monitor|uptime|pingdom|curl|wget|python|node-fetch|axios|httpclient|go-http|java\//i;
const PAGE_PATHS=new Set(['/','/pazar','/market','/veri-kaynagi','/cerez-politikasi','/gizlilik-politikasi','/kullanim-sartlari','/iletisim']);
const istDay=ms=>new Date(ms+3*3600e3).toISOString().slice(0,10);
const istHour=ms=>new Date(ms+3*3600e3).getUTCHours();
const bump=(o,k,n=1)=>{o[k]=(o[k]||0)+n};
function trim(o,max){const e=Object.entries(o);if(e.length<=max)return;e.sort((a,b)=>b[1]-a[1]);for(const [k] of e.slice(max))delete o[k]}
function emptyDay(day){return {day,views:0,botViews:0,visitors:0,apiCalls:0,searches:0,itemOpens:0,hours:Array(24).fill(0),pages:{},servers:{},categories:{},terms:{},countries:{},devices:{},referrers:{},status:{},ms:{n:0,sum:0}}}
function createVisitorStats({dir,secret=crypto.randomBytes(32).toString('hex'),now=()=>Date.now(),keepDays=90,flushMs=30000}={}){
 fs.mkdirSync(dir,{recursive:true});
 let day=null,data=null,seen=new Set(),dirty=false,timer=null;
 const file=d=>path.join(dir,d+'.json');
 function load(d){try{const j=JSON.parse(fs.readFileSync(file(d),'utf8'));return {...emptyDay(d),...j,hours:j.hours||Array(24).fill(0)}}catch{return emptyDay(d)}}
 function roll(){const d=istDay(now());if(d===day)return;if(data&&dirty)flush();day=d;data=load(d);seen=new Set();dirty=false;prune()}
 function prune(){try{const limit=istDay(now()-keepDays*86400e3);for(const f of fs.readdirSync(dir))if(/^\d{4}-\d{2}-\d{2}\.json$/.test(f)&&f.slice(0,10)<limit)fs.unlinkSync(path.join(dir,f))}catch{}}
 function flush(){if(!data||!dirty)return;try{const tmp=file(day)+'.'+process.pid+'.tmp';fs.writeFileSync(tmp,JSON.stringify(data));fs.renameSync(tmp,file(day));dirty=false}catch{}}
 const cleanTerm=q=>{const t=String(q||'').toLowerCase().replace(/\s+/g,' ').trim();if(t.length<2||t.length>60||/@|https?:|\d{6,}/.test(t))return null;return t};
 const device=ua=>BOT.test(ua)?'bot':/ipad|tablet/i.test(ua)?'tablet':/mobi|android|iphone/i.test(ua)?'mobil':'masaüstü';
 function record(e){roll();const t=now(),ua=String(e.ua||''),dev=device(ua),p=e.path;const isApi=p.startsWith('/api/');
  if(!isApi&&!PAGE_PATHS.has(p))return;
  if(isApi&&!['/api/items','/api/item','/api/container','/api/shop','/api/history'].includes(p))return;
  dirty=true;bump(data.status,String(e.status).slice(0,1)+'xx');
  if(isApi){data.apiCalls++;if(p==='/api/items'&&dev!=='bot'){data.searches++;const term=cleanTerm(e.q);if(term){bump(data.terms,term);if(Object.keys(data.terms).length>600)trim(data.terms,400)}if(e.category&&/^\d{1,2}$/.test(e.category))bump(data.categories,e.category);if(e.server&&/^[a-z0-9-]{2,20}$/.test(e.server))bump(data.servers,e.server)}if(p==='/api/item'&&dev!=='bot')data.itemOpens++;data.ms.n++;data.ms.sum+=e.ms||0;return}
  if(e.status!==200)return;
  if(dev==='bot'){data.botViews++;return}
  data.views++;data.hours[istHour(t)]++;bump(data.pages,p==='/market'?'/pazar':p);bump(data.devices,dev);
  const c=/^[A-Z]{2}$/.test(e.country||'')?e.country:'??';bump(data.countries,c);
  let ref='doğrudan';try{if(e.referrer){const host=new URL(e.referrer).hostname.replace(/^www\./,'');ref=host&&!/(^|\.)metin2search\.|^(localhost|127\.0\.0\.1)$/i.test(host)?host:null}}catch{}if(ref)bump(data.referrers,ref);
  const h=crypto.createHmac('sha256',secret+day).update(String(e.ip||'')+'|'+ua).digest('base64').slice(0,12);if(!seen.has(h)){seen.add(h);data.visitors++}
  if(Object.keys(data.referrers).length>200)trim(data.referrers,100)}
 function snapshot(days=30){roll();flush();const out=[];const today=now();for(let i=0;i<Math.min(90,Math.max(1,days));i++){const d=istDay(today-i*86400e3);out.push(d===day?JSON.parse(JSON.stringify(data)):load(d))}return out.reverse()}
 timer=setInterval(flush,flushMs);timer.unref?.();
 return {record,snapshot,flush,close(){clearInterval(timer);flush()},seenCount:()=>seen.size,_file:file}
}
module.exports={createVisitorStats,istDay};
