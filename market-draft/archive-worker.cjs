const {parentPort,workerData}=require('node:worker_threads'),path=require('node:path');
const {readConfig,r2Store}=require('./archive-storage.cjs'),{createArchiveRunner}=require('./archive-runner.cjs');
const config=readConfig(workerData.root);let runner;
if(config?.automaticUpload===true){runner=createArchiveRunner({sourceRoot:workerData.sourceRoot,statusFile:path.join(workerData.runtimeRoot,'data','archive-upload-status.json'),store:r2Store(config,path.join(workerData.root,'..','.archive-state')),maxRows:config.maxRowsPerPass||5000,onStatus:status=>parentPort.postMessage(status)});runner.start()}else parentPort.postMessage({enabled:false});
parentPort.on('message',async message=>{if(message==='close'){await runner?.close();parentPort.postMessage({stopped:true});parentPort.close()}});
