const assert=require('node:assert/strict'),http=require('node:http'),fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
const {BoundedCache,encoding}=require('./http-performance.cjs'),base='http://127.0.0.1:'+(process.env.PORT||4180);
function raw(route,headers={}){return new Promise((resolve,reject)=>http.get(base+route,{headers},res=>{const chunks=[];res.on('data',c=>chunks.push(c));res.on('end',()=>resolve({status:res.statusCode,headers:res.headers,body:Buffer.concat(chunks)}))}).on('error',reject))}
(async()=>{
 assert.equal(encoding('gzip;q=0, br;q=0'),null);assert.equal(encoding('gzip, br'),'br');
 const bounded=new BoundedCache(2200,2);bounded.set('a',{body:Buffer.alloc(100)});bounded.set('b',{body:Buffer.alloc(100)});assert(bounded.bytes<=2200);bounded.set('expired',{body:Buffer.alloc(1),expires:Date.now()-1});assert.equal(bounded.get('expired'),undefined);
 const route='/api/items?server=safir&page=2&sort=unit-asc',plain=await raw(route),br=await raw(route,{'Accept-Encoding':'br'}),gzip=await raw(route,{'Accept-Encoding':'gzip'});
 assert.equal(plain.status,200);assert.equal(br.headers['content-encoding'],'br');assert.deepEqual(zlib.brotliDecompressSync(br.body),plain.body);assert.deepEqual(zlib.gunzipSync(gzip.body),plain.body);assert.equal((await raw(route,{'If-None-Match':br.headers.etag})).status,304);
 const build=JSON.parse(fs.readFileSync(path.join(__dirname,'data/public-build.json'),'utf8'));
 for(const name of ['market','home']){const entry=build.pages[name];for(const extension of ['css','js']){const asset=await raw(entry.assets[extension],{'Accept-Encoding':'br'});assert.equal(asset.status,200);assert(asset.headers['cache-control'].includes('immutable'));assert.equal(asset.headers['content-encoding'],'br');assert.equal((await raw(entry.assets[extension],{'If-None-Match':asset.headers.etag})).status,304)}}
 for(const route of ['/server.cjs','/data/live/safir.sqlite'])assert.equal((await raw(route)).status,403);
 console.log(JSON.stringify({apiPlainBytes:plain.body.length,apiBrotliBytes:br.body.length,apiGzipBytes:gzip.body.length,etag304:true,hashedAssetsImmutable:true,byteBoundedCache:true}));
})().catch(e=>{console.error(e);process.exitCode=1});
