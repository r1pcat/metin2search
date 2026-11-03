const JOB_FLAGS=['ANTI_MUSA','ANTI_ASSASSIN','ANTI_SURA','ANTI_MUDANG','ANTI_WOLFMAN'];
const COSTUMES=new Set(['COSTUME_WEAPON','COSTUME_BODY','COSTUME_HAIR']);
const WEAPON_JOBS={'0':[0,1,2],'1':[1],'2':[1],'3':[0],'4':[3],'5':[3],'8':[4]};
const WEAPONS=[['0','WEAPON_SWORD','Tek elli silahlar',0],['1','WEAPON_DAGGER','Hançerler',1],['2','WEAPON_BOW','Yaylar',2],['3','WEAPON_TWO_HANDED','Çift elli silahlar',3],['4','WEAPON_BELL','Çanlar',4],['5','WEAPON_FAN','Yelpazeler',6],['8','WEAPON_CLAW','Pençeler',5]];
const GENDER_EXTRA={e:[41983,45727],k:[41985,45729]};
function resolveEquipmentProto(proto,vnum){return proto[vnum]||(vnum>=110000&&vnum<180000&&proto[Math.floor(vnum/100)*100]?.Type==='ITEM_DS'?proto[Math.floor(vnum/100)*100]:null)}
function expandedEquipmentProto(proto){const expanded={...proto};for(const [v,p]of Object.entries(proto))if(p.Type==='ITEM_DS'&&Number(v)%100===0)for(let upgrade=1;upgrade<=6;upgrade++){const id=Number(v)+upgrade*10;if(!expanded[id])expanded[id]=p}return expanded}
function equipmentTypeKey(p){
 if(p.Type==='ITEM_WEAPON')return WEAPONS.find(w=>w[1]===p.SubType)?.[0]!=null?'weapon-'+WEAPONS.find(w=>w[1]===p.SubType)[0]:null;
 if(p.Type==='ITEM_ARMOR')return 'armor-'+p.SubType;
 if(p.Type==='ITEM_COSTUME')return p.SubType==='COSTUME_WEAPON'?'costume-weapon-'+p.Value3:COSTUMES.has(p.SubType)?'costume-'+p.SubType:null;
 if(p.Type==='ITEM_DS')return 'alchemy-'+p.SubType;
 if(p.Type==='ITEM_BELT')return 'belt';return null;
}
function buildEquipmentTypes(proto){
 const definitions=[...WEAPONS.map(([id,,label,sub])=>({id:'weapon-'+id,label,category:2,sub})),...WEAPONS.map(([id,,label])=>({id:'costume-weapon-'+id,label:({'0':'Tek elli silah','1':'Hançer','2':'Yay','3':'Çift elli silah','4':'Çan','5':'Yelpaze','8':'Pençe'})[id]+' görünümleri',category:9,sub:0})),
 ...[['ARMOR_BODY','Zırhlar',3,0],['ARMOR_HEAD','Miğferler',3,1],['ARMOR_SHIELD','Kalkanlar',3,2],['ARMOR_GLOVE','Eldivenler',3,3],['ARMOR_WRIST','Bilezikler',4,0],['ARMOR_FOOTS','Ayakkabılar',4,1],['ARMOR_NECK','Kolyeler',4,2],['ARMOR_EAR','Küpeler',4,3],['ARMOR_PENDANT','Tılsımlar',4,5]].map(([type,label,category,sub])=>({id:'armor-'+type,label,category,sub})),
 ...['Elmas','Yakut','Yeşim','Safir','Grena','Oniks','Ametist'].map((label,i)=>({id:'alchemy-DS_SLOT'+(i+1),label,category:8,sub:i})),
 {id:'belt',label:'Kemerler',category:4,sub:4},{id:'costume-COSTUME_BODY',label:'Gövde kostümleri',category:9,sub:1},{id:'costume-COSTUME_HAIR',label:'Saç modelleri',category:9,sub:2}];
 return definitions.map(d=>{const jobs=new Set();for(const p of Object.values(proto))if(equipmentTypeKey(p)===d.id&&!String(p.AntiFlags||'').includes('ANTI_MYSHOP'))for(const j of equipmentTraits(p).allowedJobs||[])jobs.add(j);return {...d,jobs:[...jobs].sort()}});
}
function equipmentTraits(proto){
 if(!proto)return {character:false,bonusCount:false};
 const wearable=proto.Type==='ITEM_WEAPON'&&!['WEAPON_ARROW','WEAPON_QUIVER','WEAPON_MOUNT_SPEAR'].includes(proto.SubType)||proto.Type==='ITEM_ARMOR'||proto.Type==='ITEM_COSTUME'&&COSTUMES.has(proto.SubType);
 const alchemy=proto.Type==='ITEM_DS';
 const flags=new Set(String(proto.AntiFlags||'').split(/\s*\|\s*/));
 const weaponType=proto.Type==='ITEM_WEAPON'?WEAPONS.find(w=>w[1]===proto.SubType)?.[0]:proto.Type==='ITEM_COSTUME'&&proto.SubType==='COSTUME_WEAPON'?String(proto.Value3):null;
 const allowedJobs=wearable?JOB_FLAGS.flatMap((f,i)=>flags.has(f)||weaponType!==null&&!(WEAPON_JOBS[weaponType]||[]).includes(i)?[]:[i]):[];
 return {character:wearable&&allowedJobs.length<5,bonusCount:wearable||alchemy,maxBonusCount:alchemy?6:proto.Type==='ITEM_COSTUME'&&COSTUMES.has(proto.SubType)?3:wearable?7:0,allowedJobs,male:!flags.has('ANTI_MALE'),female:!flags.has('ANTI_FEMALE')};
}
function createEquipmentFilters(proto){
 const entries=Object.entries(expandedEquipmentProto(proto)).map(([vnum,p])=>({vnum:Number(vnum),type:equipmentTypeKey(p),...equipmentTraits(p)})),byJob=JOB_FLAGS.map((_,i)=>JSON.stringify(entries.filter(e=>e.allowedJobs.includes(i)).map(e=>e.vnum))),bonusVnums=JSON.stringify(entries.filter(e=>e.bonusCount).map(e=>e.vnum)),byGender={e:JSON.stringify([...entries.filter(e=>e.male&&e.allowedJobs.length).map(e=>e.vnum),...GENDER_EXTRA.e]),k:JSON.stringify([...entries.filter(e=>e.female&&e.allowedJobs.length).map(e=>e.vnum),...GENDER_EXTRA.k])};
 const byType=new Map([...new Set(entries.map(e=>e.type).filter(Boolean))].map(type=>[type,JSON.stringify(entries.filter(e=>e.type===type).map(e=>e.vnum))]));
 return function equipmentConditions(query,readTable='active_items'){
  const where=[],args=[],job=query.get('character');
  if(job!==null&&job!==''){if(!/^[0-4]$/.test(job))throw Error('Invalid character filter');where.push('items.vnum IN (SELECT value FROM json_each(?))');args.push(byJob[Number(job)])}
  const gender=query.get('gender');if(gender!==null&&gender!==''){if(!byGender[gender])throw Error('Invalid gender filter');where.push('items.vnum IN (SELECT value FROM json_each(?))');args.push(byGender[gender])}
  const type=query.get('equipmentType');if(type){if(!byType.has(type))throw Error('Invalid equipment type');where.push('items.vnum IN (SELECT value FROM json_each(?))');args.push(byType.get(type))}
  const raw=query.get('bonusCount');
  if(raw){if(!/^[1-7](?:,[1-7])*$/.test(raw))throw Error('Invalid bonus count');const counts=[...new Set(raw.split(',').map(Number))];where.push('items.vnum IN (SELECT value FROM json_each(?))');args.push(bonusVnums);where.push((readTable==='web_search_active'?'items.bonus_count':'(SELECT COUNT(DISTINCT b.slot) FROM item_bonuses b WHERE b.item_id=items.id AND b.slot>=0 AND b.slot<7 AND b.type>0)')+' IN ('+counts.map(()=>'?').join(',')+')');args.push(...counts)}
  return {where,args};
 };
}
module.exports={equipmentTraits,createEquipmentFilters,buildEquipmentTypes,equipmentTypeKey,resolveEquipmentProto,JOB_FLAGS};
