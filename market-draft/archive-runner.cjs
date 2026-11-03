const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
const {atomic}=require('./archive-storage.cjs'),{exportSource}=require('./history-archive.cjs'),{resolveServer}=require('./servers.cjs');
function createArchiveRunner({sourceRoot,statusFile,store,maxRows=5000,batchSize=250,recoveryMs=60000,retryMs=30000,onStatus=()=>{},exporter=exportSource}){
 if(!Number.isSafeInteger(maxRows)||maxRows<1||maxRows>10000)throw Error('Invalid archive pass size');
 let stopped=false,running=null,dirty=false,wakeTimer,recoveryTimer,watcher;const states=new Map();
 try{const previous=JSON.parse(fs.readFileSync(statusFile,'utf8'));for(const row of previous.servers||[])if(row.file&&row.dataset)states.set(row.file,row)}catch{}
 function status(){return {enabled:true,running:!!running,updatedAt:new Date().toISOString(),servers:[...states.values()].map(({file,...row})=>row),budget:store.budget?.()||null}}
 function persist(){const value=status();atomic(statusFile,Buffer.from(JSON.stringify({...value,servers:[...states.values()]},null,2)));onStatus(value)}
 function schedule(delay=80){if(stopped)return;if(running){dirty=true;return;}clearTimeout(wakeTimer);wakeTimer=setTimeout(()=>{wakeTimer=null;void drain()},delay)}
 async function pass(){let more=false;const files=fs.existsSync(sourceRoot)?fs.readdirSync(sourceRoot,{withFileTypes:true}).filter(d=>d.isDirectory()).map(d=>path.join(sourceRoot,d.name,'market.sqlite')).filter(f=>fs.existsSync(f)):[];
  for(const file of files){if(stopped)break;let db;try{
   db=new DatabaseSync(file,{readOnly:true});db.exec('PRAGMA query_only=ON;PRAGMA busy_timeout=1000');
   const server=resolveServer(db.prepare("SELECT value FROM trade_metadata WHERE key='server'").get()?.value),dataset=db.prepare("SELECT value FROM trade_metadata WHERE key='dataset_id'").get()?.value;if(!server||!dataset)throw Error('Archive source identity missing');
   const maximum=db.prepare('SELECT COALESCE(MAX(id),0) n FROM compact_versions').get().n,completion=db.prepare("SELECT 1 FROM sqlite_master WHERE name='category_outbox'").get()?db.prepare('SELECT COALESCE(MAX(rowid),0) n FROM category_outbox').get().n:0,signature=dataset+':'+maximum+':'+completion,old=states.get(file);
   if(old?.dataset===dataset&&old.signature===signature&&!old.error&&old.lastVersion>=maximum)continue;
   if(old?.retryAt>Date.now())continue;
   const result=await exporter(db,server.id,store,{maxRows,batchSize,onProgress:progress=>{states.set(file,{file,server:server.id,dataset,signature,lastVersion:progress.lastVersion,sourceVersion:maximum,completion,exported:(old?.dataset===dataset?old.exported||0:0)+progress.exported,lastExported:progress.exported,lastChunks:progress.chunks,lastUploadBytes:progress.uploadedBytes,lastSuccess:new Date().toISOString(),error:null,retryAt:0});persist()}});
   states.set(file,{file,server:server.id,dataset,signature,lastVersion:result.lastVersion,sourceVersion:maximum,completion,exported:(old?.dataset===dataset?old.exported||0:0)+result.exported,lastExported:result.exported,lastChunks:result.chunks,lastUploadBytes:result.uploadedBytes,lastSuccess:new Date().toISOString(),error:null,retryAt:0});
   if(result.lastVersion<maximum)more=true;
  }catch(e){const old=states.get(file)||{file};const budget=/budget reached/i.test(e.message);states.set(file,{...old,error:e.message,lastError:new Date().toISOString(),retryAt:Date.now()+(budget?3600000:retryMs)})}finally{db?.close()}}
  return more;
 }
 function drain(){if(stopped)return Promise.resolve();if(running)return running;running=(async()=>{let more=false;dirty=false;try{more=await pass()}catch(e){onStatus({enabled:true,error:e.message})}finally{running=null;persist();if((more||dirty)&&!stopped)schedule(more?1000:80)}})();queueMicrotask(()=>{if(running)persist()});return running}
 function start(){try{watcher=fs.watch(sourceRoot,{recursive:true},(_,filename)=>{if(filename&&String(filename).replace(/\\/g,'/').endsWith('category-sync/ready.json'))schedule()});watcher.on('error',()=>{watcher?.close();watcher=null})}catch{}recoveryTimer=setInterval(()=>void drain(),recoveryMs);schedule(0);return api}
 async function close(){stopped=true;clearTimeout(wakeTimer);clearInterval(recoveryTimer);watcher?.close();await running;store.close?.()}
 const api={start,drain,status,async close(){await close()}};return api;
}
module.exports={createArchiveRunner};
