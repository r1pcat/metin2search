const assert=require('node:assert/strict'),{findSimilar,needed}=require('./similar.cjs');
const make=(id,won,name='Zodyak Kılıç+9',bonuses=[{type:72,value:40,slot:0},{type:22,value:20,slot:1},{type:15,value:10,slot:2}])=>({id,Name:name,Vnum:319,Won:won,Yang:0,Count:1,Seller:id,ClientDetails:{itemType:1,bonuses,element:{applyType:101,grade:3}}});
const source=make('source',100),close=make('close',80),wrong=make('wrong',10,'Zodyak Bıçak+9'),expensive=make('expensive',120),unrelated=make('unrelated',20,'Zodyak Kılıç+9',[{type:1,value:2000,slot:0}]);
const result=findSimilar(source,[wrong,expensive,unrelated,close]);assert.equal(result.length,1);assert.equal(result[0].item.id,'close');assert.equal(result[0].saving,20e8);assert(Math.abs(result[0].savingPercent-20)<1e-8);assert.equal(result[0].shared,3);
assert.equal(findSimilar(source,[make('upgrade',20,'Zodyak Kılıç+0')]).length,0);

const near=make('near',70);near.ClientDetails.bonuses[0].value=35;assert.equal(findSimilar(source,[near]).length,1);
const far=make('far',70);far.ClientDetails.bonuses[0].value=10;far.ClientDetails.bonuses[1].value=5;const farR=findSimilar(source,[far]);assert.equal(farR.length,1,'aynı türler, çok farklı değerler yine de alternatif');assert.equal(farR[0].differences.length,2);assert.equal(farR[0].lowerCount,2);assert.equal(farR[0].higherCount,0);
const dragon=make('dragon',100,'Mitsi ejderha elması');dragon.Vnum=115460;dragon.category=8;dragon.ClientDetails.itemType=29;
const same=structuredClone(dragon);same.id='same';same.Seller='same';same.Won=80;same.ClientDetails.bonuses[0].slot=5;assert.equal(findSimilar(dragon,[same]).length,1);
const otherQuality=structuredClone(same);otherQuality.id='different';otherQuality.Vnum=115450;assert.equal(findSimilar(dragon,[otherQuality]).length,0);
const five=(id,won,types,rareTypes=[])=>make(id,won,'Zodyak Kılıç+9',[...types.map((t,i)=>({type:t,value:10+i,slot:i})),...rareTypes.map((t,i)=>({type:t,value:5,slot:5+i}))]);
const src5=five('s5',100,[72,22,15,10,11],[200,201]);
assert.equal(findSimilar(src5,[five('f55',60,[72,22,15,10,11])]).length,1,'5/5 alternatif');
assert.equal(findSimilar(src5,[five('f45',60,[72,22,15,10,99])]).length,1,'4/5 alternatif');
assert.equal(findSimilar(src5,[five('f35',60,[72,22,15,98,99])]).length,0,'3/5 alternatif değil');
assert.equal(findSimilar(src5,[five('rare',60,[72,22,15,10,11],[300,301])]).length,1,'6–7. efsunlar farklı olsa da etkilemez');
const f45=findSimilar(src5,[five('f45b',60,[72,22,15,10,99],[200])])[0];assert.equal(f45.shared,4);assert.equal(f45.sourceBonuses,5);assert.equal(f45.missing.length,1);assert.equal(f45.extra.length,1);
const alc=(id,won,types)=>{const x=make(id,won,'Mitsi ejderha safiri',types.map((t,i)=>({type:t,value:16,slot:i})));x.Vnum=115460;x.category=8;x.ClientDetails.itemType=29;return x};
const asrc=alc('a',100,[6,88,103,78,81,61]);
assert.equal(findSimilar(asrc,[alc('a56',60,[6,88,103,78,81,1])]).length,1,'simya 5/6 alternatif');
assert.equal(findSimilar(asrc,[alc('a46',60,[6,88,103,78,1,2])]).length,0,'simya 4/6 alternatif değil');
assert.equal(findSimilar(asrc,[alc('a66',60,[6,88,103,78,81,61])])[0].shared,6);
const book=(id,won,title,vnum=50300)=>({id,Name:'Beceri Kitabı',Vnum:vnum,Won:won,Yang:0,Count:1,Seller:id,ClientDetails:{itemType:15,lines:[title],bonuses:[]}});
const air=book('air',100,'Hava Kılıcı Beceri Kitabı');
const other=findSimilar(air,[book('o1',10,'Büyülü Silah Beceri Kitabı'),book('o2',10,'Ruh Çağırma Beceri Kitabı')]);assert.equal(other.length,0,'başka beceri kitabı önerilmemeli');
const sameBook=findSimilar(air,[book('o3',10,'Büyülü Silah Beceri Kitabı'),book('same',70,'Hava Kılıcı Beceri Kitabı')]);assert.equal(sameBook.length,1);assert.equal(sameBook[0].item.id,'same');
(async()=>{const current=await(await fetch('http://127.0.0.1:4180/api/items?server=safir&category=8&sort=total-desc')).json();const x=current.items[0];assert(x,'Güncel simya örneği gerekli');const similar=await(await fetch('http://127.0.0.1:4180/api/similar?id='+x.id)).json();assert(Array.isArray(similar.suggestions));for(const r of similar.suggestions){assert.equal(r.item.Vnum,x.Vnum);assert(r.sameQuality);for(const d of r.differences)assert.notEqual(d.current,d.alternative);assert(r.nearbyBonuses>=needed(r.sourceBonuses));assert((r.item.Won*1e8+r.item.Yang)/r.item.Count<(x.Won*1e8+x.Yang)/x.Count)}console.log(JSON.stringify({source:x.Name,quality:x.ClientDetails.lines[1],alternatives:similar.suggestions.map(r=>({name:r.item.Name,quality:r.item.ClientDetails.lines[1],won:r.item.Won,near:r.nearbyBonuses}))}));console.log('Yakın oran, aynı simya kalitesi ve fiyat eşleşmeleri doğrulandı.');})().catch(e=>{console.error(e);process.exitCode=1});

