const assert=require('node:assert/strict');
const get=async(p)=>{const r=await fetch('http://127.0.0.1:4180'+p);assert.equal(r.status,200);return r.json()};
(async()=>{
const meta=await get('/api/meta');assert(meta.economy.won>0&&meta.economy.yang>0);console.log('Safir totals',meta.economy);
for(const filters of [[{type:72,mode:'gte',value:40}],[{type:1,mode:'eq',value:2000}],[{type:1,mode:'eq',value:2000},{type:15,mode:'gte',value:5}]]){
 const r=await get('/api/items?bonusFilters='+encodeURIComponent(JSON.stringify(filters)));assert(r.total>0);for(const x of r.items)for(const f of filters)assert(x.Bonuses.some(b=>b.Type===f.type&&(f.mode==='eq'?b.Value===f.value:b.Value>=f.value)));console.log('Efsun',JSON.stringify(filters),r.total);
}
for(const [q,pred] of [['level=90&levelMax=105',x=>{const l=x.ClientDetails.limits.find(l=>l[0]===1)?.[1]||0;return l>=90&&l<=105}],['energyMin=10&energyMax=10',x=>x.energy===10],['absorbMin=25&absorbMax=25',x=>x.absorption===25],['stones=1',x=>x.stones.length>0]]){const r=await get('/api/items?'+q);assert(r.total>0);assert(r.items.every(pred));console.log(q,r.total)}
const r=await get('/api/items?sort=total-desc&category=2');const h=await get('/api/history?id='+r.items[0].id);assert(h.observations.length>0);assert(h.observations.every(x=>x.seller&&Number.isFinite(x.won)));console.log('History',h.observations.length);
})().catch(e=>{console.error(e);process.exitCode=1});
