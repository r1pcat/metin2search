const fs=require('node:fs'),{signRequest}=require('./ingest.cjs');
const a=(()=>{const x=process.argv.slice(2),o={};for(let i=0;i<x.length;i+=2)o[x[i].replace(/^--/,'')]=x[i+1];return o})();
if(!a.url||!a['key-id']||!a['key-file']){console.error('kullanım: node ingest-ping.cjs --url <adres> --key-id <kimlik> --key-file <dosya> [--server Safir]');process.exit(2)}
const secret=fs.readFileSync(a['key-file'],'utf8').trim(),base=a.url.replace(/\/$/,'');
async function call(target){
 const headers={'X-M2-Key-Id':a['key-id'],...signRequest(secret,'GET',target)};
 const r=await fetch(base+target,{headers});return {status:r.status,body:await r.json().catch(()=>null)};
}
(async()=>{
 const t0=Date.now(),ping=await call('/ingest/v1/ping');
 console.log('ping:',ping.status,ping.body?.ok?'ok':JSON.stringify(ping.body),`(${Date.now()-t0} ms)`);
 if(ping.status===200){const skew=Math.round((Date.now()/1000)-ping.body.serverTime);console.log('saat farkı (yerel - sunucu):',skew,'sn',Math.abs(skew)>120?'  ← UYARI: 5 dakikadan fazlası reddedilir':'');
  const state=await call('/ingest/v1/state?server='+encodeURIComponent(a.server||'Safir'));console.log('durum:',state.status,state.status===200?`veri kümesi ${state.body.datasetId||'(henüz yok)'}, kategori ${Object.keys(state.body.categories).length}, kontrol kaydı ${state.body.purchaseChecksCursor}`:JSON.stringify(state.body))}
 process.exit(ping.status===200?0:1);
})().catch(e=>{console.error('bağlantı hatası:',e.message);process.exit(1)});
