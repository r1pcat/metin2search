const FILTER_GROUPS=[
 ['product','Ürün seçimi',['catalogFamily']],['fish','Balık seçimi',['fishSpecies','fishGrilled']],
 ['character','Karakter koşulu',['character']],['gender','Cinsiyet koşulu',['gender']],['equipment-type','Eşya türü',['equipmentType']],['bonus-count','Efsun adedi',['bonusCount']],
 ['price','Fiyat aralığı',['min','max']],['level','Seviye aralığı',['level','levelMax']],
 ['upgrade','Yükseltme aralığı',['upgradeMin','upgradeMax']],['quantity','Miktar aralığı',['qtyMin','qtyMax']],
 ['element','Element koşulu',['element','grade']],['stones','Taş koşulu',['stones']],
 ['energy','Enerji koşulu',['energyMin','energyMax']],['absorb','Emiş koşulu',['absorbMin','absorbMax']],
 ['pet','Pet özellikleri',['petState','petSpecies','petType','petLevelMin','petLevelMax']],
 ['alchemy','Simya özellikleri',['alchemyClass','alchemyClarity','alchemyUpgradeMin']],
 ['consumable','İksir koşulları',['consumableEffect','consumableMin','durationMin']],
 ['remaining','Kalan süre koşulu',['timed','remainMin','remainMax']],
 ['orb','Dönüşen yaratık',['orb']]
];
function reliefCandidates(query){
 const out=FILTER_GROUPS.filter(([, ,keys])=>keys.some(k=>query.has(k)&&query.get(k)!=='')).map(([id,label,keys])=>({id,label,keys}));
 let bonuses=[];try{const parsed=JSON.parse(query.get('bonusFilters')||'[]');if(Array.isArray(parsed))bonuses=parsed.slice(0,7)}catch{}
 bonuses.forEach((b,index)=>out.push({id:'bonus-'+index,label:'Efsun koşulu',bonusIndex:index,type:Number(b.type),value:Number(b.value)}));
 if(query.has('stat'))out.push({id:'legacy-bonus',label:'Efsun koşulu',keys:['stat','statMin']});
 if(query.get('sub'))out.push({id:'subcategory',label:'Alt kategori',keys:['sub']});
 const inCategory=query.get('category')&&query.get('category')!=='0';
 if(inCategory&&query.get('q'))out.unshift({id:'query',label:'Eşya araması',keys:['q','itemFamily']});
 else if(inCategory)out.push({id:'category',label:'Kategori',keys:['category','sub']});
 return out;
}
function relaxedQuery(query,candidate){const next=new URLSearchParams(query);next.set('page','1');for(const k of candidate.keys||[]){if(k==='category')next.set('category','0');else next.delete(k)}if(candidate.bonusIndex!=null){const bonuses=JSON.parse(next.get('bonusFilters')||'[]');bonuses.splice(candidate.bonusIndex,1);if(bonuses.length)next.set('bonusFilters',JSON.stringify(bonuses));else next.delete('bonusFilters')}return next}
function filterRelief(query,count){
 const base=count(query);if(base>0)return {total:base,suggestions:[]};
 const candidates=reliefCandidates(query),suggestions=[];
 for(const c of candidates){const total=count(relaxedQuery(query,c));if(total>0)suggestions.push({...c,total})}
 if(!suggestions.length&&candidates.length>1){const keys=[...new Set(candidates.flatMap(c=>c.keys||[]))];const all={id:'all-conditions',label:'Ek filtreler',keys:[...keys,'bonusFilters']},total=count(relaxedQuery(query,all));if(total>0)suggestions.push({...all,total})}
 return {total:base,suggestions:suggestions.sort((a,b)=>(b.id==='query')-(a.id==='query')||a.total-b.total).slice(0,4)};
}
module.exports={filterRelief,reliefCandidates,relaxedQuery};
