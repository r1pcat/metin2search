const assert=require('node:assert/strict'),{homeSummary}=require('./home-summary.cjs');
const record=(count,won,yang)=>({ready:true,latestSeen:'2026-10-15T12:00:00Z',db:{prepare(){return {get(){return {value:JSON.stringify({overall:{count,sellers:2,distinctItems:3},economy:{won,yang}})}}}}}});
const states=new Map([['a',record(5,10,150000000)],['b',record(7,20,0)]]),status={servers:[{id:'a',name:'A',available:true,clientName:'[SAPPHIRE] A'},{id:'b',name:'B',available:true},{id:'c',name:'C',available:false}]};
const home=homeSummary(status,states);
assert.equal(home.valueWon,31.5);assert.equal(home.listings,12);assert.equal(home.sellerRecords,4);assert.equal(home.available,2);assert.equal(home.servers[0].group,'SAPPHIRE');assert.equal(home.servers[2].valueWon,null);assert.equal(home.servers[2].listings,null);
const pending=homeSummary({servers:[{id:'a',available:false}]},states);assert.equal(pending.available,0);assert.equal(pending.valueWon,0);
const broken=homeSummary({servers:[{id:'a',available:true}]},new Map([['a',{ready:true,db:{prepare(){throw Error('private path')}}}]]));assert.equal(broken.servers[0].available,false);assert(!JSON.stringify(broken).includes('private path'));
console.log('Ana sayfa toplamları, Yang→Won dönüşümü ve kaydı olmayan sunucular doğrulandı.');
