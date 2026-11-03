const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const {BoundedCache,encoding,makeEntry,sendEntry,serveStatic}=require('./http-performance.cjs');
const {createGuard}=require('./guard.cjs'),guard=createGuard();
const {DatabaseSync}=require('node:sqlite');
const {publicContainer}=require('./container-response.cjs');
const {homeSummary}=require('./home-summary.cjs');
const {createDashboardHistory}=require('./dashboard-history.cjs');
const root=__dirname,runtimeRoot=process.env.DATA_ROOT?path.resolve(process.env.DATA_ROOT):root,port=Number(process.env.PORT||4180);
fs.mkdirSync(path.join(runtimeRoot,'data'),{recursive:true});
const responseCache=new BoundedCache(128*1024*1024,2048),eventClients=new Set();
const dashboardHistory=createDashboardHistory(path.join(runtimeRoot,'data','dashboard-metrics.sqlite'));
const {monitorEventLoopDelay}=require('node:perf_hooks'),eventLoopDelay=monitorEventLoopDelay({resolution:20});eventLoopDelay.enable();
const serviceMetrics={started:new Date().toISOString(),requests:0,cacheHits:0,routes:{}};
const {Worker}=require('node:worker_threads'),{catalog}=require('./servers.cjs');
let liveStatus={started:null,until:null,running:true,intervalSeconds:60,metrics:{},servers:catalog.map(s=>({...s,available:false,revision:0}))};
const sync={states:new Map(),status:()=>liveStatus,get until(){return liveStatus.until},get stopped(){return !liveStatus.running}};
for(const server of catalog){const file=path.join(runtimeRoot,'data','live',server.id+'.sqlite');if(!fs.existsSync(file))continue;let connection;try{connection=new DatabaseSync(file,{readOnly:true});connection.exec('PRAGMA query_only=ON; PRAGMA busy_timeout=1000');const row=connection.prepare("SELECT value FROM meta WHERE key='snapshot'").get(),ready=connection.prepare("SELECT 1 FROM meta WHERE key='web_context'").get();if(!row||!ready){connection.close();continue}const metadata=JSON.parse(row.value);sync.states.set(server.id,{...server,db:connection,ready:true,available:true,revision:metadata.revision,latestSeen:metadata.latest});Object.assign(liveStatus.servers.find(s=>s.id===server.id),{available:true,revision:metadata.revision,lastSeen:metadata.latest})}catch{connection?.close()}}
if(process.env.IMPORT_PAUSED==='1')liveStatus={...liveStatus,running:false,intervalSeconds:60};
let importer,importerRestartTimer,shuttingDown=false;
function startImporter(){
 importer=new Worker(path.join(root,'live-worker.cjs'),{workerData:{root:runtimeRoot,definitionsRoot:root,sourceRoot:process.env.SOURCE_ROOT||path.join(root,'..','scanner-data','Sunucular')}});importer.on('message',status=>{liveStatus=status;for(const s of status.servers){const previous=sync.states.get(s.id);if(s.available){const connection=previous?.db||new DatabaseSync(path.join(runtimeRoot,'data','live',s.id+'.sqlite'),{readOnly:true});connection.exec('PRAGMA query_only=ON; PRAGMA busy_timeout=1000');sync.states.set(s.id,{...s,db:connection,ready:true,latestSeen:s.lastSeen});}}dashboardHistory.capture(status,sync.states);broadcastLive();});
 importer.on('error',error=>console.error('Canlı aktarım:',error.message));
 importer.on('exit',code=>{if(shuttingDown)return;console.error('Aktarım işlemi yeniden başlatılıyor:',code);liveStatus={...liveStatus,running:false,error:'Aktarım yeniden başlatılıyor'};broadcastLive();importerRestartTimer=setTimeout(startImporter,2000);importerRestartTimer.unref()});
}
let archiver,archiverRestartTimer,archiveStatus={enabled:false};
function startArchiver(){
 const config=require('./archive-storage.cjs').readConfig(root);if(!config?.automaticUpload||process.env.ARCHIVE_PAUSED==='1')return;
 archiver=new Worker(path.join(root,'archive-worker.cjs'),{workerData:{root,runtimeRoot,sourceRoot:process.env.SOURCE_ROOT||path.join(root,'..','scanner-data','Sunucular')}});
 archiver.on('message',status=>{archiveStatus=status});
 archiver.on('error',error=>{archiveStatus={enabled:true,error:error.message};console.error('Arşiv aktarımı:',error.message)});
 archiver.on('exit',()=>{if(!shuttingDown){archiverRestartTimer=setTimeout(startArchiver,30000);archiverRestartTimer.unref()}});
}
let dailyBackup,dailyBackupRestartTimer,dailyBackupStatus={enabled:false};
function startDailyBackup(){
 const config=require('./archive-storage.cjs').readConfig(root);if(!config?.automaticDailyBackup||process.env.BACKUP_PAUSED==='1'||process.env.ARCHIVE_PAUSED==='1')return;
 dailyBackup=new Worker(path.join(root,'daily-backup-worker.cjs'),{workerData:{root}});
 dailyBackup.on('message',status=>{dailyBackupStatus=status});
 dailyBackup.on('error',error=>{dailyBackupStatus={enabled:true,error:error.message};console.error('Günlük yedek:',error.message)});
 dailyBackup.on('exit',()=>{if(!shuttingDown){dailyBackupRestartTimer=setTimeout(startDailyBackup,30000);dailyBackupRestartTimer.unref()}});
}
if(process.env.IMPORT_PAUSED!=='1'){startImporter();startArchiver();startDailyBackup()}const source=path.join(root,'data/definitions');
const clientStats=JSON.parse(fs.readFileSync(path.join(source,'client-stat-map.json'),'utf8')),stats={...JSON.parse(fs.readFileSync(path.join(source,'stat_map.json'),'utf8')),...clientStats};
const bonusEditorRoute=require('./bonus-editor-store.cjs').createBonusEditorStore(root,stats);
const containerCatalog=JSON.parse(fs.readFileSync(path.join(root,'data/container-content.json'),'utf8')).entries;
const queryPool=require('./query-pool.cjs').createQueryPool({root,runtimeRoot,sourceRoot:process.env.SOURCE_ROOT||path.join(root,'..','scanner-data','Sunucular'),sourceDisabled:process.env.IMPORT_PAUSED==='1'},{size:Math.max(1,Math.min(4,Number(process.env.READ_WORKERS)||2))});
const readerPaths=new Set(['/api/meta','/api/items','/api/item','/api/search-suggestions','/api/history','/api/similar','/api/shop','/api/filter-relief','/api/stone','/api/unit-prices']),pendingResponses=new Map();
function metricPath(name){return readerPaths.has(name)||['/api/home','/api/servers','/api/status','/api/events','/api/container','/api/health'].includes(name)?name:'/api/other'}
function sendLive(client){if(client.res.destroyed)return;if(client.res.writableLength>65536){client.res.destroy();return}const state=liveStatus.servers.find(s=>s.id===client.server),tag=JSON.stringify([state?.revision,state?.lastSeen,liveStatus.running,liveStatus.servers.map(s=>s.available)]);if(client.tag===tag)return;client.tag=tag;client.res.write('event: state\ndata: '+JSON.stringify({server:client.server,revision:state?.revision||0,available:!!state?.available,lastSeen:state?.lastSeen||null,lastSync:state?.lastSync||null,testRunning:liveStatus.running,testUntil:liveStatus.until,servers:liveStatus.servers})+'\n\n')}
function broadcastLive(){for(const client of eventClients)sendLive(client)}
setInterval(()=>{for(const client of eventClients)client.res.write(': keepalive\n\n')},30000).unref();
function writeJson(res,entry,status=200){return sendEntry(res,entry,status)}
function json(res,obj,status=200){const entry=makeEntry(JSON.stringify(obj));if(res.cacheKey&&status===200)responseCache.set(res.cacheKey,entry);return writeJson(res,entry,status)}
const SECURITY_HEADERS={
 'X-Content-Type-Options':'nosniff','X-Frame-Options':'DENY','Referrer-Policy':'strict-origin-when-cross-origin',
 'Permissions-Policy':'camera=(), microphone=(), geolocation=(), payment=(), usb=()','Cross-Origin-Opener-Policy':'same-origin',
 'Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'"
};
function applySecurityHeaders(res){for(const [name,value]of Object.entries(SECURITY_HEADERS))res.setHeader(name,value)}
const SITE_PAGES=['/','/pazar','/veri-kaynagi','/cerez-politikasi','/gizlilik-politikasi','/kullanim-sartlari','/iletisim'];
function siteOrigin(req){const configured=process.env.SITE_ORIGIN;if(configured&&/^https?:\/\/[A-Za-z0-9.-]+(:\d+)?$/.test(configured))return configured;return 'http://'+String(req.headers.host||'localhost').replace(/[^A-Za-z0-9.:-]/g,'')}
const seo=require('./seo.cjs');
const {createVisitorStats}=require('./visitor-stats.cjs'),{createAdminAuth}=require('./admin-auth.cjs');
const visitorStats=createVisitorStats({dir:path.join(runtimeRoot,'data','stats'),secret:process.env.GUARD_SECRET||undefined});
process.on('exit',()=>visitorStats.flush());
const adminAuth=createAdminAuth({isLocal:ip=>guard.isLocal(ip)});
const maintenance=require('./maintenance.cjs').createMaintenance({file:path.join(runtimeRoot,'data','maintenance.json')});
function adminRoute(req,res,url){
 adminAuth.allowed(req).then(ok=>{
  if(!ok)return sendNotFound(req,res,url.pathname);
  res.setHeader('X-Robots-Tag','noindex, nofollow');res.setHeader('Cache-Control','no-store');
  if(url.pathname==='/yonetim')return serveStatic(req,res,path.join(root,'yonetim.html'));
  if(url.pathname==='/yonetim.js')return serveStatic(req,res,path.join(root,'yonetim.js'));
  if(url.pathname==='/api/yonetim/bakim'){if(req.headers['x-yonetim']!=='1')return json(res,{error:'Geçersiz istek'},400);const on=url.searchParams.get('set');if(on==='on'||on==='off'){const v=maintenance.set(on==='on',url.searchParams.get('message')||'');console.log('Bakım modu '+(v.on?'AÇILDI':'KAPATILDI'));return json(res,{maintenance:v})}return json(res,{maintenance:maintenance.get()})}
  const st=sync.status(),days=Math.max(1,Math.min(90,Number(url.searchParams.get('days'))||30));
  let disk=null;try{const s=fs.statfsSync(runtimeRoot),total=Number(s.blocks)*Number(s.bsize),free=Number(s.bavail)*Number(s.bsize);disk={freeGB:Math.round(free/1073741824*10)/10,usedPercent:total?Math.round((1-free/total)*100):null}}catch{}
  return json(res,{now:Date.now(),health:{ready:queryPool.metrics.workersReady>0,disk,uptimeSeconds:Math.round(process.uptime()),memoryRSS:process.memoryUsage().rss,queue:queryPool.metrics.queued,readWorkers:queryPool.metrics.workersReady,eventLoopP99Ms:Math.round(eventLoopDelay.percentile(99)/1e6)},servers:st.servers.map(({id,name,available,lastSeen,lastSync,error})=>({id,name,available,lastSeen,lastSync,error:!!error})),events:eventClients.size,maintenance:maintenance.get(),guard:guard.stats,routes:serviceMetrics.routes,days:visitorStats.snapshot(days)});
 }).catch(()=>sendNotFound(req,res,url.pathname));
}
const seoPages=new Set(['index.html','home.html','market-built.html','home-built.html','veri-kaynagi.html','cerez-politikasi.html','gizlilik-politikasi.html','kullanim-sartlari.html','iletisim.html']);
const seoCache=new Map();
async function serveSeoPage(req,res,file,url,name){
 try{const stat=await fs.promises.stat(file),pageName=name==='/'&&/home/.test(file)?'/':name==='/'?'/pazar':name,servers=seoServers(),key=file+':'+stat.mtimeMs+':'+pageName+':'+url.searchParams.get('server')+':'+[...url.searchParams.keys()].sort().join(',')+':'+servers.map(s=>s.id+s.available).join(',');
  let entry=seoCache.get(key);if(!entry){const html=injectSeoSafe(await fs.promises.readFile(file,'utf8'),{pathname:pageName,searchParams:url.searchParams,servers});entry=makeEntry(html,60000);seoCache.set(key,entry);if(seoCache.size>200)seoCache.delete(seoCache.keys().next().value)}
  return sendEntry(res,entry,200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-cache'});
 }catch(e){if(!res.headersSent)return serveStatic(req,res,file)}
}
function injectSeoSafe(html,ctx){try{return seo.injectSeo(html,ctx)}catch{return html}}
function seoServers(){try{return sync.status().servers.map(s=>({id:s.id,name:s.name,available:!!s.available}))}catch{return []}}
function serveSiteFiles(req,res,url){
 if(url.pathname==='/favicon.ico'){res.writeHead(301,{Location:'/favicon.svg','Cache-Control':'public, max-age=86400'});return res.end()}
 const text=url.pathname==='/robots.txt'?seo.robots():seo.sitemap(seoServers());
 res.writeHead(200,{'Content-Type':url.pathname==='/robots.txt'?'text/plain; charset=utf-8':'application/xml; charset=utf-8','Cache-Control':'public, max-age=3600'});return res.end(req.method==='HEAD'?undefined:text);
}
const PUBLIC_PAGES=['index.html','home.html','market-built.html','home-built.html','hata-404.html','veri-kaynagi.html','cerez-politikasi.html','gizlilik-politikasi.html','kullanim-sartlari.html','iletisim.html'],publicRootFiles=new Set(['favicon.svg','dil.css']);
const SOURCE_PAGES=['index.html','home.html'],productionMode=process.env.SERVE_BUILD==='1'||process.env.NODE_ENV==='production';
for(const p of PUBLIC_PAGES.filter(p=>!(productionMode&&SOURCE_PAGES.includes(p)))){let h='';try{h=fs.readFileSync(path.join(root,p),'utf8')}catch{continue}for(const m of h.matchAll(/(?:src|href)="\/?([^"#?:\/]+\.(?:css|js|svg|png|webp|ico))(?:[?#][^"]*)?"/g))publicRootFiles.add(m[1])}
const isPublicFile=file=>{const rel=path.relative(root,file).split(path.sep);return rel[0]==='assets'||(rel.length===1&&(publicRootFiles.has(rel[0])||PUBLIC_PAGES.includes(rel[0])&&!(productionMode&&SOURCE_PAGES.includes(rel[0]))))};
let notFoundEntry=null;
function sendNotFound(req,res,pathname){
 if(res.headersSent||res.destroyed)return;
 if(pathname.startsWith('/api/'))return json(res,{error:'Bulunamadı'},404);
 const wantsPage=!/\.[a-z0-9]{2,5}$/i.test(pathname)&&String(req.headers.accept||'').includes('text/html');
 if(!wantsPage){res.writeHead(404,{'Content-Type':'text/plain; charset=utf-8','Cache-Control':'no-store'});return res.end(res.headOnly?undefined:'Not found')}
 try{notFoundEntry??=makeEntry(fs.readFileSync(path.join(root,'hata-404.html')),3600000)}catch{res.writeHead(404,{'Content-Type':'text/plain; charset=utf-8'});return res.end('Not found')}
 return sendEntry(res,notFoundEntry,404,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','X-Robots-Tag':'noindex'});
}
const server=http.createServer((req,res)=>{applySecurityHeaders(res);res.responseEncoding=encoding(req.headers['accept-encoding']);res.requestTag=req.headers['if-none-match'];res.headOnly=req.method==='HEAD';let url;try{url=new URL(req.url,'http://localhost')}catch{res.writeHead(400);return res.end()}if(guard.handle(req,res,url))return;
 if(req.method==='GET'){const began=performance.now();res.on('finish',()=>{try{visitorStats.record({ip:req.clientIp,ua:req.headers['user-agent'],country:req.headers['cf-ipcountry'],path:url.pathname,status:res.statusCode,ms:performance.now()-began,referrer:req.headers.referer,q:url.searchParams.get('q'),category:url.searchParams.get('category'),server:url.searchParams.get('server')})}catch{}})}
 if(['/yonetim','/yonetim.js','/api/yonetim','/api/yonetim/bakim'].includes(url.pathname))return adminRoute(req,res,url);
 if(maintenance.blocks(url.pathname,guard.isLocal(req.clientIp))){const api=url.pathname.startsWith('/api/'),body=api?JSON.stringify({error:'Bakımdayız, kısa süre sonra döneceğiz.',maintenance:true}):maintenance.page();res.writeHead(503,{'Content-Type':api?'application/json; charset=utf-8':'text/html; charset=utf-8','Retry-After':'120','Cache-Control':'no-store','X-Robots-Tag':'noindex'});return res.end(res.headOnly?undefined:body)}
 if(url.pathname.startsWith('/api/')){serviceMetrics.requests++;const started=performance.now();res.on('finish',()=>{const elapsed=performance.now()-started,key=metricPath(url.pathname),m=serviceMetrics.routes[key]||(serviceMetrics.routes[key]={requests:0,totalMs:0,maxMs:0,errors:0});m.requests++;m.totalMs+=elapsed;m.maxMs=Math.max(m.maxMs,elapsed);m.errors+=Number(res.statusCode>=500)})}
 const handlerStarted=performance.now();
 try{
 if(bonusEditorRoute(req,res,url))return;
 if(url.pathname==='/api/events'){res.writeHead(200,{'Content-Type':'text/event-stream','Cache-Control':'no-cache, no-transform','Connection':'keep-alive','X-Accel-Buffering':'no'});res.write('retry: 15000\n\n');const client={res,server:url.searchParams.get('server')||'safir',tag:null};eventClients.add(client);sendLive(client);const beat=setInterval(()=>{if(res.destroyed||res.writableLength>65536)return;res.write(': ping\n\n')},25000);res.on('close',()=>{clearInterval(beat);eventClients.delete(client)});return}
 if(['/robots.txt','/sitemap.xml','/favicon.ico'].includes(url.pathname))return serveSiteFiles(req,res,url);
 if(url.pathname==='/api/health'){const ready=queryPool.metrics.workersReady>0;if(!guard.isLocal(req.clientIp))return json(res,{ready},ready?200:503);let disk=null;try{const s=fs.statfsSync(runtimeRoot),total=Number(s.blocks)*Number(s.bsize),free=Number(s.bavail)*Number(s.bsize);disk={freeGB:Math.round(free/1073741824*10)/10,usedPercent:total?Math.round((1-free/total)*100):null}}catch{}return json(res,{ready,disk,uptimeSeconds:Math.round(process.uptime()),availableServers:sync.states.size,importRunning:liveStatus.running,queue:queryPool.metrics.queued,readWorkers:queryPool.metrics.workersReady,memoryRSS:process.memoryUsage().rss,eventLoopP99Ms:Math.round(eventLoopDelay.percentile(99)/1e6)},ready?200:503)}
 if(url.pathname==='/api/container'){const key=url.searchParams.get('vnum')||'',entry=/^\d{1,9}$/.test(key)&&Object.hasOwn(containerCatalog,key)?containerCatalog[key]:null;if(!entry)return json(res,{error:'İçerik kaydı bulunamadı'},404);return json(res,publicContainer(entry))}
 if(url.pathname==='/api/home'){const status=sync.status();dashboardHistory.capture(status,sync.states);const key='home:'+status.running+':'+status.servers.map(s=>s.id+':'+s.available+':'+s.revision+':'+s.lastSeen+':'+s.lastSync+':'+!!s.error).join('|'),cached=responseCache.get(key);if(cached?.expires>Date.now())return writeJson(res,cached);res.cacheKey=key;return json(res,{...homeSummary(status,sync.states),history:dashboardHistory.read()})}
 if(url.pathname==='/api/backup-status')return guard.isLocal(req.clientIp)?json(res,dailyBackupStatus):sendNotFound(req,res,url.pathname);
 if(url.pathname==='/api/archive-status'&&!guard.isLocal(req.clientIp))return sendNotFound(req,res,url.pathname);
 if(url.pathname==='/api/archive-status')return json(res,{...archiveStatus,servers:(archiveStatus.servers||[]).map(({file,...row})=>row)});
 if(url.pathname==='/api/servers'){const st=sync.status();if(!guard.isLocal(req.clientIp)){const {metrics,intervalSeconds,until,...publicPart}=st;return json(res,publicPart)}return json(res,{...st,webMetrics:{...serviceMetrics,eventConnections:eventClients.size,queryPool:{...queryPool.metrics}}})}
 if(url.pathname==='/api/status'){const id=url.searchParams.get('server')||'safir',state=sync.states.get(id),tag='"'+id+':'+(state?.revision||0)+':'+(state?.latestSeen||'')+':'+sync.stopped+'"';if(req.headers['if-none-match']===tag){res.writeHead(304,{'ETag':tag,'Cache-Control':'no-cache'});return res.end()}res.setHeader('ETag',tag);return json(res,{server:id,revision:state?.revision||0,available:!!state?.ready,lastSync:state?.lastSync||null,lastSeen:state?.latestSeen||null,testUntil:sync.until,testRunning:!sync.stopped})}
 if(readerPaths.has(url.pathname)){const id=url.searchParams.get('server')||'safir',state=sync.states.get(id);if(!state?.ready)return json(res,{error:'Bu sunucuya henüz veri gelmedi',items:[],total:0,page:1,pages:1},409);const sorted=new URLSearchParams(url.searchParams);sorted.sort();const suffix=url.pathname+'?'+sorted,key=id+':'+state.revision+':'+suffix,cached=responseCache.get(key);if(cached){serviceMetrics.cacheHits++;return writeJson(res,cached)}
 let pending=pendingResponses.get(key);if(!pending){pending=queryPool.query(url.pathname+url.search).then(result=>{const entry=makeEntry(result.body);if(result.status===200)responseCache.set(id+':'+(result.revision??state.revision)+':'+suffix,entry);return {entry,status:result.status}}).finally(()=>pendingResponses.delete(key));pendingResponses.set(key,pending)}
 pending.then(({entry,status})=>{if(status===503)res.setHeader('Retry-After','2');return writeJson(res,entry,status)}).catch(error=>{console.error('Okuma yanıtı:',error.message);if(!res.destroyed&&!res.headersSent)json(res,{error:'İstek tamamlanamadı'},503)});return;
 } }catch(e){console.error(e.message);return json(res,{error:'Yerel kayıtlar okunamadı'},500)}finally{if(url.pathname.startsWith('/api/')&&url.pathname!=='/api/events'){const stats=serviceMetrics.synchronous||(serviceMetrics.synchronous={}),entry=stats[metricPath(url.pathname)]||(stats[metricPath(url.pathname)]={requests:0,totalMs:0,maxMs:0});const elapsed=performance.now()-handlerStarted;entry.requests++;entry.totalMs+=elapsed;entry.maxMs=Math.max(entry.maxMs,elapsed)}}
 let name;try{name=decodeURIComponent(url.pathname)}catch{res.writeHead(400);return res.end()};const DUP={'/index.html':'/pazar','/market-built.html':'/pazar','/home.html':'/','/home-built.html':'/','/market':'/pazar'};if(DUP[name]){res.writeHead(301,{Location:DUP[name]+url.search,'Cache-Control':'public, max-age=3600'});return res.end()};
 const landing=seo.LANGS.length?new RegExp('^\\/('+seo.LANGS.join('|')+')\\/?$').exec(name):null;if(landing){const html=seo.renderLanding(landing[1],seoServers());return sendEntry(res,makeEntry(html,300000),200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'public, max-age=300'})}const marketQuery=['server','category','q','seller','view','page','sort','bonusFilters','min','max','element','grade'].some(k=>url.searchParams.has(k));const page=name==='/'?(marketQuery?'/index.html':'/home.html'):['/pazar','/market'].includes(name)?'/index.html':({'/veri-kaynagi':'/veri-kaynagi.html','/cerez-politikasi':'/cerez-politikasi.html','/gizlilik-politikasi':'/gizlilik-politikasi.html','/kullanim-sartlari':'/kullanim-sartlari.html','/iletisim':'/iletisim.html'})[name]||name;const built=(process.env.SERVE_BUILD==='1'||process.env.NODE_ENV==='production')&&['/index.html','/home.html'].includes(page);const servedPage=built?(page==='/index.html'?'/market-built.html':'/home-built.html'):page;const file=path.resolve(root,'.'+servedPage);
 if(!file.startsWith(root+path.sep)||!(/\.(?:html|css|js|svg|png|jpe?g|webp|ico|woff2?|ttf)$/i.test(file))||file.includes(path.sep+'data'+path.sep)||!isPublicFile(file))return sendNotFound(req,res,name);
 fs.promises.access(file).then(()=>/\.html$/i.test(file)&&seoPages.has(path.basename(file))?serveSeoPage(req,res,file,url,name):serveStatic(req,res,file),()=>sendNotFound(req,res,name));
});
setInterval(()=>fs.writeFileSync(path.join(runtimeRoot,'data','live-api-status.json'),JSON.stringify({...serviceMetrics,sampledAt:new Date().toISOString(),eventConnections:eventClients.size,memory:process.memoryUsage(),queryPool:{...queryPool.metrics},eventLoopP99Ms:Math.round(eventLoopDelay.percentile(99)/1e6)},null,2)),60000).unref();
server.keepAliveTimeout=65000;server.headersTimeout=70000;server.requestTimeout=30000;server.maxHeadersCount=60;
server.listen(port,process.env.HOST||'127.0.0.1',()=>console.log('Local: http://'+(process.env.HOST||'127.0.0.1')+':'+port+' — Canlı yerel aktarım / '+sync.status().servers.filter(s=>s.available).length+' açık sunucu'));





async function shutdown(){if(shuttingDown)return;shuttingDown=true;clearTimeout(importerRestartTimer);clearTimeout(archiverRestartTimer);clearTimeout(dailyBackupRestartTimer);for(const client of eventClients)client.res.end();eventClients.clear();const timeout=setTimeout(()=>{server.closeAllConnections();process.exit(1)},8000);timeout.unref();server.close(()=>{clearTimeout(timeout)});await Promise.all([queryPool.close(),importer?.terminate(),archiver?.terminate(),dailyBackup?.terminate()]);for(const state of sync.states.values())state.db.close();dashboardHistory.close();eventLoopDelay.disable();process.exit(0)}
process.once('SIGTERM',shutdown);process.once('SIGINT',shutdown);
