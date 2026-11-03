const {parentPort,workerData}=require('node:worker_threads');
const reader=require('./api-reader.cjs').createReader(workerData);
let lastErrorLog=0;
parentPort.on('message',async({id,url})=>{const started=performance.now();try{parentPort.postMessage({id,...await reader.queryAsync(url),ms:performance.now()-started})}catch(error){const invalid=error instanceof SyntaxError||/^Invalid /.test(error.message);if(!invalid&&Date.now()-lastErrorLog>10000){lastErrorLog=Date.now();console.error('Query worker:',error.message)}parentPort.postMessage({id,status:invalid?400:500,body:JSON.stringify({error:invalid?'Filtre değerlerini kontrol edin':'Yerel kayıtlar okunamadı'}),ms:performance.now()-started})}});
parentPort.postMessage({ready:true});
