const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
function validateSelection(input,stats){
 if(!input||input.version!==1||!input.colors||Array.isArray(input.colors)||typeof input.colors!=='object')throw Error('Geçersiz renk dosyası.');
 const colors={};
 for(const [type,color]of Object.entries(input.colors)){
  if(!Object.hasOwn(stats,type)||typeof color!=='string'||!/^#[0-9a-f]{6}$/i.test(color))throw Error('Geçersiz efsun veya renk kodu.');
  colors[type]=color.toLowerCase();
 }
 if(Object.keys(colors).length!==Object.keys(stats).length)throw Error('Tüm efsunların renkleri kaydedilmelidir.');
 return {version:1,savedAt:new Date().toISOString(),colors};
}
function createBonusEditorStore(root,stats){
 const token=crypto.randomBytes(32).toString('hex'),file=path.join(root,'data','bonus-color-selection.json');
 const reply=(res,status,obj)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});res.end(JSON.stringify(obj))};
 return function(req,res,url){
  if(url.pathname!=='/local/bonus-colors')return false;
  const host=req.headers.host||'',local=/^(127\.0\.0\.1|localhost)(:\d+)?$/.test(host)&&['127.0.0.1','::1','::ffff:127.0.0.1'].includes(req.socket.remoteAddress);
  if(!local||process.env.NODE_ENV==='production'){reply(res,403,{error:'Düzenleyici yalnızca yerel önizlemede kullanılabilir.'});return true}
  if(req.method==='GET'){
   try{reply(res,200,{token,selection:fs.existsSync(file)?JSON.parse(fs.readFileSync(file,'utf8')):null})}catch{reply(res,500,{error:'Kaydedilen renkler okunamadı.'})}
   return true;
  }
  if(req.method!=='POST'){reply(res,405,{error:'Desteklenmeyen işlem.'});return true}
  if(req.headers.origin!=='http://'+host||req.headers['x-bonus-editor-token']!==token){reply(res,403,{error:'Kaydetmek için düzenleyiciyi yerel siteden açın.'});return true}
  let body='',oversize=false;
  req.on('data',chunk=>{if(oversize)return;body+=chunk;if(Buffer.byteLength(body)>65536){oversize=true;reply(res,413,{error:'Renk dosyası çok büyük.'})}});
  req.on('end',()=>{if(oversize)return;try{
   const selection=validateSelection(JSON.parse(body),stats),temp=file+'.tmp';
   fs.writeFileSync(temp,JSON.stringify(selection,null,2)+'\n','utf8');fs.renameSync(temp,file);
   reply(res,200,{savedAt:selection.savedAt,count:Object.keys(selection.colors).length});
  }catch(e){reply(res,400,{error:e.message})}});
  return true;
 };
}
module.exports={createBonusEditorStore,validateSelection};
