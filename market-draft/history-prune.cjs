const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');

function pruneArchive({localRoot,prefix='metin2-history-v1',server,dataset,minVersion}){
 const result={removedRanges:0,removedChunks:0,freedBytes:0,removedCatalogs:0,skipped:0};
 if(!Number.isSafeInteger(minVersion)||minVersion<1)return result;
 const base=path.join(localRoot,prefix),dir=path.join(base,'catalog',server,dataset);
 if(!fs.existsSync(dir))return result;
 for(const name of fs.readdirSync(dir).filter(n=>/^\d+\.json$/.test(n)).sort((a,b)=>parseInt(a)-parseInt(b))){
  const file=path.join(dir,name);let catalog;
  try{catalog=JSON.parse(fs.readFileSync(file,'utf8'))}catch{result.skipped++;continue}
  if(!Array.isArray(catalog.ranges)){result.skipped++;continue}
  const drop=catalog.ranges.filter(r=>r.last<minVersion);if(!drop.length)continue;
  const keep=catalog.ranges.filter(r=>r.last>=minVersion);
  if(keep.length){
   const text=JSON.stringify({...catalog,ranges:keep}),temp=file+'.'+crypto.randomUUID()+'.tmp';
   fs.writeFileSync(temp,text,{flag:'wx'});fs.renameSync(temp,file);
  }else{fs.unlinkSync(file);result.removedCatalogs++}
  for(const r of drop){
   const chunk=path.join(base,r.key);
   try{const size=fs.statSync(chunk).size;fs.unlinkSync(chunk);result.freedBytes+=size;result.removedChunks++}catch(error){if(error.code!=='ENOENT')throw error}
   result.removedRanges++;
  }
 }
 return result;
}
module.exports={pruneArchive};
