const assert=require('node:assert/strict'),path=require('node:path'),{createQueryPool}=require('./query-pool.cjs');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{const pool=createQueryPool({},{size:1,maxQueue:1,timeoutMs:60,restartDelay:10,workerFile:path.join(__dirname,'test-fixtures/query-worker.cjs')});
 try{while(pool.metrics.workersReady!==1)await sleep(5);
 const hanging=pool.query('hang'),next=pool.query('healthy');assert.equal((await pool.query('overflow')).status,503);assert.equal((await hanging).status,503);assert.equal((await next).status,200);assert.equal(pool.metrics.timeouts,2);assert(pool.metrics.rejected>=1);
 const crash=pool.query('crash');assert.equal((await crash).status,503);assert.equal((await pool.query('recovered')).status,200);assert(pool.metrics.restarts>=4);console.log('Hung/crashed read workers restart, reads retry once, bounded queue rejects overload, and subsequent requests succeed.');
 }finally{await pool.close()}
})().catch(error=>{console.error(error);process.exitCode=1});
