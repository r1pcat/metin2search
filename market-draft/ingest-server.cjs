const path=require('node:path');
const {createIngestServer}=require('./ingest.cjs');
const root=__dirname;
const keysFile=process.env.INGEST_KEYS_FILE;
if(!keysFile){console.error('INGEST_KEYS_FILE gerekli');process.exit(2)}
if(!process.env.SOURCE_ROOT){console.error('SOURCE_ROOT gerekli');process.exit(2)}
const sourceRoot=process.env.SOURCE_ROOT;
const stateDir=process.env.INGEST_STATE_DIR||path.join(root,'data','ingest');
const host=process.env.INGEST_HOST||'127.0.0.1',port=Number(process.env.INGEST_PORT)||4181;
const {server}=createIngestServer({sourceRoot,keysFile,stateDir,log:(event,data)=>console.log(JSON.stringify({at:new Date().toISOString(),event,...data}))});
server.listen(port,host,()=>console.log(JSON.stringify({at:new Date().toISOString(),event:'listening',host,port,sourceRoot})));
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>{server.close(()=>process.exit(0));setTimeout(()=>process.exit(0),5000).unref()});
