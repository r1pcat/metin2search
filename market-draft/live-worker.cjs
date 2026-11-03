const {parentPort,workerData}=require('node:worker_threads');
const {LiveSync}=require('./live-sync.cjs');
const sync=new LiveSync(workerData.root,workerData.sourceRoot,()=>{},workerData.definitionsRoot||workerData.root);
sync.timer?.ref();
sync.onStatus=status=>parentPort.postMessage(status);
parentPort.postMessage(sync.status());
