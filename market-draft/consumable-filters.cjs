const EFFECTS=[
 ['move','Hareket hızı',/^Hareket Hızı/i],['attack-speed','Saldırı hızı',/^Saldırı Hızı/i],['cast-speed','Büyü hızı',/^Büyü Hızı/i],
 ['critical','Kritik vuruş şansı',/^Kritik Vuruş.*şans/i],['piercing','Delici vuruş şansı',/^Delici.*şans/i],
 ['attack','Saldırı değeri',/^Saldırı Değeri/i],['magic-attack','Büyülü saldırı değeri',/^Büyülü Saldırı Değeri/i],['defence','Savunma',/^(Savunma|Defans)\b/i],['magic-resist','Büyüye karşı dayanıklılık',/^(Büyüye karşı dayanıklılık|Büyü Direnci)/i],
 ['hp','HP yenileme',/^HP\s*:/i,/^(Kırmızı [İi]ksir|Güneş Özütü|Yaşam Kutsaması)/i],['sp','SP yenileme',/^SP\s*:/i,/^(Mavi [İi]ksir|Ay Özütü|Sihir Kutsaması)/i],
 ['hp-max','Maksimum HP',/^(Max\.?\s*HP|Maksimum HP)/i],['sp-max','Maksimum SP',/^(Max\.?\s*SP|Maksimum SP)/i],['strength','Güç',/^Güç\s*[+:]/i],['experience','Tecrübe bonusu',/^(EXP|Tecrübe|Deneyim).*([+%]|bonus)/i],['block-pierce','Engelleme nüfuzu',/Engelleme nüfuzu/i],
 ...[['fire','Ateş','Ateşin'],['ice','Buz','Buzun'],['wind','Rüzgar','Rüzgarın'],['lightning','Şimşek','(?:Şimşeğin|Şimşeklerin)'],['earth','Toprak','Toprağın'],['dark','Karanlık','Karanlığın']].flatMap(([id,label,prefix])=>[
 [id+'-power',label+' gücü',new RegExp('^'+prefix+' gücü','i')],
 [id+'-resist',label+' direnci',new RegExp('^'+label+'(?:a|e)?(?: karşı)? (?:Direnci|Dayanıklılığı)','i')]
 ])
];
function effectsFor(row){const result=[];for(const line of row.effects||[])for(const [id,,pattern] of EFFECTS)if(pattern.test(line)){const match=String(line).match(/[-+]?\d+(?:[.,]\d+)?/);result.push({id,value:match?Number(match[0].replace(',','.')):null})}for(const [id,,,names] of EFFECTS)if(names?.test(row.name||'')&&!result.some(e=>e.id===id))result.push({id,value:null});return result}
const cache=new WeakMap();
function consumableConditions(query,db,revision){
 const id=query.get('consumableEffect');if(!id)return {where:[],args:[]};if(!EFFECTS.some(e=>e[0]===id))throw Error('Invalid consumable effect');
 let indexed=cache.get(db);if(!indexed||indexed.revision!==revision){const effects=new Map(EFFECTS.map(e=>[e[0],[]]));for(const row of db.prepare("SELECT id,name,json_extract(detail,'$.special.consumable.effects') effects FROM "+require('./search-projection.cjs').searchTable(db)+" WHERE category=6").all()){let lines=[];try{lines=JSON.parse(row.effects||'[]')}catch{}for(const e of effectsFor({...row,effects:lines}))effects.get(e.id).push({id:row.id,value:e.value})}indexed={revision,effects};cache.set(db,indexed)}
 const min=query.get('consumableMin'),threshold=min!==null&&min!==''?Number(min):null;if(threshold!==null&&!Number.isFinite(threshold))throw Error('Invalid effect minimum');
 const ids=[...new Set(indexed.effects.get(id).filter(e=>threshold===null||e.value!==null&&e.value>=threshold).map(e=>e.id))];
 return {where:['items.id IN (SELECT value FROM json_each(?))'],args:[JSON.stringify(ids)]};
}
module.exports={effectsFor,consumableConditions,consumableEffects:EFFECTS.map(([id,label])=>({id,label}))};
