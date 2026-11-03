const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),crypto=require('node:crypto'),zlib=require('node:zlib');
const {Transform}=require('node:stream'),{pipeline}=require('node:stream/promises'),{DatabaseSync}=require('node:sqlite');
function validateManifest(m,{expectedDataset,expectedServer,expectedDay,maxBytes=8*1024**3}={}){
 if(m.schemaVersion!==1||m.complete!==true||m.encoding!=='br'||!['Europe/Istanbul','Turkey Standard Time'].includes(m.timezone))throw Error('Unsupported or incomplete daily archive');
 if(typeof m.datasetId!=='string'||!m.datasetId.length||m.datasetId.length>200||typeof m.server!=='string'||!m.server.length||m.server.length>200||!/^\d{4}-\d{2}-\d{2}$/.test(m.day)||!Number.isFinite(Date.parse(m.day+'T00:00:00Z'))||new Date(m.day+'T00:00:00Z').toISOString().slice(0,10)!==m.day||!Number.isFinite(Date.parse(m.createdAt)))throw Error('Invalid daily archive identity/date');
 for(const name of ['events','lastSequence','sqliteBytes','compressedBytes'])if(!Number.isSafeInteger(m[name])||m[name]<0)throw Error('Invalid daily archive size/count');
 if(!m.sqliteBytes||!m.compressedBytes||m.sqliteBytes>maxBytes||m.compressedBytes>maxBytes)throw Error('Daily archive exceeds configured size limit');
 for(const name of ['sqliteSha256','compressedSha256'])if(typeof m[name]!=='string'||!/^[a-f\d]{64}$/i.test(m[name]))throw Error('Invalid archive checksum');
 if(m.archiveFile!==undefined&&(typeof m.archiveFile!=='string'||!/^daily(?:-[a-f\d]{64})?\.sqlite\.br$/i.test(m.archiveFile)))throw Error('Invalid daily archive filename');
 for(const [actual,expected]of [[m.datasetId,expectedDataset],[m.server,expectedServer],[m.day,expectedDay]])if(expected!==undefined&&actual!==expected)throw Error('Unexpected daily archive identity');
 return m;
}
async function verifyDailyArchive(manifestFile,options={}){
 const file=path.resolve(manifestFile);if(fs.statSync(file).size>65536)throw Error('Manifest too large');
 const manifest=validateManifest(JSON.parse(fs.readFileSync(file,'utf8')),options),compressedFile=path.join(path.dirname(file),manifest.archiveFile||'daily.sqlite.br');
 if(fs.statSync(compressedFile).size!==manifest.compressedBytes)throw Error('Compressed archive size mismatch');
 const parent=options.tempRoot?path.resolve(options.tempRoot):os.tmpdir();fs.mkdirSync(parent,{recursive:true});
 const disk=fs.statfsSync(parent);if(Number(disk.bavail)*Number(disk.bsize)<manifest.sqliteBytes+256*1024**2)throw Error('Not enough free space to verify archive');
 const temp=fs.mkdtempSync(path.join(parent,'daily-verify-')),snapshot=path.join(temp,'daily.sqlite');let db;
 try{
  const compressedHash=crypto.createHash('sha256'),sqliteHash=crypto.createHash('sha256');let rawBytes=0,compressedBytes=0;
  const countCompressed=new Transform({transform(chunk,_encoding,done){compressedBytes+=chunk.length;compressedHash.update(chunk);done(null,chunk)}});
  const countRaw=new Transform({transform(chunk,_encoding,done){rawBytes+=chunk.length;if(rawBytes>manifest.sqliteBytes)return done(Error('Expanded archive exceeds declared size'));sqliteHash.update(chunk);done(null,chunk)}});
  await pipeline(fs.createReadStream(compressedFile),countCompressed,zlib.createBrotliDecompress(),countRaw,fs.createWriteStream(snapshot,{flags:'wx'}));
  if(rawBytes!==manifest.sqliteBytes||compressedBytes!==manifest.compressedBytes||compressedHash.digest('hex')!==manifest.compressedSha256.toLowerCase()||sqliteHash.digest('hex')!==manifest.sqliteSha256.toLowerCase())throw Error('Daily archive checksum/size mismatch');
  db=new DatabaseSync(snapshot,{readOnly:true});db.exec('PRAGMA query_only=ON');if(db.prepare('PRAGMA quick_check').get().quick_check!=='ok')throw Error('Daily SQLite integrity check failed');
  const identity=Object.fromEntries(db.prepare('SELECT key,value FROM meta').all().map(r=>[r.key,r.value]));
  for(const [aliases,expected]of [[['datasetId','dataset_id','dataset'],manifest.datasetId],[['server'],manifest.server],[['day'],manifest.day],[['timezone'],manifest.timezone]]){const value=aliases.map(k=>identity[k]).find(v=>v!==undefined);if(value!==expected)throw Error('Daily SQLite identity mismatch')}
  const count=db.prepare('SELECT COUNT(*) n FROM events').get().n;if(count!==manifest.events)throw Error('Daily archive event count mismatch');
  const columns=db.prepare('PRAGMA table_info(events)').all().map(c=>c.name),sequence=columns.find(c=>['seq','sequence','sequence_id','id'].includes(c));
  if(!sequence)throw Error('Unsupported daily sequence schema');const latest=db.prepare('SELECT COALESCE(MAX('+sequence+'),0) n FROM events').get().n;if(latest!==manifest.lastSequence)throw Error('Daily archive sequence mismatch');
  return {verified:true,manifest,events:count};
 }finally{db?.close();for(const owned of [snapshot,snapshot+'-wal',snapshot+'-shm'])if(fs.existsSync(owned))fs.unlinkSync(owned);fs.rmdirSync(temp)}
}
module.exports={validateManifest,verifyDailyArchive};
if(require.main===module){const file=process.argv[2];if(!file){console.error('Usage: node daily-archive.cjs /path/to/backup/manifest.json');process.exitCode=1}else verifyDailyArchive(file).then(result=>console.log(JSON.stringify(result,null,2))).catch(error=>{console.error(error.message);process.exitCode=1})}
