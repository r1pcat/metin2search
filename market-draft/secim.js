(()=>{
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const norm=s=>String(s).toLocaleLowerCase('tr').normalize('NFD').replace(/[̀-ͯ]/g,'').replace(/ı/g,'i');
let open=null;
const menu=document.createElement('div');menu.className='cs-menu';menu.hidden=true;menu.setAttribute('role','listbox');document.body.append(menu);
function label(sel){const o=sel.selectedOptions[0];return o?o.textContent:''}
function enhance(sel){
  if(sel.dataset.cs||sel.multiple)return;sel.dataset.cs='1';
  const btn=document.createElement('button');btn.type='button';btn.className='cs-btn';btn.setAttribute('aria-haspopup','listbox');btn.setAttribute('aria-label',sel.getAttribute('aria-label')||'Seçim');
  btn.innerHTML='<span class="cs-val"></span><svg class="cs-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>';
  sel.classList.add('cs-native');sel.tabIndex=-1;sel.setAttribute('aria-hidden','true');sel.after(btn);sel._cs=btn;
  const sync=()=>{btn.querySelector('.cs-val').textContent=label(sel);btn.classList.toggle('cs-set',sel.value!=='');btn.disabled=sel.disabled};
  sel.addEventListener('change',sync);new MutationObserver(sync).observe(sel,{childList:true,subtree:true,attributes:true,attributeFilter:['disabled']});sync();sel._csSync=sync;
  btn.addEventListener('click',e=>{e.stopPropagation();open===sel?close():show(sel)});
  btn.addEventListener('keydown',e=>{if(['ArrowDown','ArrowUp','Enter',' '].includes(e.key)){e.preventDefault();show(sel)}});
}
function items(sel,q){return [...sel.options].map((o,i)=>({o,i,g:o.parentElement?.tagName==='OPTGROUP'?o.parentElement.label:''})).filter(({o})=>!q||norm(o.textContent).includes(norm(q)))}
function show(sel){
  close();open=sel;const btn=sel._cs,long=sel.options.length>12;btn.classList.add('cs-open');
  menu.innerHTML=(long?'<div class="cs-search"><input type="search" placeholder="Listede ara…" aria-label="Listede ara"></div>':'')+'<div class="cs-list"></div>';
  const list=menu.querySelector('.cs-list'),input=menu.querySelector('input');let active=sel.selectedIndex;
  let lastGroup='';const draw=()=>{lastGroup='';const rows=items(sel,input?.value||'');list.innerHTML=rows.map(({o,i,g})=>(g&&g!==lastGroup?(lastGroup=g,`<div class="cs-grp" role="presentation">${esc(g)}</div>`):'')+`<button type="button" role="option" data-i="${i}" class="cs-opt${i===sel.selectedIndex?' sel':''}${i===active?' act':''}" aria-selected="${i===sel.selectedIndex}">${esc(o.textContent)}</button>`).join('')||'<div class="cs-none">Eşleşen seçenek yok</div>';list.querySelector('.act')?.scrollIntoView({block:'nearest'})};
  draw();menu.hidden=false;place();
  if(input){input.focus();input.addEventListener('input',()=>{const r=items(sel,input.value);active=r[0]?.i??-1;draw()})}
  menu.onkeydown=e=>{const r=items(sel,input?.value||'');let k=r.findIndex(x=>x.i===active);
    if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();k=Math.max(0,Math.min(r.length-1,k+(e.key==='ArrowDown'?1:-1)));active=r[k]?.i;draw()}
    else if(e.key==='Enter'){e.preventDefault();if(active>=0)pick(active)}else if(e.key==='Escape'){close();btn.focus()}else if(e.key==='Tab')close()};
  if(!input)menu.tabIndex=-1,menu.focus({preventScroll:true});
}
function place(){if(!open)return;const r=(open._cs.closest('.field')||open._cs).getBoundingClientRect(),w=Math.max(r.width,200);menu.style.minWidth=w+'px';menu.style.left=Math.min(r.left,innerWidth-w-8)+'px';
  const below=innerHeight-r.bottom,h=Math.min(menu.scrollHeight,340);if(below<h+12&&r.top>below){menu.style.top='';menu.style.bottom=(innerHeight-r.top+6)+'px'}else{menu.style.bottom='';menu.style.top=(r.bottom+6)+'px'}}
function pick(i){const sel=open;if(!sel)return;close();if(sel.selectedIndex!==i){sel.selectedIndex=i;sel.dispatchEvent(new Event('change',{bubbles:true}))}sel._csSync?.();sel._cs.focus({preventScroll:true})}
function close(){if(!open)return;open._cs?.classList.remove('cs-open');open=null;menu.hidden=true}
menu.addEventListener('click',e=>{const b=e.target.closest('[data-i]');if(b)pick(Number(b.dataset.i))});
menu.addEventListener('mousedown',e=>e.stopPropagation());
document.addEventListener('mousedown',e=>{if(open&&!e.target.closest('.cs-btn'))close()});
addEventListener('resize',close);addEventListener('scroll',e=>{if(open&&!menu.contains(e.target))close()},true);
const scan=root=>root.querySelectorAll?.('select').forEach(enhance);
scan(document);new MutationObserver(ms=>{for(const m of ms)for(const n of m.addedNodes)if(n.nodeType===1){n.matches?.('select')?enhance(n):scan(n)}}).observe(document.body,{childList:true,subtree:true});
window.refreshSelects=()=>document.querySelectorAll('select[data-cs]').forEach(s=>s._csSync?.());
})();
