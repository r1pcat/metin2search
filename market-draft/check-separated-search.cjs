const assert=require('node:assert/strict');
const base='http://127.0.0.1:4180';
const normalize=s=>String(s).toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/ı/g,'i');
async function get(route,params){const url=new URL(route,base);url.search=new URLSearchParams({server:'safir',...params});const response=await fetch(url);assert.equal(response.status,200);return response.json();}
(async()=>{
 const item=await get('/api/items',{q:'Leylak'});assert(item.items.every(x=>normalize(x.Name).includes('leylak')));
 const sellerOnly=await get('/api/items',{q:'AyberkTR'});assert.equal(sellerOnly.total,0,'Seller names must not match item search');
 const seller=await get('/api/items',{seller:'AyberkTR',sellerExact:'1'});assert(seller.total>0);assert(seller.items.every(x=>x.Seller==='AyberkTR'));
 const exact=await get('/api/items',{q:'Cennetin Gözyaşı Kolye+9',itemExact:'1'});assert(exact.total>0);assert(exact.items.every(x=>x.Name==='Cennetin Gözyaşı Kolye+9'));
 for(const kind of ['item','player']){const data=await get('/api/search-suggestions',{kind,q:kind==='item'?'Leylak':'Ayberk'});assert.equal(data.kind,kind);assert(data.suggestions.length<=8);assert(data.suggestions.every(x=>normalize(x.label).includes(kind==='item'?'leylak':'ayberk')));}
 const together=await get('/api/items',{q:'Cennetin Gözyaşı Kolye+9',itemExact:'1',seller:'AyberkTR',sellerExact:'1'});assert(together.total>0);assert(together.items.every(x=>x.Name==='Cennetin Gözyaşı Kolye+9'&&x.Seller==='AyberkTR'));
 const families=await get('/api/search-suggestions',{kind:'item',q:'Zehir'});assert(families.suggestions.length>0);assert(families.suggestions.every(x=>! /\+\s*\d+\s*$/.test(x.label)));assert.equal(new Set(families.suggestions.map(x=>normalize(x.label))).size,families.suggestions.length);
 const family=families.suggestions.find(x=>normalize(x.label)==='zehir kilici');assert(family);const familyItems=await get('/api/items',{q:family.label,itemFamily:'1'});assert(familyItems.total>0);assert(familyItems.items.every(x=>normalize(x.Name.replace(/\s*\+\s*\d+\s*$/,'').trim())===normalize(family.label)));
 const upgraded=await get('/api/items',{q:family.label,itemFamily:'1',upgradeMin:'9',upgradeMax:'9'});assert(upgraded.total>0);assert(upgraded.items.every(x=>/\+\s*9\s*$/.test(x.Name)));
 console.log(JSON.stringify({leylakItems:item.total,playerListings:seller.total,exactItems:exact.total,combined:together.total}));
 console.log('Item/player isolation, exact selection, suggestion scopes and combined filters verified.');
})().catch(e=>{console.error(e);process.exitCode=1;});
