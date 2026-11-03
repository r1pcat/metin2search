const assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite'),{describeSpecial}=require('./item-specials.cjs'),{bonusAppearance}=require('./bonus-theme.js');
const db={close(){}};
const pet={Vnum:55002,Sockets:[1790964731,1788372731,216847],ClientDetails:{lines:['Nakliye Kutusu','Nessiewwwwwwwwwwwwww(Ticareti var)','Nessie','Seviye 40 (1g)','Pet süresi: 840 saat','Pet türü: Tip 5: gözükara. Güçlü','Genç(3)','HP +3.9%','Defans +4.0%','SP +6.4%','Kalan Süre : Süresi geçmiş']}},p=describeSpecial(pet).pet;assert.equal(p.filled,true);assert.equal(p.species,'Nessie');assert.equal(p.type,5);assert.equal(p.level,40);assert.equal(p.values.Defans,4);assert.equal(p.lifeHours,840);assert(!p.species.includes('Ticareti'));assert.equal(describeSpecial({Vnum:55002,Sockets:[0,0,0]}).pet.filled,false);
const dew={Vnum:50822,Name:'Turuncu Şebnem',category:6,ClientDetails:{itemType:27,lines:['Turuncu Şebnem','Seviye: 55','Delici Vuruş için şansı %12','Süre:3Min.']}},d=describeSpecial(dew).consumable;assert.equal(d.effectValue,12);assert.equal(d.durationSeconds,180);assert.equal(d.effects.length,1);
assert.deepEqual(describeSpecial({Vnum:115460,ClientDetails:{itemType:29}}).alchemy,{grade:5,clarity:4,upgrade:6,className:'Mitsi',clarityName:'Kusursuz'});
for(const [value,tier]of [[2000,'max'],[1500,'high'],[1000,'mid'],[500,'low']])assert.equal(bonusAppearance({type:1,value,slot:0},[500,2000]).tier,tier);
assert.equal(bonusAppearance({type:1,value:2000,slot:0},null).tier,'low');assert.equal(bonusAppearance({type:1,value:2000,slot:5},[50,500]).tier,'low');assert.equal(bonusAppearance({type:1,value:-2000,slot:0},[500,2000]).tier,'negative');
assert(bonusAppearance({type:1,value:1000,slot:0},[500,2000]).strength>bonusAppearance({type:1,value:500,slot:0},[500,2000]).strength);
console.log('Gerçek pet ve şebnem kayıtları, simya kodlaması, kalite ve belirsiz oran kontrolleri geçti.');db.close();
for(const v of [0,1,20,60])assert.equal(bonusAppearance({type:72,value:v,slot:0},null).tier,'low');assert.equal(bonusAppearance({type:72,value:-1,slot:0},null).tier,'negative');

const {rareBonusRange}=require('./bonus-theme.js');
for(const [type,max]of [[214,8],[53,50],[55,50],[249,15],[260,12],[153,5],[223,25],[1,500]])for(const slot of [5,6])assert.equal(bonusAppearance({type,value:max,slot},rareBonusRange(type)).tier,'max');
assert.equal(bonusAppearance({type:214,value:7,slot:5},rareBonusRange(214)).tier,'high');
assert.equal(bonusAppearance({type:214,value:4,slot:5},rareBonusRange(214)).tier,'mid');
assert.equal(bonusAppearance({type:214,value:9,slot:5},rareBonusRange(214)).tier,'low');
assert.equal(bonusAppearance({type:999,value:8,slot:5},null).tier,'low');
console.log('6–7 kalite sınırları, iki nadir slot ve sınır dışı değerler doğrulandı.');
