(()=>{
const $=id=>document.getElementById(id);
const url=new URLSearchParams(location.search);
let SERVER=url.get('server')||'safir',SERVER_NAME='Safir';
const AD_AFTER=5, MAX_CMP=3;
const AD_MOBILE=[3,8,13],adAt=n=>matchMedia('(max-width:760px)').matches?AD_MOBILE.includes(n):n===AD_AFTER;
const CATS=[['Tümü','54705'],['Binek & Pet','71125'],['Silah','00360'],['Ekipman','21310'],['Mücevher','14610'],['Geliştirme','31233'],['İksir','50821'],['Balık & Maden','29101'],['Simya','125400'],['Kostüm','41936'],['Beceri','book_01'],['Özel eşya','70050'],['Diğer','80016']];
const ELEMENTS={99:['Şimşek','#d6bf5c','lightning'],100:['Ateş','#d77a52','fire'],101:['Buz','#6eaed6','ice'],102:['Rüzgar','#79bd8e','wind'],103:['Toprak','#bf9562','earth'],104:['Karanlık','#9c86cf','dark']};
const JOBS=[['0','Savaşçı'],['1','Ninja'],['2','Sura'],['3','Şaman'],['4','Lycan']];
const FILTER_KEYS=['min','max','level','levelMax','upgradeMin','upgradeMax','qtyMin','qtyMax','grade','element','energyMin','energyMax','absorbMin','absorbMax','petState','petType','petSpecies','petLevelMin','petLevelMax','alchemyClass','alchemyClarity','alchemyUpgradeMin','consumableMin','consumableEffect','durationMin','orb','gender','remainMin','remainMax','character','bonusCount','equipmentType','catalogFamily','fishSpecies','fishGrilled'];
const ADV_KEYS=['min','max','level','levelMax','upgradeMin','upgradeMax','qtyMin','qtyMax'];
const SPECIAL_KEYS=FILTER_KEYS.filter(k=>!ADV_KEYS.includes(k));
const LABELS={min:'Fiyat ≥',max:'Fiyat ≤',level:'Seviye ≥',levelMax:'Seviye ≤',upgradeMin:'+ ≥',upgradeMax:'+ ≤',qtyMin:'Adet ≥',qtyMax:'Adet ≤',grade:'Element ≥ +',energyMin:'Enerji ≥',energyMax:'Enerji ≤',absorbMin:'Emiş ≥ %',absorbMax:'Emiş ≤ %',petLevelMin:'Pet sv. ≥',petLevelMax:'Pet sv. ≤',alchemyUpgradeMin:'Simya + ≥',consumableMin:'Etki ≥',durationMin:'Süre ≥ dk',remainMin:'Kalan süre ≥ gün',remainMax:'Kalan süre ≤ gün'};
const DEFAULT_LINE='#c1c1c1', BASE_LINE='#89b88d', BONUS_LINE='#b0dfb4', RARE_LINE='#ffff9a';

const nf=new Intl.NumberFormat('tr-TR'),nf2=new Intl.NumberFormat('tr-TR',{maximumFractionDigits:2});
const compact=v=>v>=999995?nf2.format(v/1e6)+'M':v>=1000?nf2.format(v/1000)+'K':nf2.format(v);
const rtf=new Intl.RelativeTimeFormat('tr',{numeric:'always'});
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const read=k=>{try{return JSON.parse(localStorage.getItem(k))}catch{return null}},write=(k,v)=>{if(window.M2Consent&&!window.M2Consent.allows('prefs'))return;try{localStorage.setItem(k,JSON.stringify(v))}catch{}};
const api=(p,q={})=>fetch('/api/'+p+'?'+new URLSearchParams({server:SERVER,...q})).then(r=>r.ok?r.json():Promise.reject(r.status));
const MISSING='/assets/items/missing.svg';
const img=(src,alt='',fb=MISSING)=>`<img src="${esc(src)}" alt="${esc(alt)}" loading="lazy" onerror="this.onerror=null;this.src='${esc(fb)}'">`;
const itemSrc=icon=>'/assets/items/'+encodeURIComponent(icon||'missing.svg');
const iconOf=it=>itemSrc(it.icon||String(it.Vnum).padStart(5,'0')+'.png');
const itemImg=it=>img(iconOf(it),it.ClientDetails?.lines?.[0]||it.Name,CATS[it.category]?'/assets/items/'+CATS[it.category][1]+'.png':MISSING);
const ago=iso=>{if(!iso)return'';const elapsed=Date.now()-new Date(iso).getTime();if(!Number.isFinite(elapsed))return'';const minutes=Math.max(1,Math.floor(elapsed/60000));return minutes<60?rtf.format(-minutes,'minute'):minutes<1440?rtf.format(-Math.floor(minutes/60),'hour'):rtf.format(-Math.floor(minutes/1440),'day')};
const dateFmt=iso=>new Date(iso).toLocaleString('tr-TR',{timeZone:'Europe/Istanbul',day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit'});
const priceValue=it=>(it.Won||0)*1e8+(it.Yang||0);
const unitValue=it=>priceValue(it)/Math.max(1,it.Count||1);
const moneyText=v=>v>=1e8?nf2.format(v/1e8)+' Won':nf.format(Math.round(v))+' Yang';
const unitShort=v=>v>=1e8?compact(v/1e8)+' Won':compact(v)+' Yang';
const priceText=it=>[it.Won?nf.format(it.Won)+' Won':'',it.Yang||!it.Won?nf.format(it.Yang||0)+' Yang':''].filter(Boolean).join(' + ');
const WON_IMG='<img class="cur" src="/assets/items/won.png" alt="">',YANG_IMG='<img class="cur" src="/assets/items/money.png" alt="">';
function priceHTML(it,{unit=true}={}){const p=[];if(it.Won)p.push(`<span class="won">${WON_IMG}${nf.format(it.Won)}<small>Won</small></span>`);if(it.Yang||!it.Won)p.push(`<span class="yang">${YANG_IMG}${nf.format(it.Yang||0)}<small>Yang</small></span>`);
  if(unit&&it.Count>1){const v=unitValue(it);p.push(`<span class="unit ${v>=1e8?'u-won':'u-yang'}" title="Adet başı ${esc(moneyText(v))}">${esc(unitShort(v))} / adet</span>`)}return p.join('')}

const state={category:Number(url.get('category')||url.get('cat'))||0,sub:url.get('sub')??'',page:Number(url.get('page'))||1,sort:url.get('sort')||'unit-asc',
  q:url.get('q')||'',qFamily:url.get('itemFamily')==='1',seller:url.get('seller')||'',sellerExact:url.get('sellerExact')==='1',filters:{},bonus:[],view:read('m2s-v3-view')||'list',favView:false,
  saved:new Set(read('m2s-v3-saved')??(SERVER==='safir'?read('m2search-safir-favorites')||[]:[])),cmp:read('m2s-v3-cmp')||[],meta:null,revision:0,total:0,editBonus:-1};
for(const k of FILTER_KEYS)state.filters[k]=url.get(k)??'';
try{const bf=JSON.parse(url.get('bonusFilters')||'[]');if(Array.isArray(bf))state.bonus=bf.filter(b=>b&&Number.isFinite(Number(b.type))&&Number.isFinite(Number(b.value))).slice(0,7).map(b=>({...b,type:Number(b.type),value:Number(b.value)}))}catch{}
const items=new Map();for(const x of state.cmp)items.set(x.id,x);
let pageItems=[];

function statLabel(type){const f=state.meta?.stats?.[type];return f?f.replace(/\s*[+:]?\s*%%?d\s*/g,' ').replace(/\s+/g,' ').trim():'Efsun '+type}
function bonusMax(type,rare){try{return rare?(RARE_RANGES[type]?.[1]??null):(BONUS_STEPS[type]?.at(-1)??null)}catch{return null}}
function bonusInfo(b){const type=Number(b.type),value=Number(b.value),rare=Number(b.slot)>=5;let color='#6c7380';try{color=bonusAppearance(b).color}catch{}
  const max=bonusMax(type,rare);let tier='';try{const st=(rare?RARE_STEPS:BONUS_STEPS)[type];if(max&&value>=max)tier='Tam oran';else if(st&&st.length>2&&value>=st.at(-2))tier='Üst kademe'}catch{}
  return {type,value,rare,color,s:max?Math.max(.1,Math.min(1,value/max)):null,max:!!(max&&value>=max),neg:value<0,text:b.text,tier}}
function elementFrom(e,steps){const d=e&&ELEMENTS[e.applyType];if(!d||!e.grade)return null;return {text:e.text||(d[0]+' gücü +'+e.value+'%'),id:e.applyType,name:d[0],color:d[1],icon:d[2],grade:e.grade,value:e.value,power:steps?.power||[],attack:steps?.attack||[],verified:!!steps?.verified}}
const elementOf=it=>it.embedded?null:elementFrom(it.ClientDetails?.element,it.elementSteps);
const elementText=el=>{const base=String(el.text||'').trim(),steps=el.verified?el.power.slice(0,el.grade):[];return steps.length===el.grade&&steps.every(Number.isFinite)?`${base} (${steps.join(',')})`:base};
function placeElement(out,it){const el=elementOf(it);if(!el)return out;
  const wanted=new Set([el.text,it.ElementSummary].filter(Boolean).map(s=>String(s).trim())),at=out.findIndex(l=>wanted.has(String(l.t).trim())),line={t:elementText(el),el};
  if(at>=0)out[at]={...out[at],...line};
  else{const bonus=new Set((it.displayBonuses||[]).map(b=>String(b.text).trim())),first=out.findIndex(l=>bonus.has(String(l.t).trim()));out.splice(first>=0?first:Math.min(out.length,2),0,{...line,c:el.color})}
  return out}
const elementRowHTML=l=>`<div class="elrow" style="color:${l.c}">${elImg(l.el)}<span>${esc(l.t)}</span></div>${l.el.verified&&l.el.attack.slice(0,l.el.grade).some(Boolean)?`<div class="elsub">Saldırı artışı: ${l.el.attack.slice(0,l.el.grade).map(v=>'+'+v).join(' · ')}</div>`:''}`;
const elImg=e=>`<img src="/assets/${e.icon}.png" alt="">`;
const sockets=it=>[...(it.stones||[]).map(s=>({...s,kind:'stone'})),...(it.jewels||[]).map(s=>({...s,kind:'jewel'}))];
const itemLevel=it=>{const l=it.ClientDetails?.limits?.find(x=>x[0]===1);return l?l[1]:0};
const plusOf=it=>{const m=/\+(\d+)\s*(\(|$)/.exec(it.Name||'');return m&&m[1]!=='0'?m[1]:''};
const title=it=>it.ClientDetails?.lines?.[0]||it.ClientDetails?.rendererCalls?.AppendTextLine?.[0]?.[0]||it.Name||'';
const hasBox=it=>!!it.container&&it.category!==10;
const isNew=it=>state.meta?.latest&&(new Date(state.meta.latest)-new Date(it.observed))<15*60e3;
const catName=c=>state.meta?.categoryTree?.types?.[c]||CATS[c]?.[0]||'Eşya';
function subNames(c){const t=state.meta?.categoryTree?.subtypes?.[c]||{},o={};for(const [k,v]of Object.entries(t)){if(typeof v==='object')for(const [k2,v2]of Object.entries(v))o[k2]??=v2;else o[k]=v}return o}
const stoneIcons=(ss,cls='mini-stones')=>ss.length?`<div class="${cls}">${ss.map(s=>`<span title="${esc(s.name+((s.effects||[]).length?' — '+s.effects.join(', '):''))}">${img(itemSrc(s.icon),s.name)}${s.stage?`<b>${s.stage}</b>`:''}</span>`).join('')}${SocketDuration.timers(ss).map(t=>`<small class="socket-time" title="${esc(t.name+' — '+t.raw)}">${ic('clock')}<b>${esc(t.name)}</b> · ${esc(t.value)}</small>`).join('')}</div>`:'';

function lineColor(c){if(c==null||c==='')return DEFAULT_LINE;const n=(Number(c)>>>0)&0xFFFFFF;if(!Number.isFinite(n))return DEFAULT_LINE;const r=n>>16&255,g=n>>8&255,b=n&255;
  if(0.299*r+0.587*g+0.114*b<60)return DEFAULT_LINE;return '#'+n.toString(16).padStart(6,'0')}
const BASE_ATK=/^(?:Büyülü saldırı değeri|Saldırı değeri|Defans)\b/;
function baseGaps(it,seen,color){const tl=it.TooltipLines||[],have=new Set(seen.map(t=>String(t).trim())),gaps=[];
  for(let i=1;i<tl.length;i++){let t=String(tl[i]??'').trim();if(!BASE_ATK.test(t)||have.has(t))continue;const up=String(tl[i+1]??'').trim();if(/^\(\+\d+\)$/.test(up))t+=' '+up;if(!have.has(t)){have.add(t);gaps.push({t,c:color,base:true})}}
  return gaps}
function placeBaseGaps(out,gaps,it){const fan=it.ClientDetails?.itemSubType===5;
  for(const g of gaps){let at=-1;out.forEach((l,i)=>{if(/^(?:Seviye|Minimum|Büyülü saldırı değeri|Saldırı değeri)/.test(l.t))at=i});
    if(!/^Büyülü/.test(g.t)&&!fan){const m=out.findIndex(l=>/^Büyülü saldırı değeri/.test(l.t));if(m>=0)at=m-1}
    out.splice(at+1,0,g)}}
function gameLines(it){
  const rc=it.ClientDetails?.rendererCalls||{},calls=rc.AppendTextLine,hide=new Set(it.socketLinesToHide||[]),skip=new Set();
  for(const s of sockets(it)){skip.add(s.name);if(s.duration)skip.add(s.duration);(s.effects||[]).forEach(e=>skip.add(e))}
  const el0=it.ClientDetails?.element;if(!it.embedded&&el0?.grade&&ELEMENTS[el0.applyType])skip.add(ELEMENTS[el0.applyType][0]+' +'+el0.grade);
  if(!Array.isArray(calls)||!calls.length){const out=(it.TooltipLines||[]).slice(1).filter(t=>t&&!skip.has(t)).map(t=>({t,c:DEFAULT_LINE})),have=new Set(out.map(l=>l.t.trim()));for(const b of it.displayBonuses||[])if(!have.has(String(b.text).trim()))out.push({t:b.text,c:Number(b.slot)>=5?RARE_LINE:BONUS_LINE});return {name:[title(it),'#e6e3dc'],lines:placeElement(out,it)}}
  const out=[];calls.slice(1).forEach((l,i)=>{const raw=String(l[0]??'');if(hide.has(i+1)||!raw.trim()&&!l[3])return;if(skip.has(raw.trim()))return;const c=lineColor(l[1]);
    const parts=raw.split(/\\n|\n/);if(l[3]===true&&out.length){out.at(-1).t+=parts.shift()}for(const p of parts)if(p.trim())out.push({t:p,c})});
  const baseColor=BASE_LINE,sash=!!it.embedded||Array.isArray(rc.AppendTextLineAcce),acce=(rc.AppendTextLineAcce||[]).map(l=>({t:String(l[0]),c:lineColor(l[1]),acce:true}));
  const gaps=baseGaps(it,[...out,...acce].map(l=>l.t),baseColor);
  if(sash)acce.unshift(...gaps.map(g=>({...g,acce:true})));else placeBaseGaps(out,gaps,it);
  if(acce.length){const at=out.findIndex(l=>/Emiş oranı/i.test(l.t));out.splice(at>=0?at+1:out.length,0,...acce)}
  const have=new Set(out.map(l=>l.t.trim()));for(const b of it.displayBonuses||[])if(!have.has(String(b.text).trim()))out.push({t:b.text,c:Number(b.slot)>=5?RARE_LINE:BONUS_LINE});
  return {name:[calls[0][0]||it.Name,lineColor(calls[0][1])],lines:placeElement(out,it)};
}

function elementBlock(el,title='Element'){return el?`<div class="gt-sec"><h4>${title} <span>+${el.grade}</span></h4><div class="gt-el" style="--el:${el.color}">${elImg(el)}<div><strong>${esc(el.text)}</strong>
  <div class="lv">${[0,1,2].map(i=>`<span class="${i<el.grade?'on':''}">${['I','II','III'][i]}<b>${i<el.grade&&el.power[i]!=null?'+'+el.power[i]:'–'}</b></span>`).join('')}</div>
  ${el.attack.some(Boolean)?`<div class="atk">Saldırı artışı: ${el.attack.slice(0,el.grade).map(v=>'+'+v).join(' · ')}</div>`:''}</div></div></div>`:''}
function socketBlock(it){const ss=sockets(it);return ss.length?`<div class="gt-sec"><h4>${it.jewels?.length?'Takılı cevherler':'Takılı taşlar'} <span>${ss.length}</span></h4><div class="gt-sockets">${ss.map(s=>`<div class="sock"><div class="si">${img(itemSrc(s.icon),s.name)}${s.stage?`<b>${s.stage}</b>`:''}</div><div><strong>${esc(s.name)}</strong>${(s.effects||[]).map(e=>`<span>${esc(e)}</span>`).join('')}</div>${s.duration?`<small class="socket-time" title="${esc(s.name)}">${ic('clock')}${esc(SocketDuration.clean(s.duration))}</small>`:''}</div>`).join('')}</div></div>`:''}
function embeddedBlock(it){const e=it.embedded;if(!e)return'';const src=(it.ClientDetails?.bonuses||[]),el=elementFrom(it.ClientDetails?.element,it.elementSteps);
  const first=src.filter(b=>Number(b.slot)<5),rare=src.filter(b=>Number(b.slot)>=5),lines=[];
  const lbl=s=>String(s).replace(/[\d.,%+\-–:()\s]+/g,'').toLowerCase(),nativeColor=new Map((it.ClientDetails?.rendererCalls?.AppendTextLine||[]).slice(1).map(c=>[lbl(c[0]),lineColor(c[1])]));
  const bonusColor=(b,fallback)=>nativeColor.get(lbl(b.text))||fallback;
  const AQUA=lineColor(4286248660),isBase=x=>/^(Saldırı değeri|Büyülü saldırı değeri|Defans)\b/i.test(x);
  (e.effects||[]).filter(isBase).forEach(x=>lines.push(`<div style="color:${BASE_LINE}">${esc(x)}</div>`));
  (e.effects||[]).filter(x=>!isBase(x)).forEach(x=>lines.push(`<div style="color:${AQUA}">${esc(x)}</div>`));
  (e.baseAffects||[]).forEach(x=>lines.push(`<div style="color:${AQUA}">${esc(x)}</div>`));
  if(lines.length&&(el||src.length))lines.push('<div class="gap"></div>');
  if(el)lines.push(elementRowHTML({c:el.color,t:elementText(el),el}));
  first.forEach(b=>lines.push(`<div style="color:${bonusColor(b,BONUS_LINE)}">${esc(b.text)}</div>`));
  if(rare.length){lines.push('<div class="gap"></div>');rare.forEach(b=>lines.push(`<div style="color:${bonusColor(b,RARE_LINE)}">${esc(b.text)}</div>`))}
  if(!src.length&&!el&&!(e.effects||[]).length&&!(e.baseAffects||[]).length)lines.push('<div class="emb-none">Bu kayıtta ekipmanın özellikleri bulunmuyor.</div>');
  const explain=it.absorption!=null?`Bu ekipman kuşağa emdirilmiş. Efsunları kuşağa <b>%${it.absorption}</b> oranında yansır; aşağıda ekipmanın kendi (%100) değerleri görünüyor.${(e.baseAffects||[]).length?' Açık mavi temel özellikler, oyunun kuşakta gösterdiği (emiş oranlı) değerlerdir.':''}`:'Bu ekipman kuşağa emdirilmiş. Aşağıda ekipmanın kendi (%100) değerleri görünüyor.';
  return `<div class="gt-sec emb-sec"><h4>Emdirilen ekipman ${it.absorption!=null?`<span>Emiş oranı %${it.absorption}</span>`:''}</h4>
    <p class="emb-explain">${explain}</p>
    <div class="emb-item"><div class="gt-head"><div class="gt-img">${img(itemSrc(e.icon),e.name)}</div><div class="gt-name emb-name">${esc(e.name)}</div></div>
    <div class="gt-lines">${lines.join('')}</div></div>
  </div>`}
function featureBlock(it){const t=it.ClientDetails?.itemType;if(![1,2,28].includes(t)||!(it.SungmaScore||it.YinYangValue))return'';return `<div class="gt-sec"><h4>Ek özellikler</h4><div class="feat-row"><span><small>SungMa puanı</small><b>${nf.format(it.SungmaScore||0)}</b></span><span><small>Yin-yang</small><b>${nf.format(it.YinYangValue||0)}</b></span></div></div>`}
function specialChips(it){const s=it.special||{},p=s.pet,parts=[];
  if(p){if(p.filled){parts.push(`<b>${esc(p.species||'Pet')}${p.name&&p.name!==p.species?' · '+esc(p.name):''}</b>`);const a=[p.type!=null?'Tip '+p.type:'',p.level!=null?(p.champion?'F. Sv ':'Sv. ')+p.level:'',p.ageDays!=null?p.ageDays+' günlük':''].filter(Boolean).join(' · ');if(a)parts.push(esc(a));
      const v=Object.entries(p.values||{}).map(([k,v])=>`${esc(k)} <b>%${nf2.format(v)}</b>`).join(' · ');if(v)parts.push(v);if(p.lifeHours)parts.push('Pet yaşam süresi · '+nf.format(p.lifeHours)+' saat')}
    else parts.push(p.filled===false?'Boş nakliye kutusu':'Pet bilgisi bu kayıtta yok');if(p.boxRemaining)parts.push('Kutu süresi · '+esc(p.boxRemaining))}
  if(s.alchemy)parts.push(`<b>${esc(s.alchemy.className||'')}</b> · ${esc(s.alchemy.clarityName||'')}${s.alchemy.upgrade!=null?' · +'+s.alchemy.upgrade:''}`);
  if(s.consumable){if(s.consumable.effects?.length)parts.push(s.consumable.effects.map(esc).join(' · '));if(s.consumable.durationSeconds)parts.push('Kullanım süresi · '+nf.format(s.consumable.durationSeconds/60)+' dk')}
  if(it.energy!=null)parts.push('Enerji değeri <b>'+esc(it.energy)+'</b>');
  if(hasBox(it)&&(it.container.status==='available'||it.container.status==='variants'))parts.push(ic('box')+(it.container.variants?nf.format(it.container.variants)+' sürüm':nf.format(it.container.count)+' olası ödül'));
  const remaining=SocketDuration.itemRemaining(it,sockets(it));if(remaining&&!p)parts.push('Kalan süre · '+esc(remaining));
  return parts.length?`<div class="spec${p?' pet':''}">${parts.map(t=>`<span>${t}</span>`).join('')}</div>`:''}
function gtHTML(it,{foot=true,wide=false}={}){
  const g=gameLines(it),bs=new Map((it.displayBonuses||[]).map(b=>[b.text,bonusInfo(b)]));
  let prev=null,lines='';for(const l of g.lines){if(prev&&prev!==l.c)lines+='<div class="gap"></div>';const bi=bs.get(l.t.trim());if(l.el){lines+=elementRowHTML(l);prev=l.c;continue}lines+=`<div style="color:${l.c}"${l.acce?' class="acce" title="Emdirilen ekipmandan kuşağa geçen değer"':''}>${esc(l.t)}${bi?.tier?` <i class="tier${bi.max?' max':''}">${bi.tier}</i>`:''}</div>`;prev=l.c}
  const head=`<div class="gt-head"><div class="gt-img">${itemImg(it)}${it.Count>1?`<span class="qty-under" title="${nf.format(it.Count)} adet">×${nf.format(it.Count)}</span>`:''}</div><div class="gt-name" style="color:${g.name[1]}">${esc(g.name[0])}</div></div>${lines?`<div class="gt-lines">${lines}</div>`:''}`,
    secs=`${embeddedBlock(it)}${socketBlock(it)}${featureBlock(it)}`;
  return `<div class="gt${wide?' wide':''}${isFakeListing(it)?' fake-listing'+staleCls(it):''}"${fakeExpiry(it)}>${purchaseBadge(it)}${wide?`<div class="gt-cols"><div>${head}</div><div>${secs}`:head+secs}
    ${hasBox(it)?`<div class="gt-sec${it.container.status==='available'||it.container.status==='variants'?' box-hint':''}"${it.container.status==='available'||it.container.status==='variants'?` data-box-open="${esc(it.id)}" role="button" tabindex="0"`:''}><h4>Sandık / kutu</h4><div class="hint-line">${ic('box')} ${it.container.status==='available'||it.container.status==='variants'?(it.container.variants?nf.format(it.container.variants)+' sürüm':nf.format(it.container.count)+' olası ödül')+' · içerik ve çıkma şansı için tıkla':'İçerik listesi henüz doğrulanmadı'}</div></div>`:''}
    ${wide?'</div></div>':''}${foot?`<div class="gt-foot"><div class="gt-price">${priceHTML(it)}<small>${esc(it.Seller)} · ${esc(ago(it.observed))}</small></div><div class="hint">Tıkla: ayrıntı</div></div>`:''}</div>`;
}

function renderCats(){const counts={0:state.meta?.overall?.count};for(const r of state.meta?.categories||[])counts[r.category]=(counts[r.category]||0)+r.count;
  $('cats').innerHTML=CATS.map(([l,ic],c)=>`<button class="cat${c===state.category&&!state.favView?' active':''}" data-cat="${c}" title="${esc(catName(c))}${counts[c]!=null?' · '+nf.format(counts[c])+' ilan':''}">${img('/assets/items/'+ic+'.png')}<span>${esc(l)}</span></button>`).join('');
  const cats=$('cats'),on=cats.querySelector('.cat.active');if(on&&cats.scrollWidth>cats.clientWidth)cats.scrollLeft+=on.getBoundingClientRect().left-cats.getBoundingClientRect().left-(cats.clientWidth-on.offsetWidth)/2}
function renderSubs(){if(!state.category||state.favView){$('subs').innerHTML='';return}
  const names=subNames(state.category),counts={};for(const r of state.meta.categories||[])if(r.category===state.category)counts[r.sub]=r.count;const total=Object.values(counts).reduce((a,b)=>a+b,0);
  $('subs').innerHTML=`<button class="sub${state.sub===''?' active':''}" data-sub="">Tümü<small>${nf.format(total)}</small></button>`+Object.entries(names).filter(([k])=>counts[k]).map(([k,v])=>`<button class="sub${String(state.sub)===k?' active':''}" data-sub="${k}">${esc(v)}<small>${nf.format(counts[k])}</small></button>`).join('');window.M2Hooks?.subs?.({category:state.category,server:SERVER,iconOf,catIcon:c=>'/assets/items/'+(CATS[c]?.[1]||'54705')+'.png'})}

const sel=(key,opts,{all='Tümü',label,w}={})=>`<div class="sp${w?' w':''}"><span class="lbl">${esc(label)}</span><label class="field sel" style="height:38px"><select data-f="${key}" aria-label="${esc(label)}"><option value="">${esc(all)}</option>${opts.map(([v,t])=>`<option value="${esc(v)}"${String(state.filters[key])===String(v)?' selected':''}>${esc(t)}</option>`).join('')}</select></label></div>`;
const range=(a,b,label,{min=0,max='',pa='En az',pb='En fazla'}={})=>`<div class="sp"><span class="lbl">${esc(label)}</span><div class="num-range"><input data-f="${a}" type="number" min="${min}" ${max!==''?`max="${max}"`:''} placeholder="${pa}" value="${esc(state.filters[a])}">${b?`<i>–</i><input data-f="${b}" type="number" min="${min}" ${max!==''?`max="${max}"`:''} placeholder="${pb}" value="${esc(state.filters[b])}">`:''}</div></div>`;
function counts(from,to,label){const chosen=String(state.filters.bonusCount).split(',').filter(Boolean);return `<div class="sp"><span class="lbl">${esc(label)}</span><div class="counts" role="group">${Array.from({length:to-from+1},(_,i)=>i+from).map(n=>`<button type="button" data-count="${n}" class="${chosen.includes(String(n))?'on':''}">${n}</button>`).join('')}</div></div>`}
const elPick=()=>`<div class="sp"><span class="lbl">Element</span><div class="el-pick">${Object.entries(ELEMENTS).map(([id,[n,c,i]])=>`<button type="button" data-el="${id}" style="--el:${c}" class="${state.filters.element===id?'on':''}" title="${n}" aria-pressed="${state.filters.element===id}"><img src="/assets/${i}.png" alt="${n}"></button>`).join('')}</div></div>`+sel('grade',[['1','En az +1'],['2','En az +2'],['3','+3']],{label:'Element seviyesi'});
function alchemyRange(cls){const f=[1,1,1,2,2,3],m=[1,2,3,4,5,6];return cls===''?[2,6]:[f[cls]??1,m[cls]??6]}
function renderSpecial(){
  const c=state.category,s=String(state.sub),q=state.q.toLocaleLowerCase('tr'),meta=state.meta,parts=[];if(state.favView){$('special').innerHTML='';return}
  const fams=(meta.categoryCatalog?.families||[]).filter(f=>f.category===c&&(s===''||f.sub===Number(s))),fish=c===7&&s==='2';
  const famLabel=c===7&&s==='3'?'Cevher':c===5&&s==='1'?'Taş / cevher':c===5&&s==='0'?'Malzeme':c===1&&s==='0'?'Binek':c===12&&s==='0'?'Sandık / kutu':'Ürün seç';
  if(fish){if(state.filters.fishGrilled==='')state.filters.fishGrilled='1';const grilled=state.filters.fishGrilled!=='0';
    parts.push(sel('fishSpecies',(meta.categoryCatalog?.fish||[]).map(f=>[f.id,`${f.label} · ${nf.format(grilled?f.grilledCount:f.normalCount)}`]),{all:'Tüm balıklar',label:'Balık türü',w:1}),`<div class="sp"><span class="lbl">Hazırlanış</span><label class="switch"><input type="checkbox" data-grilled ${grilled?'checked':''}>Izgara</label></div>`)}
  else if(c===12&&s==='5')parts.push(sel('orb',(meta.specialOptions?.orbs||[]).map(o=>[o.label,`${o.label.replace(/\s*Dönüşüm Küresi$/i,'')} · ${nf.format(o.count)}`]),{all:'Tüm küreler',label:'Dönüşen yaratık',w:1}));
  else if(fams.length>=2||state.filters.catalogFamily)parts.push(sel('catalogFamily',fams.map(f=>[f.id,`${f.label} · ${nf.format(f.count)}`]),{all:'Tüm ürünler',label:famLabel,w:1}));
  if(c===1){const chosen=fams.find(f=>f.id===state.filters.catalogFamily);if(!chosen||chosen.group==='Nakliye kutusu')parts.push(sel('petState',[['1','Dolu'],['0','Boş']],{label:'Kutu içeriği'}),sel('petSpecies',(meta.specialOptions?.petSpecies||[]).map(v=>[v,v]),{label:'Pet türü'}),sel('petType',(meta.specialOptions?.petTypes||[]).map(v=>[v,'Tip '+v]),{label:'Pet tipi'}),range('petLevelMin','petLevelMax','Pet seviyesi',{max:120,pa:'Min',pb:'Max'}))}
  const costume=c===9&&['','0','1','2'].includes(s),armor=c===3,weapon=c===2&&s!=='8';
  const gendered=c===9&&['1','2'].includes(s);
  if(gendered)parts.push(sel('gender',[['e','Erkek'],['k','Kadın']],{all:'Erkek & Kadın',label:'Cinsiyet'}));
  else if(armor&&['','0','1'].includes(s)||costume)parts.push(sel('character',JOBS,{all:'Tüm karakterler',label:'Karakter'}));
  if(c===9&&s==='')parts.push(sel('gender',[['e','Erkek'],['k','Kadın']],{all:'Erkek & Kadın',label:'Cinsiyet'}));
  if(armor||costume){const job=gendered?'':state.filters.character,types=(meta.equipmentTypes||[]).filter(o=>o.category===c&&(job===''||o.jobs.includes(Number(job)))&&(s===''||o.sub===Number(s)));if(types.length>1)parts.push(sel('equipmentType',types.map(o=>[o.id,o.label]),{all:'Tüm türler',label:'Tür'}))}
  if(weapon||armor||costume||c===4&&s!=='4')parts.push(counts(1,costume?3:7,'Efsun adedi'));
  if(c===9&&!['3','4','5'].includes(s))parts.push(range('remainMin','remainMax','Kalan süre (gün)',{pa:'En az',pb:'En çok'}));
  if(weapon||c===9&&s==='3')parts.push(elPick());
  if(c===9&&s==='3')parts.push(range('absorbMin','absorbMax','Emiş oranı %',{max:25,pa:'0',pb:'25'}));
  if(c===6)parts.push(sel('consumableEffect',(meta.consumableEffects||[]).map(e=>[e.id,e.label]),{all:'Tüm etkiler',label:'İksir etkisi',w:1}),range('consumableMin','','En az etki değeri'),range('durationMin','','Kullanım süresi (dk)'));
  if((c===6||c===0||c===11)&&q.includes('enerji kristali')||state.filters.energyMin||state.filters.energyMax)parts.push(range('energyMin','energyMax','Enerji değeri',{min:1,max:10,pa:'1',pb:'10'}));
  if(c===8){parts.push(sel('alchemyClass',[['3','Antika'],['4','Efsanevi'],['5','Mitsi']],{label:'Sınıf'}),sel('alchemyClarity',[['0','Mat'],['1','Parlak'],['2','Tertemiz'],['3','Mükemmel'],['4','Kusursuz']],{label:'Saflık'}),range('alchemyUpgradeMin','','Simya yükseltme +',{max:6}));const [a,b]=alchemyRange(state.filters.alchemyClass===''?'':Number(state.filters.alchemyClass));parts.push(counts(a,b,'Bonus adedi'))}
  const titles={1:['paw','Binek / Pet'],2:['sword','Silah'],3:['armor','Ekipman'],4:['ring','Mücevher'],5:['layers','Geliştirme'],6:['flask','İksir'],7:['pick','Balıkçılık / Madencilik'],8:['gem','Ejderha Taşı Simyası'],9:['shield','Kostüm'],11:['box','Özel eşya'],12:['box','Diğer']};
  const [ico,tit]=titles[c]||['sliders','Filtreler'];
  $('special').innerHTML=parts.length?`<div class="sp-head">${ic(ico)}<strong>${esc(tit)}</strong><span>Bu kategoriye özel filtreler</span>${SPECIAL_KEYS.some(k=>state.filters[k]!==''&&k!=='fishGrilled')?'<button class="btn sm ghost" type="button" data-reset-special>'+ic('reset')+'Sıfırla</button>':''}</div>`+parts.join(''):'';
}
const clearSpecial=()=>{for(const k of SPECIAL_KEYS)state.filters[k]=''};

function filterLabel(k,v){const m=state.meta;if(k==='element')return 'Element: '+(ELEMENTS[v]?.[0]||v);if(k==='orb')return 'Küre: '+v.replace(/\s*Dönüşüm Küresi$/i,'');if(k==='gender')return 'Cinsiyet: '+(v==='e'?'Erkek':'Kadın');if(k==='character')return 'Karakter: '+(JOBS.find(j=>j[0]===v)?.[1]||v);
  if(k==='bonusCount')return 'Efsun adedi: '+v.split(',').join(', ');if(k==='catalogFamily')return (m.categoryCatalog?.families||[]).find(f=>f.id===v)?.label||'Ürün';
  if(k==='fishSpecies')return (m.categoryCatalog?.fish||[]).find(f=>f.id===v)?.label||'Balık';if(k==='equipmentType')return (m.equipmentTypes||[]).find(o=>o.id===v)?.label||'Tür';
  if(k==='consumableEffect')return (m.consumableEffects||[]).find(e=>e.id===v)?.label||'Etki';if(k==='petState')return v==='1'?'Kutu: dolu':'Kutu: boş';if(k==='petType')return 'Pet tipi '+v;if(k==='petSpecies')return v;
  if(k==='alchemyClass')return {3:'Antika',4:'Efsanevi',5:'Mitsi'}[v]||v;if(k==='alchemyClarity')return ['Mat','Parlak','Tertemiz','Mükemmel','Kusursuz'][v]||v;return (LABELS[k]||k)+(k==='grade'?'':' ')+v}
const MODE={gte:'≥',eq:'=',lte:'≤'},GROUP={any:'',first:' · 1–5',rare:' · 6–7'};
function renderApplied(){const c=[];
  if(state.category&&!state.favView){const sn=state.sub!==''?subNames(state.category)[state.sub]:'';c.push(`<span class="chip cat-chip"><small>Kategori</small>${esc(catName(state.category))}${sn?' · '+esc(sn):''}<button data-rm="cat" aria-label="Kaldır">${ic('close')}</button></span>`)}
  if(state.q)c.push(`<span class="chip"><small>Eşya</small>${esc(state.q)}<button data-rm="q" aria-label="Kaldır">${ic('close')}</button></span>`);
  if(state.seller)c.push(`<span class="chip"><small>Satıcı</small>${esc(state.seller)}<button data-rm="seller" aria-label="Kaldır">${ic('close')}</button></span>`);
  for(const k of FILTER_KEYS){const v=state.filters[k];if(v===''||k==='fishGrilled')continue;c.push(`<span class="chip">${esc(filterLabel(k,v))}<button data-rm="f:${k}" aria-label="Kaldır">${ic('close')}</button></span>`)}
  state.bonus.forEach((b,i)=>{let col='#6c7380';try{col=bonusAppearance({type:b.type,value:b.value,slot:b.group==='rare'?5:0}).color}catch{}c.push(`<span class="chip bonus${state.editBonus===i?' editing':''}" style="--c:${col}"><button class="chip-edit" data-edit-bonus="${i}" title="Düzenle">${esc(statLabel(b.type))} ${MODE[b.mode]||'≥'} ${esc(b.value)}<small>${GROUP[b.group]||''}</small></button><button data-rm="b:${i}" aria-label="Kaldır">${ic('close')}</button></span>`)});
  $('applied').innerHTML=c.length?`<div class="applied-head"><span class="applied-label">${ic('sliders')}<b>${c.length}</b> aktif filtre</span><button class="btn sm reset-all" type="button" data-reset-all>${ic('reset')}Tümünü temizle</button></div><div class="applied-chips">${c.join('')}</div>`:'';$('applied').classList.toggle('many',c.length>3);
  const advN=ADV_KEYS.filter(k=>state.filters[k]!=='').length;$('adv-count').textContent=advN;$('adv-count-all').textContent=advN+state.bonus.length+(state.seller?1:0);for(const k of ADV_KEYS)$('f-'+k).value=state.filters[k];window.M2Hooks?.applied?.({ELEMENTS,state})}

function params(){const q={category:state.category,page:state.page,sort:state.sort};if(state.sub!=='')q.sub=state.sub;
  if(state.q){q.q=state.q;if(state.qFamily)q.itemFamily='1'}if(state.seller){q.seller=state.seller;if(state.sellerExact)q.sellerExact='1'}
  for(const k of FILTER_KEYS)if(state.filters[k]!=='')q[k]=state.filters[k];if(state.bonus.length)q.bonusFilters=JSON.stringify(state.bonus);
  if(state.favView){q.favorites=[...state.saved].join(',')||'none';q.category=0;delete q.sub}return q}
function shareUrl(){const p=params();if(SERVER!=='safir')p.server=SERVER;return location.origin+location.pathname+'?'+new URLSearchParams(p)}
function syncUrl(){if(state.favView)return;const u=new URL(shareUrl());const open=$('detail').classList.contains('open')&&$('detail').dataset.item;if(open)u.searchParams.set('item',open);history.replaceState(null,'',u)}
const savedGone=new Set();
function updateSavedBadge(){const n=Math.max(0,state.saved.size-savedGone.size);$('saved-count').textContent=n;$('saved-count').classList.toggle('on',n>0);$('saved-count').title=savedGone.size?`${savedGone.size} kayıt artık listede yok`:''}
async function checkSaved(){
  const ids=[...state.saved];savedGone.clear();if(!ids.length){updateSavedBadge();return}
  try{
    const present=new Set();let page=1,pages=1;
    do{const r=await api('items',{favorites:ids.join(','),category:'0',page:String(page),sort:state.sort});r.items.forEach(x=>present.add(x.id));pages=r.pages||1;page++}while(page<=pages&&page<=40);
    if(pages>40){updateSavedBadge();return}
    ids.filter(id=>!present.has(id)).forEach(id=>savedGone.add(id));
  }catch{savedGone.clear()}
  updateSavedBadge();
}
function goneNote(){return savedGone.size?`<div class="empty gone-note">Kaydettiğin ${savedGone.size} ilan bu sunucuda artık listelenmiyor (satılmış veya kaldırılmış olabilir). <button class="btn sm" type="button" data-clear-gone>Listeden çıkar</button></div>`:''}
let token=0;
async function load(resetPage=true){
  if(resetPage)state.page=1;const t=++token;$('list').innerHTML=Array(5).fill('<div class="skeleton"></div>').join('');
  $('nav-saved').classList.toggle('active',state.favView);$('nav-market').classList.toggle('active',!state.favView);
  renderCats();renderSubs();renderSpecial();renderApplied();$('context').hidden=!$('subs').innerHTML&&!$('special').innerHTML;
  const sub=state.sub!==''?subNames(state.category)[state.sub]:'';
  $('r-title').textContent=state.favView?'Kaydedilenler':state.seller&&!state.q?`${state.seller} ilanları`:state.q?`“${state.q}”`:sub||(state.category?catName(state.category):'Tüm eşyalar');
  syncUrl();window.refreshSelects?.();
  try{const r=await api('items',params());if(t!==token)return;pageItems=r.items;r.items.forEach(x=>items.set(x.id,x));state.total=r.total;$('r-count').textContent=nf.format(r.total);
    if(state.favView)await checkSaved();
    if(!r.items.length){$('pager').innerHTML='';$('list').innerHTML=(state.favView?goneNote():'')+`<div class="empty">${state.favView?(state.saved.size&&savedGone.size===state.saved.size?'Kaydettiğin ilanların hiçbiri şu an listede değil.':'Henüz kaydettiğin ilan yok. Satırlardaki yer imi düğmesiyle kaydedebilirsin.'):emptyWhy()}<div class="relief" id="relief"></div></div>`;if(!state.favView)loadRelief(t);return}
    $('list').innerHTML=(state.favView?goneNote():'')+r.items.map((x,i)=>rowHTML(x,i)+(adAt(i+1)&&r.items.length>i+1?'<div class="ad-slot inline"><span>REKLAM</span><b>728 × 90</b></div>':'')).join('');renderPager(r.page,r.pages);
  }catch(e){if(t===token)$('list').innerHTML=`<div class="empty">Liste şu an yüklenemiyor. Birkaç saniye sonra sayfayı yenileyin.</div>`}
}
function emptyWhy(){const parts=[];if(state.q)parts.push(`“${esc(state.q)}”`);if(state.seller)parts.push(`satıcı “${esc(state.seller)}”`);
  const where=state.category?`<b>${esc(catName(state.category))}${state.sub!==''&&subNames(state.category)[state.sub]?' · '+esc(subNames(state.category)[state.sub]):''}</b> kategorisinde`:'bu sunucuda';
  const extra=state.bonus.length||FILTER_KEYS.some(k=>state.filters[k]!==''&&k!=='fishGrilled');
  return `<strong>${parts.length?parts.join(' + ')+' için ':''}${where.replace(/^bu/,'Bu')} ilan bulunamadı.</strong><span class="empty-sub">${state.category&&state.q?'Aradığın eşya başka bir kategoride olabilir; seçtiğin kategori korunuyor. ':''}${extra?'Uyguladığın filtreler de sonucu daraltıyor olabilir.':'Yazımı kontrol edebilir ya da aşağıdaki önerileri deneyebilirsin.'}</span>`}
function reliefLabel(s){
  if(s.bonusIndex!=null){const b=state.bonus[s.bonusIndex];if(b)return `<b class="rl-what">${esc(statLabel(b.type))} ${MODE[b.mode]||'≥'} ${esc(b.value)}${esc(GROUP[b.group]||'')}</b> efsununu`}
  if(s.id==='all-conditions')return 'Tüm ek filtreleri';
  const detail=(s.keys||[]).filter(k=>state.filters[k]!==''&&state.filters[k]!==undefined&&k!=='fishGrilled').map(k=>filterLabel(k,state.filters[k])).join(', ');
  return detail?`${esc(s.label)}: <b class="rl-what">${esc(detail)}</b>`:esc(s.label)
}
async function loadRelief(t){try{const r=await api('filter-relief',params());if(t!==token)return;const host=$('relief');if(!host)return;
  host.innerHTML=(r.suggestions||[]).length?`<p>${state.category&&state.q?esc(catName(state.category))+' kategorisinde kalarak:':'Tek tıkla sonuç bulmak için:'}</p>${r.suggestions.map(s=>`<button class="btn sm" type="button" data-relief="${esc((s.keys||[]).join(','))}"${s.bonusIndex!=null?` data-relief-bonus="${s.bonusIndex}"`:''}>${ic('reset')}<span class="rl-text">${(s.keys||[]).includes('q')?`“${esc(state.q)}” aramasını kaldır`:(s.keys||[]).includes('category')?esc(catName(state.category))+' kategorisini kaldır':reliefLabel(s)+' kaldır'}</span> <b>${nf.format(s.total)} ilan${(s.keys||[]).includes('q')&&state.category?' · '+esc(catName(state.category)):''}</b></button>`).join('')}`:'<p>Filtreleri gevşetmeyi dene.</p>'}catch{}}
function isFakeListing(it){return !!it?.purchaseStatus?.isFake}
const isStaleFake=it=>isFakeListing(it)&&(it.purchaseStatus.state==='fake-stale'||Date.parse(it.purchaseStatus.staleAt)<=Date.now());
const fakeExpiry=it=>isFakeListing(it)?` data-fake-stale-at="${esc(it.purchaseStatus.staleAt)}"`:'';
const staleCls=it=>isStaleFake(it)?' fake-stale':'';
const checkAgo=it=>{const m=Math.max(0,Math.round(((Date.now()-Date.parse(it.purchaseStatus.checkedAt))||0)/60000));return m<60?`${m} dk`:`${Math.floor(m/60)} sa ${m%60} dk`};
const fakeTitle=it=>`${isStaleFake(it)?'FAKE? · doğrulama eski':'FAKE · pazar hazır değil'}|Son kontrol ${checkAgo(it)} önce (${dateFmt(it.purchaseStatus.checkedAt)}). Oyun, bu pazarın şu an alışverişe hazır olmadığını bildirdi; bu bir hile iddiası değildir.${isStaleFake(it)?' Kontrol sonucu eskidi, pazar şu an hazır olabilir.':''}`;
const OK_ICON='<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9.6" fill="#1b1d1b"/><circle cx="12" cy="12" r="9.6" fill="none" stroke="#38b232" stroke-width="2"/><path d="M7.5 12.6l3.1 3.1 5.9-6.6" fill="none" stroke="#4fd048" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const isVerifiedClean=it=>it.purchaseStatus?.state==='clear'&&!it.purchaseStatus?.isFake;
const verifiedBadge=(it,full=false)=>isVerifiedClean(it)?`<span class="ok-badge${full?' full':''}" data-tip="${esc('Pazar doğrulandı|Son kontrol '+checkAgo(it)+' önce: bu pazar alışverişe hazırdı, FAKE değil.')}" tabindex="0">${OK_ICON}${full?`<b>Doğrulandı</b><small>son kontrol ${esc(checkAgo(it))} önce</small>`:''}</span>`:'';
const purchaseBadge=it=>isFakeListing(it)?`<span class="fake-badge${staleCls(it)}"${fakeExpiry(it)} data-tip="${esc(fakeTitle(it))}" tabindex="0">${isStaleFake(it)?'FAKE?':'FAKE'}<small class="fake-age">son kontrol ${esc(checkAgo(it))} önce</small></span>`:'';
setInterval(()=>{for(const el of document.querySelectorAll('[data-fake-stale-at]')){
  if(Date.parse(el.dataset.fakeStaleAt)>Date.now()||el.classList.contains('fake-stale'))continue;
  el.classList.add('fake-stale');const b=el.matches('.fake-badge')?el:el.querySelector('.fake-badge');if(b?.firstChild)b.firstChild.textContent='FAKE?';
}},15000);
function rowHTML(it,i){
  const bs=(it.displayBonuses||[]).map(bonusInfo),el=elementOf(it),lvl=itemLevel(it),plus=plusOf(it),name=title(it).replace(/\+\d+\s*(?=\(|$)/,'').trim();
  const meta=[`<span>${esc(catName(it.category))}</span>`];if(lvl)meta.push(`<span>Sv. ${lvl}</span>`);
  if(el)meta.push(`<span class="el-mini" style="--el:${el.color}">${elImg(el)}${esc(elementText(el))}</span>`);
  if(it.absorption!=null)meta.push(`<span>Emiş %${it.absorption}</span>`);if(it.SungmaScore)meta.push(`<span>SungMa ${nf.format(it.SungmaScore)}</span>`);
  const have=new Set((it.displayBonuses||[]).map(b=>String(b.text).trim())),baseChips=((it.ClientDetails?.rendererCalls?.AppendTextLine||[]).slice(1).filter(c=>(Number(c[1])>>>0)===0xFF7AF6D4&&c[0]&&!have.has(String(c[0]).trim()))).map(c=>`<span class="bn base" style="--c:#7af6d4;--s:1" title="Temel özellik">${esc(String(c[0]).trim())}</span>`).join('');
  const chips=baseChips+bs.map(b=>`<span class="bn${b.max?' max':''}${b.rare?' rare':''}${b.neg?' neg':''}" style="--c:${b.color};--s:${b.s??.35}"${b.tier?` title="${b.tier}"`:''}>${esc(b.text)}</span>`).join('');
  const emb=it.embedded?`<span class="mini-emb">${img(itemSrc(it.embedded.icon),it.embedded.name)}<span>Emdirilen <b>${esc(it.embedded.name)}</b>${(it.ClientDetails?.bonuses||[]).length?` · ${it.ClientDetails.bonuses.length} efsun`:''}</span></span>`:'';
  return `<article class="item${state.cmp.some(c=>c.id===it.id)?' cmp':''}${isFakeListing(it)?' fake-listing'+staleCls(it):''}"${fakeExpiry(it)} data-id="${esc(it.id)}" style="--i:${i}">
    <div class="slot${it.Count>1?' has-qty':''}">${itemImg(it)}${plus?`<span class="plus">+${plus}</span>`:''}${it.Count>1?`<span class="qty-under" title="${nf.format(it.Count)} adet">×${nf.format(it.Count)}</span>`:''}</div>
    <div class="info"><div class="name-line"><span class="name">${esc(name)}</span>${purchaseBadge(it)}${verifiedBadge(it)}${isNew(it)?'<span class="tag new">Yeni</span>':''}</div>
      <div class="meta-line">${meta.join('<i class="sep"></i>')}</div>${specialChips(it)}${chips?`<div class="bonuses">${chips}</div>`:''}${stoneIcons(sockets(it))}${emb}</div>
    <div class="cell-seller"><button type="button" class="seller-btn" data-shop="${esc(it.Seller)}" title="${esc(it.Seller)} pazarını aç">${ic('shop')}<span>${esc(it.Seller)}</span></button><small>${esc(ago(it.observed))}</small></div>
    <div class="cell-qty"><span class="qty-box">×${nf.format(it.Count)}</span><small>adet</small></div>
    <div class="cell-price">${priceHTML(it)}</div>
    <div class="cell-act">${actBtns(it)}</div></article>`}
const actBtns=it=>`<button class="ibtn" data-hist="${esc(it.id)}" title="Fiyat ve eşya geçmişi" aria-label="Geçmiş">${ic('history')}</button><button class="ibtn${state.cmp.some(c=>c.id===it.id)?' on':''}" data-cmp="${esc(it.id)}" title="Karşılaştırmaya ekle" aria-label="Karşılaştır">${ic('compare')}</button><button class="ibtn${state.saved.has(it.id)?' on':''}" data-save="${esc(it.id)}" title="Kaydet" aria-label="Kaydet">${ic('bookmark')}</button>`;
function renderPager(p,pages){const nums=[...new Set([1,p-2,p-1,p,p+1,p+2,pages])].filter(n=>n>=1&&n<=pages).sort((a,b)=>a-b);let last=0,h=`<button data-page="${p-1}" ${p<=1?'disabled':''} aria-label="Önceki">‹</button>`;
  for(const n of nums){if(n-last>1)h+='<button disabled>…</button>';h+=`<button data-page="${n}" class="${n===p?'on':''}">${n}</button>`;last=n}$('pager').innerHTML=h+`<button data-page="${p+1}" ${p>=pages?'disabled':''} aria-label="Sonraki">›</button>`}

const hc=$('hovercard');let hcId=null;
function placeCard(x,y){const w=hc.offsetWidth,h=hc.offsetHeight,pad=14;let l=x+20,t=y-30;if(l+w>innerWidth-pad)l=x-w-20;if(t+h>innerHeight-pad)t=innerHeight-h-pad;hc.style.left=Math.max(pad,l)+'px';hc.style.top=Math.max(pad,t)+'px'}
function hideCard(){hc.hidden=true;hcId=null}
document.addEventListener('mousemove',e=>{if(e.sourceCapabilities?.firesTouchEvents)return;
  {const dlg=e.target.closest?.('dialog[open]')||document.body;if(hc.parentElement!==dlg)dlg.appendChild(hc)}
  const rw=e.target.closest('.reward');if(rw?._r&&!e.target.closest('[data-tip]')){const id='rw:'+rw._k;if(id!==hcId){hcId=id;hc.innerHTML=rewardCard(rw._r);hc.hidden=false}placeCard(e.clientX,e.clientY);return}
  const el=e.target.closest('.item,.inv-item,[data-hover]');
  if(!el||e.target.closest('button:not(.inv-item):not([data-hover]),.ad-slot,[data-tip]')){if(hcId)hideCard();return}const id=el.dataset.hover||el.dataset.id;
  if(id!==hcId){const it=items.get(id);if(!it)return;hcId=id;hc.innerHTML=gtHTML(it,{wide:!!it.embedded&&innerWidth>720});hc.hidden=false;if(hc.offsetHeight>innerHeight-24&&innerWidth>720){hc.innerHTML=gtHTML(it,{wide:true});if(hc.offsetHeight>innerHeight-24)hc.firstElementChild.classList.add('dense')}}placeCard(e.clientX,e.clientY)});
document.addEventListener('scroll',hideCard,{passive:true});
const tipEl=document.createElement('div');tipEl.className='m2-tip';tipEl.hidden=true;document.body.appendChild(tipEl);
const tipParts=el=>{const [h,...r]=(el.dataset.tip||'').split('|');return [h,r.join('|')]};
function showTip(el){{const dlg=el.closest('dialog[open]')||document.body;if(tipEl.parentElement!==dlg)dlg.appendChild(tipEl)}const [h,b]=tipParts(el);tipEl.innerHTML=`<b>${esc(h)}</b>${b?`<span>${esc(b)}</span>`:''}<small>Büyütmek için tıkla</small>`;tipEl.hidden=false;
  const r=el.getBoundingClientRect(),w=tipEl.offsetWidth,hh=tipEl.offsetHeight;let l=r.left+r.width/2-w/2;l=Math.max(10,Math.min(l,innerWidth-w-10));let tp=r.bottom+8;if(tp+hh>innerHeight-10)tp=r.top-hh-8;tipEl.style.left=l+'px';tipEl.style.top=Math.max(10,tp)+'px'}
document.addEventListener('mouseover',e=>{if(e.sourceCapabilities?.firesTouchEvents)return;const el=e.target.closest('[data-tip]');if(el){hideCard();showTip(el)}else tipEl.hidden=true});
document.addEventListener('focusin',e=>{const el=e.target.closest?.('[data-tip]');if(el)showTip(el)});document.addEventListener('focusout',()=>{tipEl.hidden=true});
document.addEventListener('scroll',()=>{tipEl.hidden=true},{passive:true,capture:true});
function closeTipBig(){document.querySelector('.m2-tipbig')?.remove()}
document.addEventListener('click',e=>{const big=e.target.closest('.m2-tipbig');if(big){if(e.target===big||e.target.closest('[data-tipclose]'))closeTipBig();return}
  const el=e.target.closest('[data-tip]');if(!el)return;e.preventDefault();e.stopPropagation();tipEl.hidden=true;hideCard();closeTipBig();
  const [h,b]=tipParts(el),ok=el.classList.contains('ok-badge'),stale=!!el.closest('.fake-stale')||el.classList.contains('fake-stale');
  const d=document.createElement('div');d.className='m2-tipbig';d.innerHTML=`<div class="tb-box ${ok?'ok':stale?'warn':'bad'}" role="dialog" aria-modal="true"><div class="tb-ic">${ok?OK_ICON:'<span>!</span>'}</div><h3>${esc(h)}</h3><p>${esc(b)}</p><button class="btn sm" type="button" data-tipclose>Tamam</button></div>`;([...document.querySelectorAll('dialog[open]')].pop()||document.body).appendChild(d);d.querySelector('button').focus()},true);
document.addEventListener('keydown',e=>{if(e.key==='Escape')closeTipBig()});

function petPanel(p){return `<div class="d-sec"><h4>Pet bilgisi</h4><div class="pet-facts">${[['Tür',p.species],['Ad',p.name&&p.name!==p.species?p.name:null],['Tip',p.type],[p.champion?'Şampiyon seviyesi':'Seviye',p.level],['Yaş',p.ageDays!=null?p.ageDays+' gün':null],['Gelişim',p.stage],['Yaşam süresi',p.lifeHours?nf.format(p.lifeHours)+' saat':null],['Kutu süresi',p.boxRemaining]].filter(([,v])=>v!=null&&v!=='').map(([k,v])=>`<span><small>${k}</small><b>${esc(String(v))}</b></span>`).join('')}</div>
  ${Object.keys(p.values||{}).length?`<div class="pet-stats">${Object.entries(p.values).map(([k,v])=>`<span><small>${esc(k)}</small><b>%${nf2.format(v)}</b></span>`).join('')}</div>`:''}${(p.extras||[]).length?`<div class="pet-extra">${p.extras.map(l=>`<span>${esc(l)}</span>`).join('')}</div>`:''}</div>`}
function openDetail(it){hideCard();const p=it.special?.pet;
  $('detail-body').innerHTML=`<div class="dt-grid${it.embedded&&innerWidth>760?' dt-wide':''}"><div class="dt-left">${gtHTML(it,{foot:false,wide:!!it.embedded&&innerWidth>760})}</div><div class="dt-right">`+(p?.filled?petPanel(p):'')+`<div class="d-meta"><div><small>Fiyat</small><div class="d-price">${priceHTML(it)}</div></div><div><small>Satıcı · ${esc(ago(it.observed))}</small>${purchaseBadge(it)}${verifiedBadge(it,true)}<button class="seller-btn" data-shop="${esc(it.Seller)}">${ic('shop')}<span>${esc(it.Seller)}</span></button></div></div>
    <div class="d-actions"><button class="btn gold wide" data-shop="${esc(it.Seller)}">${ic('shop')}Satıcının pazarını aç</button><button class="btn" data-hist="${esc(it.id)}">${ic('history')}Geçmiş</button><button class="btn${state.cmp.some(c=>c.id===it.id)?' on':''}" data-cmp="${esc(it.id)}">${ic('compare')}Karşılaştır</button><button class="btn${state.saved.has(it.id)?' on':''}" data-save="${esc(it.id)}">${ic('bookmark')}<span>${state.saved.has(it.id)?'Kaydedildi':'Kaydet'}</span></button><button class="btn" data-share-item="${esc(it.id)}">${ic('external')}Eşyayı paylaş</button></div>
    ${hasBox(it)?`<button class="d-sec box-open" type="button" data-box-open="${esc(it.id)}">${ic('box')}<span>İçinden çıkabilecekler <b>${it.container.variants?nf.format(it.container.variants)+' sürüm':nf.format(it.container.count||0)+' ödül'}</b></span>${ic('chevron')}</button>`:''}
    <details class="d-sec fold" id="sim-panel"><summary>${ic('search')}Daha uygun alternatifler${ic('chevron')}</summary><div class="sim-body"><div class="skeleton" style="height:120px"></div></div></details></div></div>`;
  $('detail').classList.add('open');$('detail').setAttribute('aria-hidden','false');$('scrim').hidden=false;$('detail').scrollTop=0;$('detail-title').textContent=title(it);$('detail').dataset.item=it.id;{const u=new URL(location.href);u.searchParams.set('item',it.id);history.replaceState(null,'',u)}
  const sim=$('sim-panel');sim.addEventListener('toggle',()=>{if(sim.open&&!sim.dataset.l){sim.dataset.l=1;loadSimilar(it,sim.querySelector('.sim-body'))}});
}
function closeDetail(){if($('detail').dataset.item){delete $('detail').dataset.item;const u=new URL(location.href);u.searchParams.delete('item');history.replaceState(null,'',u)}$('detail').classList.remove('open');$('detail').setAttribute('aria-hidden','true');$('scrim').hidden=true}

function closeBoxPop(){document.querySelector('.box-pop')?.remove();document.body.classList.remove('box-lock')}
function openBoxPop(it){closeBoxPop();hideCard();const d=document.createElement('div');d.className='box-pop';
  d.innerHTML=`<div class="bp-box" role="dialog" aria-modal="true" aria-label="Sandık içeriği"><div class="bp-head"><div class="bp-img">${itemImg(it)}</div><div class="bp-title"><small>Sandık içeriği</small><h3>${esc(title(it))}</h3></div><button class="ibtn bp-x" type="button" aria-label="Kapat" data-bp-close>${ic('close')}</button></div><div class="bp-body"><div class="skeleton" style="height:160px"></div></div></div>`;
  (document.querySelector('dialog.modal[open]')||document.body).appendChild(d);document.body.classList.add('box-lock');d.querySelector('.bp-x').focus();loadContainer(it,d.querySelector('.bp-body'))}
document.addEventListener('click',e=>{const b=e.target.closest('[data-box-open]');if(b){const it=items.get(b.dataset.boxOpen);if(it)openBoxPop(it);return}
  const pop=e.target.closest('.box-pop');if(pop&&(e.target===pop||e.target.closest('[data-bp-close]')))closeBoxPop()});
document.addEventListener('keydown',e=>{if(e.key==='Escape'&&document.querySelector('.box-pop')){e.stopPropagation();e.preventDefault();closeBoxPop()}else if(e.key==='Enter'&&e.target.matches?.('[data-box-open][role=button]')){e.preventDefault();e.target.click()}},true);
const boxCache=new Map();
async function loadContainer(it,host){try{let d=boxCache.get(it.Vnum);if(!d){d=await fetch('/api/container?vnum='+encodeURIComponent(it.Vnum)).then(r=>{if(!r.ok)throw r.status;return r.json()});boxCache.set(it.Vnum,d)}renderBox(host,d)}catch{host.innerHTML='<p class="muted">İçerik bilgisi yüklenemedi.</p>'}}
const pct=v=>new Intl.NumberFormat('tr-TR',{maximumFractionDigits:5}).format(v);
const BOX_NOTE='<p class="box-note"><i>i</i><span>Bu sandığın içeriği ya da oranları henüz tam değil. Eksikleri tamamlamak için çalışıyoruz.</span></p>';
function renderBox(host,d){
  if(d.status==='variants'){host.innerHTML=`<p class="muted">Bu sandığın farklı etkinlik sürümleri var. Sürüm seç:</p><label class="field sel" style="height:38px;margin-bottom:10px"><select aria-label="Sandık sürümü"><option value="">Sürüm seç…</option>${d.variants.map((v,i)=>`<option value="${i}">${esc(v.title)}</option>`).join('')}</select></label><div class="box-variant"></div>`;
    const s=host.querySelector('select'),v=host.querySelector('.box-variant');s.addEventListener('change',()=>{v.innerHTML='';if(s.value!=='')renderBox(v,{...d.variants[Number(s.value)],status:'available',selection:d.selection,checkedAt:d.checkedAt})});
    if(d.defaultVariant!=null){s.value=String(d.defaultVariant);s.dispatchEvent(new Event('change'))}return}
  if(d.status!=='available'&&d.selection){host.innerHTML='<p class="box-sum">Bu sandıkta <b>listeden istediğin eşyayı seçersin</b>; rastgele ödül ya da çıkma şansı yoktur.</p><p class="muted">Seçenekler henüz eklenmedi.</p>'+BOX_NOTE;return}
  if(d.status!=='available'){host.innerHTML='<p class="muted">Bu sandık için doğrulanmış içerik listesi henüz yok.</p>'+BOX_NOTE;return}
  const rated=d.rewards.filter(r=>Number.isFinite(r.chancePercent)).length;
  host.innerHTML=`<p class="box-sum">${d.selection?'Bu sandığı açınca <b>aşağıdaki listeden istediğin tek bir eşyayı seçersin</b>. Rastgele ödül ya da çıkma şansı yoktur.':rated?`<b>${nf.format(rated)} / ${nf.format(d.rewards.length)}</b> ödül için yayımlanmış çıkma oranı var.`:'Bu sandığın çıkma oranları henüz doğrulanamadı.'}</p>
    ${d.rewards.length>12?'<label class="field" style="height:36px;margin-bottom:8px">'+ic('search')+'<input type="search" placeholder="İçerikte ara…" aria-label="Sandık içeriğinde ara"></label>':''}
    <div class="rewards">${d.rewards.map((r,i)=>`<div class="reward" data-r="${i}"><span class="ri">${r.icon?img(itemSrc(r.icon),r.name):ic('box')}</span><span class="rn"><strong>${esc(r.name)}</strong><small>${[r.quantity?'×'+r.quantity:'',r.duration?esc(r.duration):'',r.notes?esc(r.notes):''].filter(Boolean).join(' · ')||esc(r.category||'')}</small><em class="rp" hidden></em></span><span class="rc${Number.isFinite(r.chancePercent)?'':' na'}">${Number.isFinite(r.chancePercent)?'%'+pct(r.chancePercent):d.selection?'Seçilebilir':'Oran yok'}</span></div>`).join('')}</div>
    ${!d.selection&&rated<d.rewards.length?BOX_NOTE:''}
    <p class="muted small">${d.selection?'Listelenenler seçebileceğin eşyalardır.':'Listelenenler olası ödüllerdir. Oranlar yayımlanmış kaynaktandır, etkinlik sürümüne göre değişebilir.'} Bilgi tarihi: ${esc(new Date(d.checkedAt).toLocaleDateString('tr-TR'))}</p>`;
  const inp=host.querySelector('input[type=search]');if(inp)inp.addEventListener('input',()=>{const q=inp.value.toLocaleLowerCase('tr');host.querySelectorAll('.reward').forEach((el,i)=>el.hidden=!d.rewards[i].name.toLocaleLowerCase('tr').includes(q))});
  host.querySelectorAll('.reward').forEach((el,i)=>{el._r=d.rewards[i];el._k=++rewardKey});showRewardPrices(host,d.rewards)}
let rewardKey=0;const boxPrices=new Map();
const priceLine=p=>p?`Pazarda en ucuz: <b>${esc(unitShort(p.unit))}</b> / adet${p.listings>1?` · ${nf.format(p.listings)} ilan`:''}`:'';
async function showRewardPrices(host,rewards){const ids=[...new Set(rewards.map(r=>r.vnum).filter(Boolean))].filter(v=>!boxPrices.has(v)).slice(0,120);
  if(ids.length){try{const d=await api('unit-prices',{vnums:ids.join(',')});for(const v of ids)boxPrices.set(v,d.prices?.[v]||null)}catch{}}
  host.querySelectorAll('.reward').forEach(el=>{const p=boxPrices.get(el._r?.vnum);const e=el.querySelector('.rp');if(e&&p){e.innerHTML=priceLine(p);e.hidden=false}})}
function rewardCard(r){const lines=[...(r.description?String(r.description).split(/\n/):[]),...(r.effects||[])].filter(Boolean),p=boxPrices.get(r.vnum),
  facts=[r.quantity?`<span><small>Adet</small><b>×${esc(r.quantity)}</b></span>`:'',r.duration?`<span><small>Süre</small><b>${esc(r.duration)}</b></span>`:'',Number.isFinite(r.chancePercent)?`<span><small>Çıkma şansı</small><b>%${pct(r.chancePercent)}</b></span>`:''].filter(Boolean).join('');
  return `<div class="gt reward-gt"><div class="gt-head"><div class="gt-img">${r.icon?img(itemSrc(r.icon),r.name):ic('box')}</div><div class="gt-name" style="color:#e6e3dc">${esc(r.name)}</div></div>${lines.length?`<div class="gt-lines">${lines.map(l=>`<div style="color:${DEFAULT_LINE}">${esc(l)}</div>`).join('')}</div>`:''}
    ${facts?`<div class="gt-sec"><h4>Bu sandıkta</h4><div class="feat-row">${facts}</div></div>`:''}
    ${(r.contents||[]).length?`<div class="gt-sec"><h4>İçeriği</h4><div class="rw-contents">${r.contents.slice(0,14).map(c=>`<div>• ${esc(c.name)}${c.quantity?' ×'+esc(c.quantity):''}${Number.isFinite(c.chancePercent)?` <i>%${pct(c.chancePercent)}</i>`:''}</div>`).join('')}${r.contents.length>14?`<div class="muted">+${r.contents.length-14} eşya daha</div>`:''}</div></div>`:''}
    <div class="gt-sec"><h4>Pazar</h4><div class="hint-line rw-market">${p?priceLine(p):r.vnum?'Şu an pazarda ilan yok':'Bu ödül pazarda satılan bir eşya değil'}</div></div></div>`}

async function loadSimilar(src,host){try{const d=await api('similar',{id:src.id});d.suggestions.forEach(r=>items.set(r.item.id,r.item));
  host.innerHTML=d.suggestions.length?`<p class="muted small">${[1,2,28,29].includes(src.ClientDetails?.itemType)?'Aynı model, birçok efsunu yakın ve daha düşük adet fiyatı.':'Aynı eşyanın daha düşük fiyatlı ilanları.'}</p>`+d.suggestions.map(r=>{const x=r.item,e=elementFrom(x.ClientDetails?.element);return `<div class="sim">
    <div class="sim-top"><span class="slot sm" data-hover="${esc(x.id)}">${itemImg(x)}</span><div><strong>${esc(title(x))}</strong><small>${esc(x.Seller)} · ${esc(ago(x.observed))}</small></div><span class="save">%${nf2.format(r.savingPercent)} daha düşük</span></div>
    <div class="sim-mid"><b class="${unitValue(x)>=1e8?'u-won':'u-yang'}">${esc(moneyText(unitValue(x)))}</b><span>adet başı ${esc(moneyText(r.saving))} fark</span></div>
    <div class="sim-tags"><span class="st-match">${r.shared}/${r.sourceBonuses} efsun türü aynı</span>${r.sameQuality?'<span>Aynı simya kalitesi</span>':`<span>${r.upgradeDifference===0?'Aynı yükseltme':'Yükseltme '+(r.upgradeDifference>0?'+':'')+r.upgradeDifference}</span><span>${r.sameElement?'Aynı element seviyesi':e?esc(e.name)+' +'+e.grade:'Element yok'}</span>`}</div>
    <div class="sim-diff">${r.sameValues?`<span class="sd same">${r.sameValues} efsun değeri aynı</span>`:''}${r.lowerCount?`<span class="sd lo">${r.lowerCount} efsun daha düşük</span>`:''}${r.higherCount?`<span class="sd hi">${r.higherCount} efsun daha yüksek</span>`:''}${(r.missing||[]).slice(0,2).map(m=>`<span class="sd miss" title="Bu ilanda yok">Yok: ${esc(m.label)}</span>`).join('')}${(r.extra||[]).slice(0,2).map(m=>`<span class="sd only" title="Yalnız bu ilanda">Ekstra: ${esc(m.label)}</span>`).join('')}</div>
    ${(r.differences||[]).length?`<ul class="sim-vals">${r.differences.slice(0,5).map(d=>`<li class="${d.direction<0?'lo':'hi'}"><span>${esc(String(d.label).replace(/\s*[+%:]?\s*-?\d[\d.,]*\s*%?\s*$/,'').trim()||d.label)}</span><b>${esc(d.current)} <i>→</i> ${esc(d.alternative)}</b></li>`).join('')}</ul>`:''}
    <div class="sim-act"><button class="btn sm" data-open="${esc(x.id)}">İncele</button><button class="btn sm" data-pair="${esc(src.id)}|${esc(x.id)}">${ic('compare')}İkisini karşılaştır</button></div></div>`}).join(''):'<p class="muted">Bu ilan için benzer ve daha ucuz bir alternatif bulunamadı.</p>'}
  catch{host.innerHTML='<p class="muted">Alternatifler yüklenemedi.</p>'}}

function openModal(id){const d=$(id);hideCard();closeDetail();if(!d.open)d.showModal();d.append(hc)}
for(const d of document.querySelectorAll('dialog.modal'))d.addEventListener('close',()=>{hideCard();document.body.append(hc)});

let shop={seller:'',pages:[],page:0,sel:null,items:[]};
async function openShop(name){shop={seller:name,pages:[],page:0,sel:null,items:[],pop:false};$('shop-name').textContent=name;$('shop-sub').textContent='Kayıtlar yükleniyor…';$('shop-body').innerHTML='<div class="skeleton" style="grid-column:1/-1;height:380px"></div>';openModal('shop');
  try{const r=await api('shop',{seller:name});shop.items=r.items||[];shop.items.forEach(x=>items.set(x.id,x));shop.layout=ShopLayout.arrange(shop.items,r.layout);shop.layout.mode=r.layout?.mode;shop.layout.sections=r.layout?.sections;shop.pages=shop.layout.pages;shop.sel=shop.items[0]?.id;shop.total=r.total??shop.items.length;renderShop()}catch{$('shop-body').innerHTML='<div class="empty" style="grid-column:1/-1">Satıcı bilgileri yüklenemedi.</div>'}}
function renderShop(){const en=shop.pages[shop.page]||[],qty=shop.items.reduce((s,x)=>s+ShopLayout.quantity(x),0),tot={Won:shop.items.reduce((s,x)=>s+(x.Won||0),0),Yang:shop.items.reduce((s,x)=>s+(x.Yang||0),0)};
  const inventory=en.map(({x,col,row,size})=>{const count=ShopLayout.quantity(x),fake=isFakeListing(x);
    return `<button class="inv-item${x.id===shop.sel?' on':''}${fake?' fake-listing'+staleCls(x):''}"${fakeExpiry(x)} data-id="${esc(x.id)}" data-pick="${esc(x.id)}" style="grid-column:${col};grid-row:${row} / span ${size}" aria-label="${esc(title(x))} — ${nf.format(count)} adet${fake?' · FAKE pazar':''}" title="${esc(title(x))} · ${nf.format(count)} adet${fake?' · FAKE pazar':''}">${itemImg(x)}${fake?`<span class="inv-fake" data-tip="${esc(fakeTitle(x))}">${isStaleFake(x)?'FAKE?':'FAKE'}</span>`:''}${count>1?`<span class="q" aria-label="${nf.format(count)} adet">${nf.format(count)}</span>`:''}</button>`;
  }).join('');
  $('shop-sub').textContent=`${nf.format(shop.total)} ilan · ${nf.format(qty)} adet · toplam ${priceText(tot)}`;const sel=items.get(shop.sel);
  $('shop-body').innerHTML=`<div>${shop.items.some(isFakeListing)?(f=>`<div class="shop-fake-notice${staleCls(f)}"${fakeExpiry(f)}><strong>${isStaleFake(f)?'FAKE? doğrulama eski':'FAKE PAZAR'}</strong><span>Son kontrol ${esc(checkAgo(f))} önce: oyun bu pazarın alışverişe hazır olmadığını bildirdi; bu bir hile iddiası değildir.</span></div>`)(shop.items.find(isFakeListing)):''}<p class="shop-layout-note">${shop.layout.verified?'Kaydedilen pazar yerleşimi':'Bu pazar için tutarlı yerleşim kaydı yok; eşyalar otomatik sıralanıyor.'}</p><div class="inv-grid" style="--shop-cols:${shop.layout.columns};--shop-rows:${shop.layout.rows}"><div class="inv-bg">${'<i></i>'.repeat(shop.layout.columns*shop.layout.rows)}</div>${inventory}</div>
    <div class="shop-pages"><button class="ibtn" data-spage="-1" ${shop.page===0?'disabled':''} aria-label="Önceki sayfa">‹</button><span>Pazar sayfası ${shop.page+1} / ${shop.pages.length} · ${shop.layout.mode==='cashmere'?'Kaşmir · I + II':'Normal pazar'} · ${shop.layout.columns} × ${shop.layout.rows} ızgara</span><button class="ibtn" data-spage="1" ${shop.page>=shop.pages.length-1?'disabled':''} aria-label="Sonraki sayfa">›</button></div></div>
    <aside class="shop-side"><button class="shop-pop-x" type="button" data-pop-close aria-label="Kapat">${ic('close')}</button>${sel?gtHTML(sel,{foot:false,wide:innerWidth>1000&&!!(sel.embedded||sockets(sel).length||sel.SungmaScore||sel.YinYangValue)})+`<div class="d-meta" style="margin:10px 0 0"><div><small>Fiyat</small><div class="d-price">${priceHTML(sel)}</div></div><div><small>Adet</small>${nf.format(ShopLayout.quantity(sel))}</div><div><small>Görüldü</small>${esc(ago(sel.observed))}</div></div><div class="d-actions"><button class="btn" data-hist="${esc(sel.id)}">${ic('history')}Geçmiş</button><button class="btn${state.cmp.some(c=>c.id===sel.id)?' on':''}" data-cmp="${esc(sel.id)}">${ic('compare')}Karşılaştır</button></div>`:'<div class="empty">Bu satıcıya ait kayıt yok.</div>'}</aside>`;$('shop-body').classList.toggle('pop',!!shop.pop)}

function toggleCmp(id,force){const i=state.cmp.findIndex(c=>c.id===id);if(i>=0&&force!==true)state.cmp.splice(i,1);else if(i<0){if(state.cmp.length>=MAX_CMP){toast(`En fazla ${MAX_CMP} ilan karşılaştırılabilir.`);return}const it=items.get(id);if(it)state.cmp.push(it)}
  write('m2s-v3-cmp',state.cmp);renderTray();const on=state.cmp.some(c=>c.id===id);document.querySelectorAll(`[data-cmp="${CSS.escape(id)}"]`).forEach(b=>b.classList.toggle('on',on));document.querySelectorAll(`.item[data-id="${CSS.escape(id)}"]`).forEach(r=>r.classList.toggle('cmp',on))}
function renderTray(){const n=state.cmp.length;$('compare-count').textContent=n;$('compare-count').classList.toggle('on',n>0);$('tray').hidden=!n;
  $('tray').innerHTML=`<div class="thumbs">${state.cmp.map(x=>`<span title="${esc(title(x))}">${itemImg(x)}</span>`).join('')}${'<i></i>'.repeat(MAX_CMP-n)}</div><div class="txt"><b>${n} / ${MAX_CMP} ilan seçildi</b>${n<2?'Bir ilan daha seç':'Yan yana incelemek için aç'}</div><button class="btn sm ghost" data-cmp-clear type="button">Temizle</button><button class="btn sm gold" data-cmp-open type="button" ${n<2?'disabled':''}>${ic('compare')}Karşılaştır</button>`}
function summaryMap(x){const m=new Map();for(const b of x.displayBonuses||[]){const g=x.ClientDetails?.itemType===29?'simya':Number(b.slot)>=5?'rare':'first',k=g+'|'+b.type;if(!m.has(k))m.set(k,{type:Number(b.type),group:g,values:[]});m.get(k).values.push(Number(b.value))}return m}
function cmpSummary(xs){const base=xs[0],gn=g=>g==='rare'?'6–7':g==='simya'?'Simya':'1–5';return `<div class="cmp-sum"><div class="cmp-sum-head"><strong>Fark özeti</strong><span>İlk ilan referans: ${esc(title(base))}</span></div><div class="cmp-sum-grid">${xs.slice(1).map((x,i)=>{
  const d=unitValue(x)-unitValue(base),a=summaryMap(base),b=summaryMap(x),rows=[];for(const k of new Set([...a.keys(),...b.keys()])){const l=a.get(k),r=b.get(k),inf=l||r,lb=esc(statLabel(inf.type));
    if(!l||!r)rows.push(`<li><i>${gn(inf.group)}</i>${lb} ${l?'bu ilanda yok':'yalnızca bu ilanda ('+r.values.map(v=>nf.format(v)).join(', ')+')'}</li>`);
    else if(JSON.stringify(l.values)!==JSON.stringify(r.values)&&l.values.length===1&&r.values.length===1){const dv=r.values[0]-l.values[0];rows.push(`<li><i>${gn(inf.group)}</i>${lb} <b class="${dv>0?'pos':'neg'}">${dv>0?'+':''}${nf.format(dv)}</b> <small>${nf.format(l.values[0])} → ${nf.format(r.values[0])}</small></li>`)}}
  return `<article><h5>İlan ${i+2} · ${esc(title(x))}</h5><strong class="${d<0?'pos':d>0?'neg':''}">${d===0?'Adet başı fiyat aynı':esc(moneyText(Math.abs(d)))+(d<0?' daha ucuz':' daha pahalı')+' · adet başı'}</strong><ul>${rows.slice(0,5).join('')||'<li>Efsunlar aynı.</li>'}</ul></article>`}).join('')}</div></div>`}
function openCompare(){const xs=state.cmp;if(xs.length<1)return;const n=xs.length,cell=(v,cls='')=>`<div class="${cls}">${v}</div>`;
  const tones=(raw,low,neutral)=>{const nums=raw.map(v=>v==null||isNaN(Number(v))?null:Number(v)),have=nums.filter(v=>v!=null);if(!have.length)return nums.map(()=>'');
    const some=have.length<nums.length,eq=have.every(v=>v===have[0]),hi=Math.max(...have),lo=Math.min(...have);
    return nums.map(v=>{if(v==null)return 'miss';if(have.length===1)return nums.length>1?'only':'';if(eq)return some?'':'same';if(neutral)return '';return v===(low?lo:hi)?'good':v===(low?hi:lo)?'bad':''})};
  const row=(label,vals,raw,low,neutral)=>{const tn=raw?tones(raw,low,neutral):[],cmpRow=!!raw&&tn.every(t=>t==='same');return cell(esc(label),'rh'+(cmpRow?' same':''))+vals.map((v,i)=>{const t=tn[i]||'',empty=v==null||v==='';return cell(empty?(t==='miss'?'<span class="miss-tag">Yok</span>':'<span class="na">—</span>'):`<span class="cv">${v}</span>${t==='only'?'<small class="only-tag">yalnız bunda</small>':''}`,t?'t-'+t:'')}).join('')};
  let h=cell('','rh top')+xs.map(x=>`<div class="top"><div class="slot" data-hover="${esc(x.id)}">${itemImg(x)}</div><strong>${esc(title(x))}</strong><button class="seller-btn" data-shop="${esc(x.Seller)}" style="margin-top:6px">${ic('shop')}<span>${esc(x.Seller)}</span></button><br><button class="btn sm ghost" data-cmp="${esc(x.id)}" data-cmp-remove>${ic('close')}Çıkar</button></div>`).join('');
  h+=row('Toplam fiyat',xs.map(x=>`<div class="d-price">${priceHTML(x,{unit:false})}</div>`),xs.map(priceValue),true);
  h+=row('Adet fiyatı',xs.map(x=>`<span class="${unitValue(x)>=1e8?'u-won':'u-yang'}">${esc(unitShort(unitValue(x)))}</span>`),xs.map(unitValue),true);
  h+=row('Adet',xs.map(x=>nf.format(x.Count)),xs.map(x=>x.Count),false,true);h+=row('Seviye',xs.map(x=>itemLevel(x)||null),xs.map(x=>itemLevel(x)||null),false,true);h+=row('Yükseltme',xs.map(x=>plusOf(x)?'+'+plusOf(x):null),xs.map(x=>Number(plusOf(x))||null));
  const els=xs.map(x=>elementOf(x));if(els.some(Boolean))h+=row('Element',els.map(e=>e?`<span class="el-mini" style="--el:${e.color}">${elImg(e)}${esc(elementText(e))}</span>`:null),els.map(e=>e?e.value:null));
  if(xs.some(x=>x.absorption!=null))h+=row('Emiş oranı',xs.map(x=>x.absorption!=null?'%'+x.absorption:null),xs.map(x=>x.absorption),false);
  if(xs.some(x=>x.SungmaScore))h+=row('SungMa puanı',xs.map(x=>x.SungmaScore?nf.format(x.SungmaScore):null),xs.map(x=>x.SungmaScore||null));
  if(xs.some(x=>sockets(x).length))h+=row('Taşlar / cevherler',xs.map(x=>stoneIcons(sockets(x),'mini-stones big')||null));
  if(xs.some(x=>x.embedded)){h+=row('Emdirilen ekipman',xs.map(x=>x.embedded?`<span class="mini-emb" data-hover="${esc(x.id)}">${img(itemSrc(x.embedded.icon),x.embedded.name)}<span><b>${esc(x.embedded.name)}</b></span></span>`:null));
    const types=[];for(const x of xs)for(const b of x.ClientDetails?.bonuses||[])if(x.embedded&&!types.includes(b.type))types.push(b.type);
    if(types.length){h+='<div class="grp">Emdirilen ekipmanın efsunları (%100)</div>';for(const t of types){const lb=statLabel(t),bs=xs.map(x=>x.embedded?(x.ClientDetails?.bonuses||[]).find(b=>b.type===t):null);h+=row(lb,bs.map(b=>b?esc(b.text.startsWith(lb)?b.text.slice(lb.length).trim()||b.text:b.text):null),bs.map(b=>b?b.value:null))}}}
  const types=[];for(const x of xs)for(const b of x.displayBonuses||[])if(!types.includes(b.type))types.push(b.type);
  if(types.length){h+=`<div class="grp">${xs.some(x=>x.embedded)?'Kuşağa geçen efsunlar':'Efsunlar'}</div>`;for(const t of types){const lb=statLabel(t),bs=xs.map(x=>(x.displayBonuses||[]).find(b=>b.type===t));h+=row(lb,bs.map(b=>{if(!b)return null;const v=b.text.startsWith(lb)?b.text.slice(lb.length).trim():b.text;return esc(v||b.text)}),bs.map(b=>b?b.value:null))}}
  $('cmp-body').innerHTML=(n>1?cmpSummary(xs):'')+(n>1?'<div class="cmp-legend"><span class="s"><i></i>Aynı</span><span class="g"><i></i>Daha iyi / yüksek</span><span class="r"><i></i>Daha düşük</span><span class="o"><i></i>Yalnız bu ilanda</span><span class="m"><i></i>Bu ilanda yok</span></div>':'')+`<div class="cmp-table" style="--n:${n}">${h}</div>`;openModal('compare')}

function historyStates(obs){const states=[],last=new Map();for(const r of obs.slice().reverse()){const k=r.seller+'|'+r.quantity,l=last.get(k);if(l&&l.won===r.won&&l.yang===r.yang&&l.feature_key===r.feature_key){l.lastSeen=r.observed;l.samples++;continue}const n={...r,lastSeen:r.observed,samples:1,previous:states.at(-1)||null};states.push(n);last.set(k,n)}return states}
async function openHistory(id,retry=false){const it=items.get(id);$('hist-title').textContent=it?title(it):'Eşya';$('hist-sub').textContent='Gözlemler yükleniyor…';$('hist-ico').innerHTML=it?itemImg(it):ic('history');$('hist-body').innerHTML='<div class="skeleton" style="height:260px"></div>';openModal('history');
  try{const h=await api('history',{id,...(retry?{refresh:Date.now()}:{})});const st=historyStates(h.observations||[]);
    if(h.hasPreviousObservations===false||st.length<1){$('hist-sub').textContent='Bu eşya için henüz önceki kayıt yok.';$('hist-body').innerHTML=`<div class="empty">Bu ilan ilk kez görüldü${h.observations?.[0]?' · '+esc(dateFmt(h.observations[0].observed)):''}. Yeni gözlemler geldikçe fiyat değişimleri burada görünecek.</div>`+relatedHTML(h.related);return}
    st.forEach(r=>{if(r.item){r.item.id=r.item.id||'h-'+r.observation_id+r.observed;r.item.category??=it?.category;items.set(r.item.id,r.item)}});
    const vals=st.map(r=>(r.won*1e8+r.yang)/Math.max(1,r.quantity)),mn=Math.min(...vals),mx=Math.max(...vals),sellers=new Set(st.map(r=>r.seller)).size;
    $('hist-sub').textContent=`${nf.format(h.observations.length)} gözlem · ${nf.format(st.length)} farklı kayıt · ${sellers} satıcı`;
    const W=900,H=150,L=76,T=8,B=22,t0=new Date(st[0].observed).getTime(),t1=Math.max(t0+1,...st.map(r=>new Date(r.lastSeen).getTime())),X=t=>L+(t-t0)/(t1-t0)*(W-L-8),Y=v=>T+(1-(v-mn)/((mx-mn)||1))*(H-T-B);
    let path='',dots='';st.forEach((r,i)=>{const x=X(new Date(r.observed).getTime()),y=Y(vals[i]);path+=i?`L${x.toFixed(1)},${Y(vals[i-1]).toFixed(1)}L${x.toFixed(1)},${y.toFixed(1)}`:`M${x.toFixed(1)},${y.toFixed(1)}`;dots+=`<circle cx="${x}" cy="${y}" r="3.5" fill="#c9a45e"><title>${esc(dateFmt(r.observed))} · ${esc(moneyText(vals[i]))} · ${esc(r.seller)}</title></circle>`});
    path+=`L${X(t1).toFixed(1)},${Y(vals.at(-1)).toFixed(1)}`;
    const yl=[mx,(mx+mn)/2,mn].map(v=>`<text x="${L-8}" y="${Y(v)+4}" text-anchor="end">${esc(unitShort(v))}</text><line x1="${L}" x2="${W-8}" y1="${Y(v)}" y2="${Y(v)}" stroke="rgba(255,255,255,.05)"/>`).join('');
    const xl=[t0,t1].map((t,i)=>`<text x="${i?W-8:L}" y="${H-4}" text-anchor="${i?'end':'start'}">${esc(dateFmt(new Date(t).toISOString()))}</text>`).join('');
    const rows=st.slice().reverse().map(r=>{const v=(r.won*1e8+r.yang)/Math.max(1,r.quantity),p=r.previous?(r.previous.won*1e8+r.previous.yang)/Math.max(1,r.previous.quantity):null,d=p?((v-p)/p*100):null,feat=r.previous&&r.previous.feature_key!==r.feature_key;
      return `<tr><td>${r.item?`<span class="slot xs" data-hover="${esc(r.item.id)}">${itemImg(r.item)}</span>`:'<span class="slot xs na">—</span>'}</td><td>${esc(dateFmt(r.observed))}${r.samples>1?`<br><small>${r.samples} kez görüldü · son ${esc(dateFmt(r.lastSeen))}</small>`:''}</td><td><button class="seller-btn" data-shop="${esc(r.seller)}">${ic('shop')}<span>${esc(r.seller)}</span></button></td><td class="r">${nf.format(r.quantity)}</td><td class="r"><div class="d-price">${priceHTML({Won:r.won,Yang:r.yang,Count:1})}</div></td><td class="r">${d==null?'':d===0?'<small>Fiyat aynı</small>':`<span class="${d>0?'up':'down'}">${d>0?'↑':'↓'} %${nf2.format(Math.abs(d))}</span>`}${feat?'<br><small class="feat-ch">Özellikler değişti</small>':''}</td></tr>`}).join('');
    $('hist-body').innerHTML=`<div class="hist-stats"><div><small>En düşük adet fiyatı</small><strong>${esc(moneyText(mn))}</strong></div><div><small>En yüksek adet fiyatı</small><strong>${esc(moneyText(mx))}</strong></div><div><small>İlk gözlem</small><strong>${esc(dateFmt(st[0].observed))}</strong></div><div><small>Son gözlem</small><strong>${esc(ago(st.at(-1).lastSeen))}</strong></div></div>
      <div class="hist-chart"><svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none">${yl}${xl}<path d="${path}" fill="none" stroke="#c9a45e" stroke-width="1.6" vector-effect="non-scaling-stroke"/>${dots}</svg></div>
      <table class="hist-table"><thead><tr><th></th><th>Gözlem</th><th>Satıcı</th><th class="r">Adet</th><th class="r">Fiyat</th><th class="r">Değişim</th></tr></thead><tbody>${rows}</tbody></table>
      ${h.snapshotsMissing?`<p class="muted small">${nf.format(h.snapshotsMissing)} gözlemin eşya ayrıntısı henüz yüklenmedi. <button class="btn sm ghost" data-hist-retry="${esc(id)}">${ic('reset')}Yeniden yükle</button></p>`:''}${h.limited?'<p class="muted small">Son 300 gözlem gösteriliyor.</p>':''}${relatedHTML(h.related)}`;
  }catch{$('hist-sub').textContent='';$('hist-body').innerHTML='<div class="empty">Fiyat geçmişi şu an yüklenemiyor.</div>'}}
function relatedHTML(rel){if(!rel?.length)return'';rel.forEach(x=>items.set(x.id,x));return `<details class="d-sec fold related"><summary>${ic('layers')}Aynı satıcının farklı özellikteki kayıtları <b>${nf.format(rel.length)}</b>${ic('chevron')}</summary><p class="muted small">Aynı eşya türüne ait ayrı kayıtlar; aynı fiziksel eşyanın değişimi olduğu doğrulanamaz.</p><div class="rel-grid">${rel.map(x=>`<div class="rel" data-hover="${esc(x.id)}"><span class="slot xs">${itemImg(x)}</span><div><strong>${esc(title(x))}</strong><small>${esc(dateFmt(x.observed))}</small></div><div class="d-price">${priceHTML(x,{unit:false})}</div></div>`).join('')}</div></details>`}

function setupSuggest(inputId,boxId,kind){const input=$(inputId),box=$(boxId);let timer,ctrl,rows=[],active=-1;
  const close=()=>{box.hidden=true;input.setAttribute('aria-expanded','false');active=-1};
  const mark=(t,q)=>{const i=t.toLocaleLowerCase('tr').indexOf(q.toLocaleLowerCase('tr'));return i<0?esc(t):esc(t.slice(0,i))+'<mark>'+esc(t.slice(i,i+q.length))+'</mark>'+esc(t.slice(i+q.length))};
  const draw=q=>{box.innerHTML=rows.length?rows.map((r,i)=>`<button type="button" role="option" data-i="${i}" class="${i===active?'on':''}"><span class="sg-img">${kind==='item'?img(itemSrc(r.icon),''):ic('shop')}</span><span><strong>${mark(r.label,q)}</strong><small>${kind==='item'?esc(catName(r.category)+(r.sub!=null&&subNames(r.category)[r.sub]?' · '+subNames(r.category)[r.sub]:'')):'Satıcı pazarı'}</small></span></button>`).join(''):'<div class="none">Eşleşen kayıt yok.</div>';box.hidden=false;input.setAttribute('aria-expanded','true')};
  const pick=i=>{const r=rows[i];if(!r)return;input.value=r.label;close();if(kind==='item'){state.q=r.label;state.qFamily=true;if(r.category!=null){state.category=r.category;state.sub=r.sub??'';clearSpecial()}}else{state.seller=r.label;state.sellerExact=true}load()};
  input.addEventListener('input',()=>{clearTimeout(timer);ctrl?.abort();const q=input.value.trim();toggleClear();if(q.length<2){close();return}
    timer=setTimeout(async()=>{ctrl=new AbortController();try{const r=await fetch('/api/search-suggestions?'+new URLSearchParams({server:SERVER,kind,q}),{signal:ctrl.signal}).then(r=>r.json());if(input.value.trim()!==q)return;rows=r.suggestions||[];active=-1;draw(q)}catch{}},180)});
  input.addEventListener('keydown',e=>{if(e.key==='ArrowDown'||e.key==='ArrowUp'){if(box.hidden||!rows.length)return;e.preventDefault();active=(active+(e.key==='ArrowDown'?1:-1)+rows.length)%rows.length;draw(input.value.trim())}
    else if(e.key==='Enter'){e.preventDefault();if(!box.hidden&&active>=0)pick(active);else{close();if(kind==='item'){state.q=input.value.trim();state.qFamily=false}else{state.seller=input.value.trim();state.sellerExact=false}load()}}else if(e.key==='Escape')close()});
  box.addEventListener('mousedown',e=>{const b=e.target.closest('[data-i]');if(b){e.preventDefault();pick(Number(b.dataset.i))}});input.addEventListener('blur',()=>setTimeout(close,120))}
function toggleClear(){for(const id of['q','seller'])document.querySelector(`[data-clear="${id}"]`).hidden=!$(id).value}

let bfGroup='any',bfMode='gte';
const POPULAR_BONUSES=[72,71,1,4,5,6,3,53,55,15,16,7,9,8,2,63,214,151,152];
const BONUS_GROUPS=[
  ['Canavar ve ırk bonusları',/karşı güçlü|karşısında güçlü/i],
  ['Savunma ve direnç',/savunma|direnç|dayanıklı|bağışıklık|karşı koyma|yansıt|bloklama|savuşturma|absorbe/i],
  ['Element',/^(?:Şimşeklerin|Ateşin|Buzun|Rüzgarın|Toprağın|Karanlığın) gücü/i],
  ['Beceri efsunları',/hasarı|soğuma|emer|sersemlet|misilleme|kullanımında|kullanımda|süresi|savaşta/i]];
function fillBonusTypes(){const keys=(state.meta.statKeys||Object.keys(state.meta.stats||{})).map(Number).filter(Boolean);
  const opts=keys.map(k=>[k,statLabel(k)]).filter(([,l])=>l),byKey=new Map(opts.map(o=>[Number(o[0]),o])),used=new Set(),groups=[];
  const top=POPULAR_BONUSES.map(k=>byKey.get(k)).filter(Boolean);top.forEach(o=>used.add(Number(o[0])));if(top.length)groups.push(['Popüler efsunlar',top]);
  const rest=opts.filter(o=>!used.has(Number(o[0]))).sort((a,b)=>a[1].localeCompare(b[1],'tr')),buckets=new Map(BONUS_GROUPS.map(g=>[g[0],[]]));buckets.set('Diğer efsunlar',[]);
  for(const o of rest){(buckets.get((BONUS_GROUPS.find(g=>g[1].test(o[1]))||[,,'Diğer efsunlar'])[0])||buckets.get('Diğer efsunlar')).push(o)}
  for(const [name,list] of buckets)if(list.length)groups.push([name,list]);
  $('bf-type').innerHTML='<option value="">Efsun seç…</option>'+groups.map(([name,list])=>`<optgroup label="${esc(name)}">${list.map(([k,l])=>`<option value="${k}">${esc(l)}</option>`).join('')}</optgroup>`).join('');window.refreshSelects?.()}
function setSeg(id,attr,v){document.querySelectorAll(`#${id} button`).forEach(b=>b.setAttribute('aria-pressed',b.dataset[attr]===v))}
function addBonus(){const type=$('bf-type').value,value=$('bf-val').value;if(!type){toast('Önce bir efsun seç.');return}if(value===''){$('bf-val').focus();toast('Aradığın efsun değerini yaz.');return}
  const f={type:Number(type),value:Number(value),mode:bfMode,group:bfGroup};if(state.editBonus>=0)state.bonus[state.editBonus]=f;else{if(state.bonus.length>=7){toast('En fazla 7 efsun koşulu eklenebilir.');return}state.bonus.push(f)}resetBonusDraft();load()}
function resetBonusDraft(){state.editBonus=-1;$('bf-type').value='';$('bf-val').value='';bfMode='gte';bfGroup='any';setSeg('bf-group','g','any');setSeg('bf-mode','m','gte');$('bf-add').lastChild.textContent='Ekle';$('bf-cancel').hidden=true;window.refreshSelects?.()}
function setAdv(open){$('adv').hidden=!open;$('adv-btn').setAttribute('aria-expanded',open);document.querySelector('.search-panel').classList.toggle('m-open',open)}
if(!matchMedia('(max-width:640px)').matches)setAdv(true);
function editBonus(i){const b=state.bonus[i];if(!b)return;if(matchMedia('(max-width:640px)').matches&&$('adv').hidden)setAdv(true);state.editBonus=i;$('bf-type').value=String(b.type);$('bf-val').value=b.value;bfMode=b.mode||'gte';bfGroup=b.group||'any';setSeg('bf-group','g',bfGroup);setSeg('bf-mode','m',bfMode);$('bf-add').lastChild.textContent='Güncelle';$('bf-cancel').hidden=false;window.refreshSelects?.();renderApplied();$('bf-val').focus()}

async function setupServers(){try{const h=await fetch('/api/home').then(r=>r.json());const cur=h.servers.find(s=>s.id===SERVER);SERVER_NAME=cur?.name||SERVER;
  document.querySelectorAll('[data-server-name]').forEach(e=>e.textContent=SERVER_NAME);document.title=`Metin2Search • ${SERVER_NAME} Pazarı`;
  $('server-menu').innerHTML='<small>Türkiye sunucuları</small>'+h.servers.sort((a,b)=>Number(b.available)-Number(a.available)).map(s=>`<button type="button" data-server="${esc(s.id)}" ${s.available?'':'disabled'} class="${s.id===SERVER?'on':''}"><span>${esc(s.name)}</span><small>${s.available?nf.format(s.listings||0)+' ilan':'Veri bekleniyor'}</small>${s.id===SERVER?ic('check'):''}</button>`).join('')}catch{}}

function toast(msg,btn){document.querySelectorAll('.toast').forEach(t=>t.remove());const t=document.createElement('div');t.className='toast';t.innerHTML=`<span>${esc(msg)}</span>${btn||''}`;([...document.querySelectorAll('dialog[open]')].pop()||document.body).append(t);setTimeout(()=>t.remove(),btn?20000:3200);return t}
function toggleSave(id){state.saved.has(id)?state.saved.delete(id):state.saved.add(id);savedGone.delete(id);write('m2s-v3-saved',[...state.saved]);updateSavedBadge();
  document.querySelectorAll(`[data-save="${CSS.escape(id)}"]`).forEach(b=>{b.classList.toggle('on',state.saved.has(id));const s=b.querySelector('span');if(s)s.textContent=state.saved.has(id)?'Kaydedildi':'Kaydet'})}
async function copy(text,msg){try{await navigator.clipboard.writeText(text);toast(msg)}catch{prompt('Bağlantıyı kopyala:',text)}}
document.addEventListener('click',e=>{const t=e.target;let el;
  if(!t.closest('#server-menu,#server-btn'))$('server-menu').hidden=true;
  if(el=t.closest('[data-save]')){e.stopPropagation();toggleSave(el.dataset.save);return}
  if(t.closest('[data-cmp-open]')){openCompare();return}
  if(t.closest('[data-cmp-clear]')){state.cmp=[];write('m2s-v3-cmp',[]);renderTray();document.querySelectorAll('[data-cmp].on').forEach(b=>b.classList.remove('on'));document.querySelectorAll('.item.cmp').forEach(r=>r.classList.remove('cmp'));return}
  if(el=t.closest('[data-pair]')){const [a,b]=el.dataset.pair.split('|');state.cmp=[items.get(a),items.get(b)].filter(Boolean);write('m2s-v3-cmp',state.cmp);renderTray();openCompare();return}
  if(el=t.closest('[data-cmp]')){e.stopPropagation();toggleCmp(el.dataset.cmp);if(el.hasAttribute('data-cmp-remove')){state.cmp.length?openCompare():$('compare').close()}return}
  if(el=t.closest('[data-hist-retry]')){openHistory(el.dataset.histRetry,true);return}
  if(el=t.closest('[data-hist]')){e.stopPropagation();openHistory(el.dataset.hist);return}
  if(el=t.closest('[data-shop]')){e.stopPropagation();openShop(el.dataset.shop);return}
  if(t.closest('[data-pop-close]')||t.matches('#shop-body.pop')){shop.pop=false;renderShop();return}
   if(el=t.closest('[data-pick]')){shop.sel=el.dataset.pick;shop.pop=matchMedia('(max-width:760px)').matches;renderShop();return}
  if(el=t.closest('[data-spage]')){shop.page+=Number(el.dataset.spage);renderShop();return}
  if(el=t.closest('[data-open]')){const it=items.get(el.dataset.open);if(it){document.querySelectorAll('dialog[open]').forEach(d=>d.close());openDetail(it)}return}
  if(t.closest('#shop-list')){$('shop').close();state.seller=shop.seller;state.sellerExact=true;$('seller').value=shop.seller;state.favView=false;toggleClear();load();return}
  if(t.closest('[data-close]')){t.closest('dialog').close();return}
  if(t.matches('dialog.modal')){t.close();return}
  if(el=t.closest('[data-relief-bonus]')){state.bonus.splice(Number(el.dataset.reliefBonus),1);resetBonusDraft();toggleClear();load();return}
  if(el=t.closest('[data-relief]')){for(const k of el.dataset.relief.split(','))if(k==='category'){state.category=0;state.sub='';clearSpecial()}else if(k==='sub')state.sub='';else if(k==='bonusFilters')state.bonus=[];else if(k==='q'){state.q='';state.qFamily=false;$('q').value=''}else if(k==='itemFamily'){state.qFamily=false}else if(k==='seller'){state.seller='';$('seller').value=''}else if(k in state.filters)state.filters[k]='';toggleClear();load();return}
  if(el=t.closest('[data-share-item]')){copy(location.origin+location.pathname+'?'+new URLSearchParams({...(SERVER!=='safir'?{server:SERVER}:{}),item:el.dataset.shareItem}),'Eşya bağlantısı kopyalandı. Açan kişi doğrudan bu eşyanın penceresini görür.');return}
  if(t.closest('#share-btn')){copy(shareUrl(),'Arama bağlantısı kopyalandı.');return}
  if(t.closest('#server-btn')){$('server-menu').hidden=!$('server-menu').hidden;return}
  if(el=t.closest('[data-server]')){if(el.dataset.server!==SERVER)location.search='?server='+encodeURIComponent(el.dataset.server);return}
  if(el=t.closest('[data-edit-bonus]')){editBonus(Number(el.dataset.editBonus));return}
  if(t.closest('#bf-cancel')){resetBonusDraft();renderApplied();return}
  if(el=t.closest('[data-cat]')){state.favView=false;state.category=Number(el.dataset.cat);state.sub='';clearSpecial();load();return}
  if(el=t.closest('[data-sub]')){state.sub=el.dataset.sub;for(const k of['catalogFamily','equipmentType','fishSpecies','fishGrilled','absorbMin','absorbMax'])state.filters[k]='';load();return}
  if(el=t.closest('[data-count]')){const set=new Set(String(state.filters.bonusCount).split(',').filter(Boolean));set.has(el.dataset.count)?set.delete(el.dataset.count):set.add(el.dataset.count);state.filters.bonusCount=[...set].sort().join(',');load();return}
  if(el=t.closest('[data-el]')){state.filters.element=state.filters.element===el.dataset.el?'':el.dataset.el;load();return}
  if(t.closest('[data-reset-special]')){clearSpecial();load();return}
  if(t.closest('[data-reset-all]')){state.category=0;state.sub='';clearSpecial();state.q='';state.seller='';state.qFamily=state.sellerExact=false;$('q').value=$('seller').value='';for(const k of FILTER_KEYS)state.filters[k]='';state.bonus=[];resetBonusDraft();toggleClear();load();return}
  if(el=t.closest('[data-rm]')){const k=el.dataset.rm;if(k==='cat'){state.category=0;state.sub='';clearSpecial()}else if(k==='q'){state.q='';state.qFamily=false;$('q').value=''}else if(k==='seller'){state.seller='';state.sellerExact=false;$('seller').value=''}else if(k.startsWith('b:')){state.bonus.splice(Number(k.slice(2)),1);resetBonusDraft()}else state.filters[k.slice(2)]='';toggleClear();load();return}
  if(el=t.closest('[data-clear]')){const id=el.dataset.clear;$(id).value='';if(id==='q'){state.q='';state.qFamily=false}else{state.seller='';state.sellerExact=false}toggleClear();load();return}
  if(el=t.closest('[data-page]')){state.page=Number(el.dataset.page);load(false);$('results').scrollIntoView({block:'start'});return}
  if(el=t.closest('[data-view]')){setView(el.dataset.view);return}
  if(el=t.closest('#bf-group [data-g]')){bfGroup=el.dataset.g;setSeg('bf-group','g',bfGroup);return}
  if(el=t.closest('#bf-mode [data-m]')){bfMode=el.dataset.m;setSeg('bf-mode','m',bfMode);return}
  if(t.closest('#bf-add')){addBonus();return}
  if(t.closest('#adv-btn')){setAdv($('adv').hidden);return}
  if(t.closest('[data-clear-gone]')){savedGone.forEach(id=>state.saved.delete(id));savedGone.clear();write('m2s-v3-saved',[...state.saved]);updateSavedBadge();toast('Listede olmayan kayıtlar çıkarıldı.');load();return}
  if(t.closest('#nav-saved')){e.preventDefault();state.favView=true;load();return}
  if(t.closest('#nav-compare')){e.preventDefault();state.cmp.length>1?openCompare():toast('Karşılaştırmak için satırlardaki karşılaştır düğmesiyle en az 2 ilan seç.');return}
  if(t.closest('#nav-market')&&state.favView){e.preventDefault();state.favView=false;load();return}
  if(el=t.closest('.item')){const it=items.get(el.dataset.id);if(it)openDetail(it);return}
});
document.addEventListener('change',e=>{const el=e.target;
  if(el.matches('[data-f]')){const k=el.dataset.f;state.filters[k]=el.value.trim();if((k==='absorbMin'||k==='absorbMax')&&Number(state.filters[k])>25)state.filters[k]='25';
    if(k==='character')state.filters.equipmentType='';if(k==='alchemyClass')state.filters.bonusCount='';
    if(k==='catalogFamily'){const f=(state.meta.categoryCatalog?.families||[]).find(f=>f.id===el.value);if(f?.group!=='Nakliye kutusu')for(const p of['petState','petType','petSpecies','petLevelMin','petLevelMax'])state.filters[p]=''}
    if(['petState','petType','petSpecies','petLevelMin','petLevelMax'].includes(k)&&el.value){const box=(state.meta.categoryCatalog?.families||[]).find(f=>f.category===1&&f.group==='Nakliye kutusu');if(box){state.filters.catalogFamily=box.id;state.sub=String(box.sub)}}
    load();return}
  if(el.matches('[data-grilled]')){state.filters.fishGrilled=el.checked?'1':'0';state.filters.fishSpecies='';load();return}
  if(el.id==='sort'){state.sort=el.value;load();return}
  if(el.id&&el.id.startsWith('f-')){state.filters[el.id.slice(2)]=el.value.trim();load();return}
});
$('bf-val').addEventListener('keydown',e=>{if(e.key==='Enter')addBonus()});
$('detail-close').onclick=closeDetail;$('scrim').onclick=closeDetail;
document.addEventListener('keydown',e=>{if(e.key==='Escape'){closeDetail();$('server-menu').hidden=true}if(e.key==='/'&&!/INPUT|SELECT|TEXTAREA/.test(document.activeElement.tagName)){e.preventDefault();$('q').focus()}});
function setView(v){state.view=v;write('m2s-v3-view',v);document.querySelectorAll('[data-view]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.view===v));$('list').classList.toggle('grid',v==='grid');$('results').classList.toggle('gridmode',v==='grid')}

function setLive(text,cls){$('live-text').textContent=text;$('state-pill').className='state-pill '+(cls||'')}
function startLive(){if(!window.EventSource){setLive('Kayıtlar hazır');return}let es,retry;const connect=()=>{setLive('Bağlanıyor…','wait');es=new EventSource('/api/events?server='+encodeURIComponent(SERVER));
  es.addEventListener('state',ev=>{let s;try{s=JSON.parse(ev.data)}catch{return}setLive(s.testRunning===false?'Aktarım duraklatıldı':s.available?'Canlı · '+(ago(s.lastSeen)||'güncel'):'Veri bekleniyor',s.available?'':'wait');
    if(state.revision&&s.revision&&s.revision!==state.revision){const t=toast('Yeni kayıtlar var.','<button class="btn sm gold" type="button">Listeyi güncelle</button>');t.querySelector('button').onclick=async()=>{t.remove();state.revision=s.revision;try{state.meta=await api('meta')}catch{}await load(false);toast('Liste güncellendi. Filtrelerin ve sayfan korundu.')}}});
  es.onerror=()=>{setLive('Bağlantı kesildi · yeniden bağlanılıyor','off');es.close();clearTimeout(retry);retry=setTimeout(connect,15000)}};connect()}

async function init(){setupSuggest('q','q-sug','item');setupSuggest('seller','s-sug','player');setView(state.view);
  $('q').value=state.q;$('seller').value=state.seller;toggleClear();$('sort').value=state.sort;
  updateSavedBadge();renderTray();setupServers();
  try{state.meta=await api('meta');state.revision=state.meta.revision}catch(e){$('list').innerHTML=`<div class="empty">${e===409?'Bu sunucu için henüz ilan yok.':'Şu an bağlantı kurulamıyor. Birkaç saniye sonra sayfayı yenileyin.'}</div>`;return}
  $('seen-note').textContent='Son gözlem: '+ago(state.meta.latest);fillBonusTypes();await load(false);checkSaved();startLive();if(url.get('shop'))openShop(url.get('shop'));if(url.get('item'))openSharedItem(url.get('item'))}
async function openSharedItem(id){let it=items.get(id);if(!it){try{it=await api('item',{id})}catch{}}if(it&&it.id){items.set(it.id,it);openDetail(it)}else toast('Paylaşılan eşya artık kayıtlarda bulunamadı.')}
init();
})();
