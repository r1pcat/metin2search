const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),zlib=require('node:zlib'),{promisify}=require('node:util');
const gzip=promisify(zlib.gzip),brotli=promisify(zlib.brotliCompress);
class BoundedCache{
 constructor(maxBytes=64*1024*1024,maxEntries=512){this.maxBytes=maxBytes;this.maxEntries=maxEntries;this.bytes=0;this.entries=new Map()}
 get(key){const entry=this.entries.get(key);if(!entry)return;if(entry.value.expires&&entry.value.expires<Date.now()){this.delete(key);return}this.entries.delete(key);this.entries.set(key,entry);return entry.value}
 delete(key){const old=this.entries.get(key);if(old){this.bytes-=old.size;this.entries.delete(key)}}
 set(key,value){this.delete(key);const size=value.cost||Buffer.byteLength(value.body||'')*3+1024;if(size>this.maxBytes)return;this.entries.set(key,{value,size});this.bytes+=size;while(this.bytes>this.maxBytes||this.entries.size>this.maxEntries)this.delete(this.entries.keys().next().value)}
 clear(){this.entries.clear();this.bytes=0}
}
function encoding(header=''){const options=String(header).split(',').map(part=>{const [name,...params]=part.trim().split(';');const q=params.find(p=>p.trim().startsWith('q='));return {name:name.trim(),q:q?Number(q.trim().slice(2)):1}});return ['br','gzip'].find(name=>options.some(o=>o.name===name&&o.q>0))||null}
function makeEntry(body,ttl=300000){return {body:Buffer.from(body),etag:'W/"'+crypto.createHash('sha256').update(body).digest('hex').slice(0,24)+'"',expires:Date.now()+ttl,compressed:{}}}
async function sendEntry(res,entry,status=200,headers={}){
 const common={'Content-Type':'application/json; charset=utf-8','Cache-Control':'private, max-age=0, must-revalidate','Vary':'Accept-Encoding','X-Content-Type-Options':'nosniff',...headers};
 if(status===200&&res.requestTag?.split(',').map(s=>s.trim()).includes(entry.etag)){res.writeHead(304,{...common,ETag:entry.etag});return res.end()}
 let body=entry.body,used=null;
 if(body.length>1024&&res.responseEncoding){used=res.responseEncoding;entry.compressed[used]??=(used==='br'?brotli(body,{params:{[zlib.constants.BROTLI_PARAM_QUALITY]:4}}):gzip(body,{level:4}));try{body=await entry.compressed[used]}catch{used=null;body=entry.body}}
 if(res.destroyed)return;
 res.writeHead(status,{...common,...(status===200?{ETag:entry.etag}:{}),...(used?{'Content-Encoding':used}:{}),'Content-Length':body.length});res.end(res.headOnly?undefined:body);
}
const mime={'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp','.ico':'image/x-icon','.woff':'font/woff','.woff2':'font/woff2','.ttf':'font/ttf'};
const PRODUCTION=process.env.SERVE_BUILD==='1'||process.env.NODE_ENV==='production';
const stripCss=s=>s.replace(/\/\*[\s\S]*?\*\//g,'').replace(/\n{3,}/g,'\n\n');
const stripJs=s=>s.replace(/^[ \t]*\/\*[\s\S]*?\*\/[ \t]*$/gm,'').replace(/^[ \t]*\/\/.*$/gm,'').replace(/\n{3,}/g,'\n\n');
const staticCache=new BoundedCache(64*1024*1024,2048);
const staticPending=new Map();
async function serveStatic(req,res,file){
 try{const stat=await fs.promises.stat(file);if(!stat.isFile()){res.writeHead(404);return res.end('Not found')}
 const key=file+':'+stat.size+':'+stat.mtimeMs;let entry=staticCache.get(key);const extension=path.extname(file).toLowerCase(),asset=file.includes(path.sep+'assets'+path.sep),hashed=/[.-][a-f0-9]{12}\.(css|js)$/.test(file);
 const policy=hashed?'public, max-age=31536000, immutable':asset?'public, max-age=86400, stale-while-revalidate=604800':'no-cache';
 if(!entry){let pending=staticPending.get(key);if(!pending){pending=fs.promises.readFile(file).then(body=>{if(PRODUCTION&&(extension==='.css'||extension==='.js'))body=Buffer.from(extension==='.css'?stripCss(body.toString('utf8')):stripJs(body.toString('utf8')));const result=makeEntry(body,60000);if(body.length<=2*1024*1024)staticCache.set(key,result);return result}).finally(()=>staticPending.delete(key));staticPending.set(key,pending)}entry=await pending}
 if(!['.html','.css','.js','.svg'].includes(extension))res.responseEncoding=null;
 return sendEntry(res,entry,200,{'Content-Type':mime[extension]||'application/octet-stream','Cache-Control':policy});
 }catch(e){if(res.headersSent||res.destroyed)return;res.writeHead(e.code==='ENOENT'?404:500);res.end('Not found')}
}
module.exports={BoundedCache,encoding,makeEntry,sendEntry,serveStatic};
