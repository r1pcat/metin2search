(()=>{
const DISCORD='https://discord.gg/';
const ADS=[
  {id:'discord',alt:'Öneri ve görüşlerinizi bize iletmek için Discord adresimize katılın',href:DISCORD,external:true,sky:'/assets/ads/discord-sky.svg',wide:'/assets/ads/discord-wide.svg',compact:'/assets/ads/discord-compact.svg'},
  {id:'pazar',alt:'Doğru fiyatı bil, doğru alışveriş yap: fiyat geçmişi, karşılaştırma ve efsun filtreleri tek yerde',href:'/pazar',sky:'/assets/ads/pazar-sky.svg',wide:'/assets/ads/pazar-wide.svg',compact:'/assets/ads/pazar-compact.svg'},
  {id:'fake',alt:'Fake pazarlara dikkat: dükkanı hazır olmayan satıcıları FAKE etiketiyle işaretliyoruz. Pazar kurmadan önce kontrol edin.',href:'/veri-kaynagi',sky:'/assets/ads/fake-sky.svg?v=2',wide:'/assets/ads/fake-wide.svg?v=2',compact:'/assets/ads/fake-compact.svg?v=2'}
];
let turn=0;
const esc=s=>String(s).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
function fill(slot){
  if(slot.dataset.ad)return;const ad=ADS[turn++%ADS.length],sky=slot.classList.contains('sky');slot.dataset.ad=ad.id;
  slot.innerHTML=`<a class="ad-link" href="${esc(ad.href)}"${ad.external?' target="_blank" rel="noopener noreferrer sponsored"':' rel="sponsored"'} aria-label="${esc(ad.alt)}"><picture>${sky?'':`<source media="(max-width:640px)" srcset="${esc(ad.compact)}">`}<img src="${esc(sky?ad.sky:ad.wide)}" alt="${esc(ad.alt)}" loading="lazy" decoding="async"></picture></a>`;
}
const scan=root=>{if(root.matches?.('.ad-slot'))fill(root);root.querySelectorAll?.('.ad-slot').forEach(fill)};
const start=()=>{scan(document);new MutationObserver(ms=>{for(const m of ms)for(const n of m.addedNodes)if(n.nodeType===1)scan(n)}).observe(document.body,{childList:true,subtree:true})};
document.readyState==='loading'?document.addEventListener('DOMContentLoaded',start):start();
const POPUP={key:'m2s-popup',every:2*60*60*1000,delay:2500,href:DISCORD};
function popupDue(){try{const t=Number(localStorage.getItem(POPUP.key));return !(t&&Date.now()-t>=0&&Date.now()-t<POPUP.every)}catch{return false}}
function showPopup(){
  if(!popupDue()||document.querySelector('.ad-pop-wrap'))return;try{localStorage.setItem(POPUP.key,String(Date.now()))}catch{}
  const wrap=document.createElement('div');wrap.className='ad-pop-wrap';const el=document.createElement('aside');el.className='ad-pop';el.setAttribute('role','dialog');el.setAttribute('aria-modal','true');el.setAttribute('aria-label','Metin2Search duyurusu');wrap.append(el);
  el.innerHTML=`<button type="button" class="ad-pop-x" aria-label="Kapat">×</button><div class="ad-pop-head"><span class="ad-pop-ico"><img src="/assets/discord-symbol.svg" alt=""></span><div><small>Metin2Search · Beta</small><strong>Birlikte geliştiriyoruz</strong></div></div><p>Metin2Search Ticaret Camı şu anda <b>beta</b> aşamasında ve sürekli geliştiriliyor. Daha iyi bir deneyim için <b>Discord kanalımıza</b> katılın; hata ve önerilerinizi bize bildirin.</p><p class="ad-pop-sub">Geri dönüşlerinizi dikkate alarak siteyi geliştirmeye ve daha iyi hale getirmeye devam edeceğiz.</p><a class="ad-pop-cta" href="${POPUP.href}" target="_blank" rel="noopener noreferrer">Discord’a katıl</a><span class="ad-pop-tag">M2Search</span>`;
  const close=()=>{wrap.classList.remove('in');setTimeout(()=>wrap.remove(),300);document.removeEventListener('keydown',onKey)},onKey=e=>{if(e.key==='Escape')close()};
  el.querySelector('.ad-pop-x').addEventListener('click',close);el.querySelector('.ad-pop-cta').addEventListener('click',()=>setTimeout(close,50));document.addEventListener('keydown',onKey);
  wrap.addEventListener('mousedown',e=>{if(e.target===wrap)close()});document.body.append(wrap);el.querySelector('.ad-pop-cta').focus({preventScroll:true});requestAnimationFrame(()=>requestAnimationFrame(()=>wrap.classList.add('in')));
}
const popupStart=()=>setTimeout(()=>{const pending=document.querySelector('.ck-banner:not([hidden])');if(pending&&innerWidth<=640){document.addEventListener('m2:consent',()=>setTimeout(showPopup,800),{once:true})}else showPopup()},POPUP.delay);
document.readyState==='complete'?popupStart():addEventListener('load',popupStart);
window.M2Ads={list:ADS};
})();