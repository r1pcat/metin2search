const assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite'),{createCategoryCatalog,buildFishCatalog}=require('./category-catalog.cjs');
const proto={27810:{Type:'ITEM_FISH',SubType:'FISH_ALIVE',Value0:'23'},27840:{Type:'ITEM_FISH',SubType:'FISH_DEAD'},27870:{Type:'ITEM_USE'},27824:{Type:'ITEM_FISH',SubType:'FISH_ALIVE',Value0:'23'},27854:{Type:'ITEM_FISH',SubType:'FISH_DEAD'},27884:{Type:'ITEM_USE'},28400:{Type:'ITEM_METIN'},28404:{Type:'ITEM_METIN'},30006:{Type:'ITEM_MATERIAL'},30007:{Type:'ITEM_MATERIAL'}};
const names={27810:'Zargana',27840:'Ölü Zargana',27870:'Izgara Zargana',27824:'Yılan başı balığı',27854:'Ölü yılan başı balığı',27884:'Izgara yılan başı balığı'};
const catalog=createCategoryCatalog(proto,names),db=new DatabaseSync(':memory:');db.exec('CREATE TABLE active_items(id TEXT,category INTEGER,sub INTEGER,vnum INTEGER,name TEXT)');
for(const [id,cat,sub,v,name]of [['live',7,2,27810,'Zargana'],['dead',7,2,27840,'Ölü Zargana'],['grilled',7,2,27870,'Izgara Zargana'],['other-fish',7,2,27884,'Izgara yılan başı balığı'],['raw-material',5,0,30006,'Ork Dişi'],['plus-material',5,0,30007,'Ork Dişi+'],['stone0',5,1,28400,'Savaşçı Taşı+0'],['stone4',5,1,28404,'Savaşçı Taşı+4']])db.prepare('INSERT INTO active_items VALUES(?,?,?,?,?)').run(id,cat,sub,v,name);
function list(q){const {where,args}=catalog.conditions(new URLSearchParams(q),db,1);return db.prepare('SELECT id FROM active_items items'+(where.length?' WHERE '+where.join(' AND '):'')+' ORDER BY id').all(...args).map(r=>r.id)}
assert.deepEqual(list({category:'7',sub:'2',fishSpecies:'27810',fishGrilled:'0'}),['dead','live']);
assert.deepEqual(list({category:'7',sub:'2',fishSpecies:'27810',fishGrilled:'1'}),['grilled']);
assert.deepEqual(list({category:'7',sub:'2'}),['grilled','other-fish']);
assert.equal(buildFishCatalog(proto,names).length,2,'Fish using the same Value0 remain distinct species');
const options=catalog.publicOptions(db,1),stone=options.families.find(f=>f.label==='Savaşçı Taşı');assert(stone);assert.deepEqual(list({catalogFamily:stone.id}),['stone0','stone4']);
assert.equal(options.families.filter(f=>f.label.startsWith('Ork Dişi')).length,2,'Plus materials are not merged with normal materials');
assert(options.families.every(f=>!Object.hasOwn(f,'vnums')));assert.equal(options.fish.find(f=>f.id==='27810').grilledCount,1);
assert.throws(()=>list({fishSpecies:'bad'}));assert.throws(()=>list({fishGrilled:'2'}));db.close();console.log('Fish species/state, paired Vnums, material variants, refinement families and category scope passed.');
