const assert=require('node:assert/strict'),get=async p=>(await fetch('http://127.0.0.1:4180'+p)).json();
async function find(query,pred,pages=40){for(let page=1;page<=pages;page++){const r=await get('/api/items?server=safir&'+query+'&page='+page);const x=r.items?.find(pred);if(x)return x;if(page>=r.pages)break}return null}
(async()=>{
const jewel=await find('category=4',x=>x.jewels?.length===3&&x.jewels.some(j=>j.duration));assert(jewel,'süreli 3 cevherli takı bulunamadı');assert(jewel.socketTextStart>0);console.log('3 jewelry sockets and captured duration OK');
const aura=await find('category=9&sub=5',x=>x.embedded?.name?.includes('Kolye')&&x.absorption>0);assert(aura,'Kolye emdirilmiş aura bulunamadı');assert.equal(aura.category,9);assert.equal(aura.sub,5);console.log('Aura embedded necklace OK');
const belt=await find('category=9',x=>x.embedded?.kind==='Kuşak'&&x.embedded.name.includes('Zodyak'));assert(belt,'Zodyak emdirilmiş kuşak bulunamadı');console.log('Belt source image metadata OK');
const ninja=await get('/api/items?server=safir&category=10&sub=1&job=1');assert(ninja.total>0);assert(ninja.items.every(x=>x.job===1));console.log('Book class filter OK: '+ninja.total);
for(let page=1;page<=5;page++){const r=await get('/api/items?server=safir&category=3&page='+page);for(const x of r.items)assert(!(x.stones||[]).some(s=>s.vnum===104),'taş olmayan soket taş sayıldı: '+x.id)}console.log('Non-stone socket excluded OK');
})().catch(e=>{console.error(e);process.exitCode=1});
