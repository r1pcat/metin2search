(()=>{
const $=s=>document.querySelector(s),$$=s=>[...document.querySelectorAll(s)];
const fire=(el,type)=>el&&el.dispatchEvent(new Event(type,{bubbles:true}));
const setVal=(id,v)=>{const el=document.getElementById(id);if(!el)return;el.value=v;fire(el,'input');fire(el,'change')};

function closeAll(except){$$('.v2-pop-wrap').forEach(w=>{if(w===except)return;w.classList.remove('open');w.querySelector('.v2-pop').hidden=true;w.firstElementChild.setAttribute('aria-expanded','false')})}
document.addEventListener('click',e=>{
  const wrap=e.target.closest('.v2-pop-wrap');
  if(!wrap){closeAll();return}
  if(e.target.closest('.v2-pop'))return;
  const open=wrap.classList.toggle('open');wrap.querySelector('.v2-pop').hidden=!open;wrap.firstElementChild.setAttribute('aria-expanded',String(open));closeAll(open?wrap:null);
  if(open)setTimeout(()=>wrap.querySelector('input')?.focus(),30);
});
document.addEventListener('keydown',e=>{if(e.key==='Escape')closeAll()});

document.addEventListener('click',e=>{const b=e.target.closest('[data-v2-clear]');if(!b)return;for(const k of b.dataset.v2Clear.split(','))setVal('f-'+k,'')});
document.addEventListener('click',e=>{const b=e.target.closest('[data-v2-qty]');if(!b)return;setVal('f-qtyMin',b.dataset.v2Qty);setVal('f-qtyMax',b.dataset.v2Qty)});
document.addEventListener('click',e=>{if(e.target.closest('#v2-seller-go')){const s=document.getElementById('seller');fire(s,'input');fire(s,'change');s.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}))}});
document.addEventListener('click',e=>{if(e.target.closest('#v2-refresh'))fire(document.getElementById('sort'),'change')});

const side=document.getElementById('v2-side');
document.addEventListener('click',e=>{if(e.target.closest('#v2-filter-btn'))side.classList.add('open');if(e.target.closest('#v2-side-x'))side.classList.remove('open')});

(()=>{const $=id=>document.getElementById(id),host=$('v2-bcards'),sel=$('bf-type');if(!host||!sel)return;
let st=null,cards=[],timer=0;
const esc=s=>String(s).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const lab=t=>{const o=[...sel.options].find(o=>o.value===String(t));return o?o.textContent.trim():'Efsun '+t};
const X='<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18"/></svg>';
const entries=()=>{const o=[];for(const c of cards){const a=c.min,b=c.max;if(a!==''&&b!==''&&a===b)o.push({type:c.type,value:Number(a),mode:'eq',group:'any'});else{if(a!=='')o.push({type:c.type,value:Number(a),mode:'gte',group:'any'});if(b!=='')o.push({type:c.type,value:Number(b),mode:'lte',group:'any'})}}return o.slice(0,7)};
const sig=a=>JSON.stringify(a.map(b=>[Number(b.type),Number(b.value),b.mode||'gte']));
const render=()=>{host.innerHTML=cards.map((c,i)=>`<div class="v2-bc" data-i="${i}"><div class="v2-bc-h"><span>${esc(lab(c.type))}</span><button type="button" class="v2-bc-x" data-bc-x="${i}" aria-label="Kaldır">${X}</button></div><div class="v2-rng"><input data-bc="min" type="number" placeholder="Min" value="${esc(c.min)}" aria-label="En az"><i>~</i><input data-bc="max" type="number" placeholder="Max" value="${esc(c.max)}" aria-label="En çok"></div></div>`).join('')};
const fromState=()=>{cards=[];for(const b of st.bonus){let c=cards.find(x=>x.type===Number(b.type));if(!c){c={type:Number(b.type),min:'',max:''};cards.push(c)}const v=String(b.value);if(b.mode==='eq'){c.min=v;c.max=v}else if(b.mode==='lte')c.max=v;else c.min=v}};
const push=()=>{clearTimeout(timer);timer=0;if(!st)return;st.bonus=entries();$('sort').dispatchEvent(new Event('change',{bubbles:true}))};
const later=()=>{clearTimeout(timer);timer=setTimeout(push,450)};
const sync=()=>{if(!st||timer)return;if(sig(st.bonus)!==sig(entries())){fromState();render()}};
if(sel.options[0])sel.options[0].text='Efsun ekle…';window.refreshSelects?.();
const prev=window.M2Hooks?.applied;
window.M2Hooks={...(window.M2Hooks||{}),applied(ctx){prev?.(ctx);if(sel.options[0]&&sel.options[0].text!=='Efsun ekle…'){sel.options[0].text='Efsun ekle…';window.refreshSelects?.()}st=ctx.state;sync()}};
sel.addEventListener('change',()=>{const v=sel.value;if(v==='')return;const t=Number(v);let c=cards.find(x=>x.type===t);if(!c){if(cards.length>=7)return;c={type:t,min:'',max:''};cards.push(c);render()}
  sel.value='';window.refreshSelects?.();setTimeout(()=>host.querySelector(`[data-i="${cards.indexOf(c)}"] input`)?.focus(),40)});
host.addEventListener('input',e=>{const i=e.target.closest('.v2-bc')?.dataset.i,k=e.target.dataset.bc;if(i==null||!k)return;cards[i][k]=e.target.value.trim();later()});
host.addEventListener('keydown',e=>{if(e.key==='Enter'&&e.target.dataset.bc)push()});
host.addEventListener('click',e=>{const x=e.target.closest('[data-bc-x]');if(!x)return;const c=cards.splice(Number(x.dataset.bcX),1)[0];render();if(c&&(c.min!==''||c.max!==''))push()});
})();
(()=>{const ap=document.getElementById('applied'),slot=document.getElementById('v2-applied-slot');if(!ap||!slot)return;const home=ap.parentNode,next=ap.nextSibling,mq=matchMedia('(min-width:901px)');
const place=()=>{if(mq.matches)slot.appendChild(ap);else home.insertBefore(ap,next&&next.parentNode===home?next:null)};mq.addEventListener('change',place);place()})();
(()=>{const g=id=>document.getElementById(id),ids=['v2-wmin','v2-ymin','v2-wmax','v2-ymax'];if(!g('v2-wmin'))return;
const num=id=>{const v=(g(id).value||'').trim().replace(/\./g,'');return v===''||isNaN(Number(v))?null:Number(v)};
const fmt=n=>n==null?'':String(Number(n.toPrecision(12)));
const calc=()=>{const lo=[num('v2-wmin'),num('v2-ymin')==null?null:num('v2-ymin')/1e8].filter(x=>x!=null),hi=[num('v2-wmax'),num('v2-ymax')==null?null:num('v2-ymax')/1e8].filter(x=>x!=null);
  const a=lo.length?Math.max(...lo):null,b=hi.length?Math.min(...hi):null;return [fmt(a),b!==null&&a!==null&&b<a?'':fmt(b)]};
const apply=()=>{const [a,b]=calc();if(g('f-min').value!==a)setVal('f-min',a);if(g('f-max').value!==b)setVal('f-max',b)};
for(const id of ['v2-ymin','v2-ymax']){g(id).addEventListener('input',()=>{const el=g(id),raw=el.value,pos=el.selectionStart??raw.length,before=raw.slice(0,pos).replace(/\D/g,'').length,d=raw.replace(/\D/g,'').replace(/^0+(?=\d)/,''),out=d.replace(/\B(?=(\d{3})+(?!\d))/g,'.');
  if(out!==raw){el.value=out;let n=0,i=0;for(;i<out.length&&n<before;i++)if(/\d/.test(out[i]))n++;try{el.setSelectionRange(i,i)}catch{}}})}
ids.forEach(id=>['input','change'].forEach(t=>g(id).addEventListener(t,apply)));
document.addEventListener('click',e=>{if(e.target.closest('#v2-price-clear')){ids.forEach(id=>g(id).value='');apply()}});
window.__v2YangSync=()=>{if(ids.some(id=>document.activeElement===g(id)))return;const [a,b]=calc(),fa=g('f-min').value,fb=g('f-max').value;
  if(a===fa&&b===fb)return;
  if(fa===''&&fb==='')ids.forEach(id=>g(id).value='');
  else if(ids.every(id=>g(id).value==='')){g('v2-wmin').value=fa;g('v2-wmax').value=fb}};
})();
(()=>{const PAIRS={'v2-wmin':'v2-wmax','v2-ymin':'v2-ymax','f-qtyMin':'f-qtyMax','f-level':'f-levelMax','f-upgradeMin':'f-upgradeMax'},MAXOF=PAIRS,MINOF=Object.fromEntries(Object.entries(PAIRS).map(([a,b])=>[b,a]));
const touched=new Set(),val=el=>{const v=(el.value||'').trim().replace(/\./g,'');return v===''||isNaN(Number(v))?null:Number(v)};
const put=(el,text)=>{if(el.value===text)return;el.value=text;fire(el,'input');fire(el,'change')};
document.addEventListener('input',e=>{const el=e.target;if(!el.id)return;
  if(MAXOF[el.id]){const mx=document.getElementById(MAXOF[el.id]);if(!mx)return;const m=val(el),x=val(mx);
    if(m===null&&x===null)touched.delete(mx.id);
    if(!touched.has(mx.id))put(mx,el.value);else if(m!==null&&x!==null&&x<m)put(mx,el.value)}
  else if(MINOF[el.id]&&e.isTrusted)touched.add(el.id)});
document.addEventListener('change',e=>{const el=e.target,mn=MINOF[el.id]&&document.getElementById(MINOF[el.id]);if(!mn||!e.isTrusted)return;const m=val(mn),x=val(el);if(m!==null&&x!==null&&x<m)put(el,mn.value)},true);
const host=document.getElementById('v2-bcards');
host?.addEventListener('input',e=>{const el=e.target,k=el.dataset.bc,card=el.closest('.v2-bc');if(!k||!card)return;const mn=card.querySelector('[data-bc=min]'),mx=card.querySelector('[data-bc=max]');
  if(k==='min'){const m=val(mn),x=val(mx);if(card.dataset.mt!=='1'||(m!==null&&x!==null&&x<m)){mx.value=mn.value;mx.dispatchEvent(new Event('input',{bubbles:true}))}}
  else if(e.isTrusted)card.dataset.mt=mx.value===''&&val(mn)===null?'':'1'},true);
host?.addEventListener('change',e=>{const el=e.target;if(el.dataset.bc!=='max'||!e.isTrusted)return;const mn=el.closest('.v2-bc').querySelector('[data-bc=min]'),m=val(mn),x=val(el);if(m!==null&&x!==null&&x<m){el.value=mn.value;el.dispatchEvent(new Event('input',{bubbles:true}))}},true);
})();
(()=>{const ls={get:k=>{try{return localStorage.getItem(k)}catch{return null}},set:(k,v)=>{try{localStorage.setItem(k,v)}catch{}}};
let secs={};try{secs=JSON.parse(ls.get('m2v2-sec')||'{}')||{}}catch{}
$$('details[data-sec]').forEach(d=>{const k=d.dataset.sec;if(secs[k]===false)d.open=false;d.addEventListener('toggle',()=>{secs[k]=d.open;ls.set('m2v2-sec',JSON.stringify(secs))})});
const sort=document.getElementById('sort'),saved=ls.get('m2v2-sort');
if(sort&&saved&&!new URLSearchParams(location.search).has('sort')&&sort.value!==saved&&[...sort.options].some(o=>o.value===saved)){sort.value=saved;window.refreshSelects?.();fire(sort,'change')}
sort?.addEventListener('change',()=>ls.set('m2v2-sort',sort.value));
})();
function refresh(){window.__v2YangSync?.();
  const v=id=>(document.getElementById(id)?.value||'').trim();
  const on=(key,yes)=>$(`[data-pop="${key}"]>.v2-ibtn`)?.classList.toggle('on',!!yes);
  on('price',v('f-min')||v('f-max'));on('qty',v('f-qtyMin')||v('f-qtyMax'));on('seller',v('seller'));
  $$('[data-v2-qty]').forEach(b=>b.classList.toggle('on',v('f-qtyMin')===b.dataset.v2Qty&&v('f-qtyMax')===b.dataset.v2Qty));
  const t=$('#special .sp-head strong');const h=document.getElementById('v2-cat-title');if(h)h.textContent=t?t.textContent:'Kategori filtreleri';
}
['input','change'].forEach(t=>document.addEventListener(t,refresh));
setInterval(refresh,700);refresh();
})();

(()=>{const list=document.getElementById('list');if(!list)return;const cache=new Map(),cv=document.createElement('canvas'),cx=cv.getContext('2d',{willReadFrequently:true});cv.width=cv.height=24;
function dominant(img){try{cx.clearRect(0,0,24,24);cx.drawImage(img,0,0,24,24);const d=cx.getImageData(0,0,24,24).data;let r=0,g=0,b=0,w=0;
  for(let i=0;i<d.length;i+=4){const a=d[i+3]/255;if(a<.5)continue;const R=d[i],G=d[i+1],B=d[i+2],mx=Math.max(R,G,B),mn=Math.min(R,G,B),s=mx?(mx-mn)/mx:0;if(mx<45)continue;const k=a*(.15+s*s)*(mx/255);r+=R*k;g+=G*k;b+=B*k;w+=k}
  if(w<.3)return null;let c=[r/w,g/w,b/w];const m=Math.max(...c);if(m<170){const f=170/m;c=c.map(x=>Math.min(255,x*f))}return c.map(Math.round)}catch{return null}}
function tag(row){if(row.dataset.glow)return;const img=row.querySelector('.slot img');if(!img)return;const go=()=>{let c=cache.get(img.src);if(c===undefined){c=dominant(img);cache.set(img.src,c)}row.dataset.glow='1';if(c)row.style.setProperty('--glow',c.join(','))};img.complete&&img.naturalWidth?go():img.addEventListener('load',go,{once:true})}
const scan=()=>list.querySelectorAll('.item:not([data-glow])').forEach(tag);
new MutationObserver(scan).observe(list,{childList:true,subtree:true});scan();
})();