const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
const {catalog}=require('./servers.cjs'),{ensureSearchProjection}=require('./search-projection.cjs'),{ensureWebIndexes,publishWebSnapshot}=require('./web-snapshot.cjs');
const root=process.env.DATA_ROOT?path.resolve(process.env.DATA_ROOT):__dirname;
fs.mkdirSync(path.join(root,'data/live'),{recursive:true});
for(const server of catalog){const file=path.join(root,'data/live',server.id+'.sqlite');if(!fs.existsSync(file))continue;const db=new DatabaseSync(file);try{
 db.exec('PRAGMA journal_mode=WAL;PRAGMA busy_timeout=5000');if(!db.prepare("SELECT value FROM meta WHERE key='snapshot'").get())continue;
 ensureWebIndexes(db);ensureSearchProjection(db);
 const expected=db.prepare('SELECT COUNT(*) n FROM active_items').get().n,actual=db.prepare('SELECT COUNT(*) n FROM web_search_active').get().n;if(expected!==actual)throw Error(server.id+': search projection mismatch');
 if(process.argv.includes('--check')&&db.prepare('PRAGMA quick_check').get().quick_check!=='ok')throw Error(server.id+': database integrity check failed');
 db.exec("DELETE FROM meta WHERE key='web_context_revision'");const result=publishWebSnapshot(db);console.log(JSON.stringify({server:server.id,active:actual,snapshotMs:result?.ms}));
 }finally{db.close()}}
