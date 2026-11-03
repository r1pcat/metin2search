const baseName=x=>String(x.Name||'').replace(/\+\d+\s*$/,'').trim();
const unit=x=>(Number(x.Won)*1e8+Number(x.Yang))/Math.max(1,Number(x.Count));
const effects=x=>x.displayBonuses||x.ClientDetails?.bonuses||[];
const isAlchemy=x=>x.ClientDetails?.itemType===29||x.category===8;
const bonusKey=(x,b)=>b.type+'|'+(isAlchemy(x)?'simya':'first');
const counted=x=>effects(x).filter(b=>isAlchemy(x)||!(Number(b.slot)>=5));
const upgrade=x=>Number(String(x.Name||'').match(/\+(\d+)\s*$/)?.[1]||0);
const shownName=x=>String(x.ClientDetails?.lines?.[0]||x.Name||'').replace(/\+\d+\s*$/,'').trim();
const needed=n=>n<=2?n:Math.ceil(n*.75);
const closeness=(a,b)=>{const x=Math.abs(Number(a)),y=Math.abs(Number(b));return x===0&&y===0?1:Math.min(x,y)/Math.max(x,y)};
function findSimilar(source,candidates){
 const sourceBonuses=counted(source),sourceElement=source.ClientDetails?.element||{},gear=[1,2,28,29].includes(source.ClientDetails?.itemType),seen=new Set();
 return candidates.flatMap(x=>{
  if(x.id===source.id||unit(x)<=0||unit(x)>=unit(source))return [];
  if(!gear){if(x.Vnum!==source.Vnum||shownName(x)!==shownName(source))return []}
  else if(baseName(x)!==baseName(source))return [];
  if(isAlchemy(source)&&x.Vnum!==source.Vnum)return [];
  const key=JSON.stringify([x.Seller,x.feature_key||[x.Vnum,x.displayBonuses,x.ClientDetails?.bonuses,x.ClientDetails?.element,x.Won,x.Yang]]);if(seen.has(key))return [];seen.add(key);
  const candidateBonuses=counted(x),pairs=sourceBonuses.map(b=>({b,c:candidateBonuses.find(c=>bonusKey(source,b)===bonusKey(x,c))})),shared=pairs.filter(p=>p.c),distance=Math.abs(upgrade(x)-upgrade(source));
  if(gear&&(distance>2||shared.length<needed(sourceBonuses.length)))return [];
  const element=x.ClientDetails?.element||{},sameElement=element.applyType===sourceElement.applyType&&element.grade===sourceElement.grade;
  const differences=shared.filter(p=>p.c.value!==p.b.value).map(p=>({label:p.b.text,current:p.b.value,alternative:p.c.value,direction:Math.sign(p.c.value-p.b.value)}));
  const missing=pairs.filter(p=>!p.c).map(p=>({label:p.b.text,current:p.b.value})),extra=candidateBonuses.filter(c=>!sourceBonuses.some(b=>bonusKey(source,b)===bonusKey(x,c))).map(c=>({label:c.text,alternative:c.value}));
  const sameValues=shared.length-differences.length,lowerCount=differences.filter(d=>d.direction<0).length,higherCount=differences.filter(d=>d.direction>0).length;
  const score=shared.length*20+shared.reduce((s,p)=>s+closeness(p.b.value,p.c.value),0)*10+sameValues*3-distance*8+(sameElement?12:0);
  return [{item:x,shared:shared.length,sourceBonuses:sourceBonuses.length,nearbyBonuses:shared.length,sameQuality:isAlchemy(source)?x.Vnum===source.Vnum:null,sameElement,upgradeDifference:upgrade(x)-upgrade(source),differences,missing,extra,sameValues,lowerCount,higherCount,saving:unit(source)-unit(x),savingPercent:(1-unit(x)/unit(source))*100,score}];
 }).sort((a,b)=>b.score-a.score||unit(a.item)-unit(b.item)).slice(0,3);
}
module.exports={findSimilar,needed};
