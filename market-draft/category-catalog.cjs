const crypto=require('node:crypto');
const FAMILY_CATEGORIES=new Set([1,5,7,11,12]);
function buildFishCatalog(proto,names){
 const fish=[];
 for(const [key,p]of Object.entries(proto))if(p.Type==='ITEM_FISH'&&p.SubType==='FISH_ALIVE'){
  const vnum=Number(key),normal=[vnum],grilled=[];
  if(vnum>=27803&&vnum<=27832){if(proto[vnum+30]?.Type==='ITEM_FISH'&&proto[vnum+30]?.SubType==='FISH_DEAD')normal.push(vnum+30);if(/^Izgara\b|^Izg\./i.test(names[vnum+60]||''))grilled.push(vnum+60)}
  fish.push({id:String(vnum),label:names[vnum]||'Balık '+vnum,normal,grilled});
 }
 return fish.sort((a,b)=>a.label.localeCompare(b.label,'tr'));
}
function familyGroup(p={}){if(p.SubType==='COSTUME_MOUNT')return 'Binek';if(p.SubType==='PET_BAG')return 'Nakliye kutusu';if(p.SubType==='PET_EGG')return 'Pet yumurtası';if(p.SubType==='PET_SKILL')return 'Pet becerisi';if(p.Type==='ITEM_PET')return 'Pet ürünü';if(p.Type==='ITEM_METIN')return 'Zihin taşı';if(p.SubType==='USE_PUT_INTO_ACCESSORY_SOCKET')return 'Takı cevheri';return ''}
function createCategoryCatalog(proto,names){
 const fish=buildFishCatalog(proto,names),fishById=new Map(fish.map(f=>[f.id,f])),cache=new WeakMap();
 function get(db,revision){let state=cache.get(db);if(state?.revision===revision)return state;
  const published=db.prepare("SELECT 1 FROM sqlite_master WHERE name='meta'").get()?db.prepare("SELECT value FROM meta WHERE key='web_context_revision'").get():null;
  const prepared=Number(published?.value)===Number(revision)?db.prepare("SELECT json_extract(value,'$.catalogRows') rows FROM meta WHERE key='web_context'").get():null;
  const rows=prepared?.rows?JSON.parse(prepared.rows):db.prepare('SELECT category,sub,vnum,name,COUNT(*) count FROM '+require('./search-projection.cjs').searchTable(db)+' GROUP BY category,sub,vnum,name').all(),families=new Map(),counts=new Map();
  for(const r of rows){counts.set(Number(r.vnum),(counts.get(Number(r.vnum))||0)+Number(r.count));if(!FAMILY_CATEGORIES.has(r.category)||r.category===7&&Number(r.sub)===2)continue;
   const p=proto[r.vnum]||{},label=['ITEM_METIN','ITEM_ROD','ITEM_PICK'].includes(p.Type)?r.name.replace(/\s*\+\s*\d+\s*$/,'').trim():r.name;
   const id=r.category+'-'+r.sub+'-'+crypto.createHash('sha1').update(label).digest('hex').slice(0,12);let family=families.get(id);if(!family){family={id,label,category:r.category,sub:Number(r.sub),group:familyGroup(p),vnums:[],count:0};families.set(id,family)}family.vnums.push(Number(r.vnum));family.count+=Number(r.count);
  }
  const list=[...families.values()].sort((a,b)=>a.label.localeCompare(b.label,'tr'));state={revision,families,list,fish:fish.map(f=>({id:f.id,label:f.label,normalCount:f.normal.reduce((n,v)=>n+(counts.get(v)||0),0),grilledCount:f.grilled.reduce((n,v)=>n+(counts.get(v)||0),0)}))};cache.set(db,state);return state;
 }
 function publicOptions(db,revision){const state=get(db,revision);return {families:state.list.map(({vnums,...f})=>f),fish:state.fish}}
 function conditions(query,db,revision){const where=[],args=[],family=query.get('catalogFamily'),species=query.get('fishSpecies'),state=query.get('fishGrilled'),fishScope=Number(query.get('category'))===7&&query.get('sub')==='2';
  if(family){const entry=get(db,revision).families.get(family);if(!entry)throw Error('Invalid category product');where.push('items.category=? AND items.sub=? AND items.vnum IN (SELECT value FROM json_each(?))');args.push(entry.category,entry.sub,JSON.stringify([...new Set(entry.vnums)]))}
  if(fishScope||species||state!==null&&state!==''){
   if(species&&!fishById.has(species))throw Error('Invalid fish species');if(state!==null&&state!==''&&!['0','1'].includes(state))throw Error('Invalid fish state');
   const chosen=species?[fishById.get(species)]:fish,ids=chosen.flatMap(f=>state==='0'?f.normal:f.grilled);where.push('items.vnum IN (SELECT value FROM json_each(?))');args.push(JSON.stringify(ids));
  }
  return {where,args};
 }
 return {publicOptions,conditions};
}
module.exports={createCategoryCatalog,buildFishCatalog,FAMILY_CATEGORIES};
