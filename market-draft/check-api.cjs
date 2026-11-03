const assert=require('node:assert/strict');
const base='http://127.0.0.1:4180';
const get=async p=>{const r=await fetch(base+p);assert.equal(r.status,200);return r.json()};
(async()=>{
 const meta=await get('/api/meta');assert.equal(meta.server,'Safir');assert(meta.uniqueRows>40000);
 for(const [sort,fn,asc] of [['unit-asc',x=>(x.Won*1e8+x.Yang)/Math.max(x.Count,1),true],['total-asc',x=>x.Won*1e8+x.Yang,true],['total-desc',x=>x.Won*1e8+x.Yang,false],['unit-desc',x=>(x.Won*1e8+x.Yang)/Math.max(x.Count,1),false],['qty-asc',x=>x.Count,true],['qty-desc',x=>x.Count,false],['newest',x=>new Date(x.observed).valueOf(),false]]){
  const r=await get('/api/items?server=safir&sort='+sort);assert(r.items.length===15);const v=r.items.map(fn);for(let i=1;i<v.length;i++)assert(asc?v[i]>=v[i-1]:v[i]<=v[i-1],sort);console.log(sort+' OK');
 }
 const el=await get('/api/items?server=safir&grade=3');assert(el.items.length);for(const x of el.items){const e=x.ClientDetails.element;assert(e.grade>=3);if(x.elementSteps?.verified){assert.equal(x.elementSteps.power.reduce((a,b)=>a+b,0),e.value);assert.equal(x.elementSteps.attack.reduce((a,b)=>a+b,0),e.bonus)}}
 const x=el.items[0],detail=await get('/api/item?id='+x.id);assert.deepEqual(detail.ClientDetails.lines,x.ClientDetails.lines);
 const shop=await get('/api/shop?seller='+encodeURIComponent(x.Seller));assert(shop.total>0);assert(shop.items.every(v=>v.Seller===x.Seller&&v.server==='Safir'));
 const foreign=await get('/api/items?server=marmara');assert.equal(foreign.total,0);
 const armor=await get('/api/items?category=3');assert(armor.items.every(v=>v.category===3));
 const stat=await get('/api/items?stat=72&statMin=40');assert(stat.items.every(v=>v.Bonuses.some(b=>b.Type===72&&b.Value>=40)));
 const search=await get('/api/items?q='+encodeURIComponent('kilici'));assert(search.total>0);
 console.log('Element step totals, full client tooltip, seller isolation, server isolation, category, bonus threshold and Turkish search OK');
})().catch(e=>{console.error(e);process.exitCode=1});
