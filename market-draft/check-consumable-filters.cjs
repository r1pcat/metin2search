const assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite'),{effectsFor,consumableConditions}=require('./consumable-filters.cjs');
const db=new DatabaseSync(':memory:');db.exec('CREATE TABLE active_items(id TEXT,name TEXT,category INTEGER,detail TEXT)');
const put=db.prepare('INSERT INTO active_items VALUES(?,?,6,?)');
for(const [id,name,effects]of [['purple','Mor İksir(B)',['Hareket Hızı: +30']],['red','Kırmızı Şebnem',['Kritik Vuruş Şansı: +%20']],['white','Beyaz Şebnem',['Savunma +100']],['multi','Çok etkili örnek',['Hareket Hızı: +10','Kritik Vuruş Şansı: +%20']],['sun','Güneş Özütü (K)',[]]])put.run(id,name,JSON.stringify({special:{consumable:{effects}}}));
function list(q,rev=1){const {where,args}=consumableConditions(new URLSearchParams(q),db,rev);return db.prepare('SELECT id FROM active_items items'+(where.length?' WHERE '+where.join(' AND '):'')+' ORDER BY id').all(...args).map(r=>r.id)}
assert.deepEqual(list({consumableEffect:'move',consumableMin:'20'}),['purple']);
assert.deepEqual(list({consumableEffect:'critical',consumableMin:'15'}),['multi','red'],'Compare the selected effect value, not the first effect');
assert.deepEqual(list({consumableEffect:'hp'}),['sun']);assert.deepEqual(list({consumableEffect:'hp',consumableMin:'1'}),[],'Unknown regeneration strength must not be invented');
assert(effectsFor({effects:['Delici Vuruş için şansı %12']}).some(e=>e.id==='piercing'&&e.value===12));
assert(effectsFor({effects:['Şimşeklerin gücü +20%']}).some(e=>e.id==='lightning-power'));
assert.throws(()=>list({consumableEffect:'not-real'}));
put.run('new','Beyaz Şebnem',JSON.stringify({special:{consumable:{effects:['Savunma +200']}}}));assert.deepEqual(list({consumableEffect:'defence',consumableMin:'150'},2),['new']);
db.close();console.log('Potion effect mapping, exact selected-effect minimum, unknown values and revision invalidation passed.');
