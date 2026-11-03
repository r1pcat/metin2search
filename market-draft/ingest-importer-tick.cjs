const fs=require('node:fs'),path=require('node:path'),{LiveSync}=require('./live-sync.cjs');
const [sourceRoot,webRoot]=process.argv.slice(2);
if(!sourceRoot||!webRoot){console.error('kullanım: node ingest-importer-tick.cjs <sourceRoot> <webRoot>');process.exit(2)}
fs.mkdirSync(path.join(webRoot,'data'),{recursive:true});
const live=new LiveSync(webRoot,sourceRoot,()=>{},__dirname);clearInterval(live.timer);
live.tick();
const errors=live.metrics.errors;
for(const state of live.states.values()){try{state.source.close();state.db.close()}catch{}}
console.log(JSON.stringify({errors,servers:[...live.states.keys()]}));process.exit(errors?1:0);
