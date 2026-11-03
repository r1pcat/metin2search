(()=>{
 const $=id=>document.getElementById(id),escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])),number=n=>new Intl.NumberFormat('tr-TR',{maximumFractionDigits:2}).format(n);
 const compact=(n,metric)=>n>=1e9?number(n/1e9)+(metric==='yang'?'T':'B'):n>=1e6?number(n/1e6)+'M':n>=1e3?number(n/1e3)+'K':number(n),normalize=s=>s.toLocaleLowerCase('tr').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/ı/g,'i');
 const emblem='<svg viewBox="0 0 32 32" width="30" height="30" fill="none" stroke="currentColor" stroke-width="1.2" aria-hidden="true"><path d="m16 2 12 8v12l-12 8L4 22V10zM4 10l12 8 12-8M16 18v12M10 6l6 12 6-12"/></svg>';
 let data=null,loading=false;
 function observed(value){const stamp=Date.parse(value);if(!Number.isFinite(stamp))return 'Gözlem zamanı bekleniyor';const minutes=Math.max(0,Math.floor((Date.now()-stamp)/60000));return 'Son gözlem · '+(minutes<1?'az önce':minutes<60?minutes+' dk önce':minutes<1440?Math.floor(minutes/60)+' saat önce':Math.floor(minutes/1440)+' gün önce')}
 function renderServers(){
  if(!data)return;const query=normalize($('home-server-search').value.trim()),servers=data.servers.filter(s=>normalize(s.name).includes(query)).sort((a,b)=>Number(b.available)-Number(a.available));
  $('home-server-grid').innerHTML=servers.length?servers.map(s=>{
   const statusLabels={ready:'Kayıtlar güncel',stale:'Gözlem eski',paused:'Aktarım durdu',issue:'Kontrol gerekli'},name=escape(s.name),group=escape(s.group||'TÜRKİYE'),statusLabel=s.available?(statusLabels[s.status]||'Kayıtlar hazır'):'Henüz kayıt yok';
   const tag=s.available?'a':'article',link=s.available?` href="/pazar?server=${encodeURIComponent(s.id)}" aria-label="${name} pazarını keşfet"`:'';
   const stamp=s.lastSeen?new Date(s.lastSeen).toLocaleString('tr-TR',{timeZone:'Europe/Istanbul'}):'';
   const stats=[['won','Won'],['yang','Yang'],['listings','İlanlar'],['sellers','Satıcılar']].map(([key,label])=>{const value=Number(s[key]||0);return `<div><strong class="server-${key}" title="${number(value)} ${label}">${compact(value,key)}</strong><small>${label}</small></div>`}).join('');
   return `<${tag} class="home-server-card ${s.available?'server-ready':'server-waiting'}"${link}><div class="server-card-top"><div><span class="server-glyph">${emblem}</span><div><h3>${name}</h3><div class="server-region">${group}</div></div></div><span class="server-card-status status-${escape(s.status||'waiting')}"><i></i>${escape(statusLabel)}</span></div><div class="server-card-stats">${stats}</div><div class="server-card-bottom"><small class="server-time-stack" title="${escape(stamp)}"><span>${s.lastSeen?observed(s.lastSeen):'Son gözlem · Henüz yok'}</span><span>Güncelleme · ${s.updatedAt?escape(new Date(s.updatedAt).toLocaleString('tr-TR',{timeZone:'Europe/Istanbul',day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'})):'Bekleniyor'}</span></small><span class="server-enter">${s.available?'Pazara gir <span aria-hidden="true">↗</span>':'Veri bekleniyor'}</span></div></${tag}>`;
  }).join(''):'<p class="home-server-state">Bu isimle bir sunucu bulunamadı.</p>';
 }
 function render(){
  window.dispatchEvent(new CustomEvent('home-data',{detail:data}));const ready=data.servers.filter(s=>s.available);
  $('home-server-state').textContent=data.available?data.available+' sunucunun kayıtlarını inceleyebilirsin.':'Sunucular için ilk kayıtlar bekleniyor.';
  for(const link of document.querySelectorAll('[data-market-link]'))link.href=ready.length?'/pazar?server='+encodeURIComponent(ready.find(s=>s.id==='safir')?.id||ready[0].id):'#sunucular';
  $('pazar-hacmi').setAttribute('aria-busy','false');$('home-server-grid').setAttribute('aria-busy','false');renderServers();
 }
 async function load(){
  if(loading)return;loading=true;
  try{const r=await fetch('/api/home');if(!r.ok)throw Error();data=await r.json();render()}
  catch{$('home-server-state').innerHTML='Sunucu bilgileri alınamadı. <button type="button" id="home-retry">Tekrar dene ↻</button>';if(!data)$('home-server-grid').innerHTML='';$('pazar-hacmi').setAttribute('aria-busy','false');$('home-server-grid').setAttribute('aria-busy','false')}
  finally{loading=false}
 }
 $('home-server-search').addEventListener('input',renderServers);document.addEventListener('click',e=>{if(e.target.closest('#home-retry'))load()});
 setInterval(()=>{if(!document.hidden)load()},60000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)load()});load();
})();
