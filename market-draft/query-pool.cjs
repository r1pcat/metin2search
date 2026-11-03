const {Worker}=require('node:worker_threads'),path=require('node:path');
function createQueryPool(options,{size=2,maxQueue=1024,timeoutMs=10000,restartDelay=500,workerFile=path.join(__dirname,'query-worker.cjs')}={}){
 const slots=[],queue=[];let serial=0,closed=false;
 const metrics={completed:0,errors:0,restarts:0,rejected:0,queued:0,active:0,workersReady:0,timeouts:0,totalMs:0,maxMs:0};
 function dispatch(){metrics.queued=queue.length;for(const slot of slots){if(!slot.ready||slot.task)continue;let task=queue.shift();while(task&&Date.now()-task.created>Math.max(timeoutMs*2,1000)){metrics.rejected++;task.resolve({status:503,body:JSON.stringify({error:'Yoğunluk nedeniyle lütfen yeniden deneyin'})});task=queue.shift()}if(!task)break;slot.task=task;metrics.active++;slot.timer=setTimeout(()=>{metrics.timeouts++;slot.worker.terminate()},timeoutMs);slot.timer.unref();slot.worker.postMessage({id:task.id,url:task.url})}metrics.queued=queue.length}
 function spawn(){if(closed)return;const slot={ready:false,task:null};slots.push(slot);const worker=new Worker(workerFile,{workerData:options});slot.worker=worker;
  worker.on('message',message=>{if(message.ready){slot.ready=true;metrics.workersReady++;dispatch();return}const task=slot.task;if(!task||task.id!==message.id)return;clearTimeout(slot.timer);slot.task=null;metrics.active--;metrics.completed++;metrics.totalMs+=message.ms;metrics.maxMs=Math.max(metrics.maxMs,message.ms);task.resolve(message);dispatch()});
  worker.on('error',error=>{metrics.errors++;console.error('Okuma işlemi:',error.message)});
  worker.on('exit',()=>{clearTimeout(slot.timer);if(slot.ready)metrics.workersReady--;slot.ready=false;if(slot.task){metrics.active--;if(!closed&&!slot.task.retried){slot.task.retried=true;queue.unshift(slot.task)}else slot.task.resolve({status:503,body:JSON.stringify({error:'İstek yeniden denenmeli'})});slot.task=null}slots.splice(slots.indexOf(slot),1);if(!closed){metrics.restarts++;setTimeout(spawn,restartDelay);dispatch()}});
 }
 for(let i=0;i<size;i++)spawn();
 return {query(url){if(closed||queue.length>=maxQueue){metrics.rejected++;return Promise.resolve({status:503,body:JSON.stringify({error:'Yoğunluk nedeniyle lütfen yeniden deneyin'})})}return new Promise(resolve=>{queue.push({id:++serial,url,resolve,created:Date.now()});dispatch()})},metrics,async close(){closed=true;for(const task of queue.splice(0))task.resolve({status:503,body:'{}'});await Promise.all([...slots].map(slot=>slot.worker.terminate()))}};
}
module.exports={createQueryPool};
