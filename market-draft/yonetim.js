(()=>{
const CATS={1:'Binek / Pet',2:'Silah',3:'Ekipman',4:'Mücevher',5:'Geliştirme',6:'İksir',7:'Balıkçılık / Madencilik',8:'Simya',9:'Kostüm',10:'Beceri',11:'Özel eşya',12:'Diğer'};
const nf=new Intl.NumberFormat('tr-TR');let days=7,last=null;
const el=(t,c,h)=>{const e=document.createElement(t);if(c)e.className=c;if(h!=null)e.textContent=h;return e};
const sum=(arr,k)=>arr.reduce((n,d)=>n+(d[k]||0),0);
function merge(arr,k){const o={};for(const d of arr)for(const [a,b] of Object.entries(d[k]||{}))o[a]=(o[a]||0)+b;return o}
function top(o,n=10){return Object.entries(o).sort((a,b)=>b[1]-a[1]).slice(0,n)}
function ago(iso){const p=Date.parse(iso);if(!iso||Number.isNaN(p))return '—';const s=Math.max(0,(Date.now()-p)/1000);return s<90?Math.round(s)+' sn önce':s<5400?Math.round(s/60)+' dk önce':s<172800?Math.round(s/3600)+' sa önce':Math.round(s/86400)+' gün önce'}
function card(title,...kids){const c=el('div','card');c.append(el('h2',null,title),...kids);return c}
function big(v,sub){const d=el('div');d.append(el('div','big',v),el('div','sub',sub));return d}
function list(rows,label=n=>n,empty='Henüz veri yok'){if(!rows.length)return el('div','empty',empty);const t=el('table');for(const [k,v] of rows){const r=el('tr');r.append(el('td',null,label(k)),el('td',null,nf.format(v)));t.append(r)}return t}
function bars(vals,tip){const w=el('div','bars'),max=Math.max(1,...vals);vals.forEach((v,i)=>{const b=el('i');b.style.height=Math.max(2,Math.round(v/max*100))+'%';b.dataset.tip=tip(i,v);w.append(b)});return w}
function render(){if(!last)return;const d=last,list30=d.days,slice=list30.slice(-days),today=list30.at(-1)||{},app=document.getElementById('app');app.replaceChildren();
 document.getElementById('upd').textContent='güncelleme '+new Date(d.now).toLocaleTimeString('tr-TR',{hour:'2-digit',minute:'2-digit',second:'2-digit'});
 const mm=d.maintenance||{},mc=el('div','card');mc.append(el('h2',null,'Bakım modu'));const mrow=el('div','mrow'),state=el('span',null,mm.on?'AÇIK, ziyaretçiler bakım sayfasını görüyor':'Kapalı, site normal çalışıyor');state.style.fontWeight='600';state.style.color=mm.on?'var(--warn)':'var(--ok)';
 if(mm.on){const b=el('button','btn ok','Bakım modunu kapat');b.onclick=()=>setMaint(false);mrow.append(state,b)}else{const inp=el('input');inp.placeholder='İsteğe bağlı mesaj (örn. 15 dakikaya döneceğiz)';inp.maxLength=150;const b=el('button','btn warn','Bakım moduna al');b.onclick=()=>{if(confirm('Site ziyaretçilere kapatılacak ve bakım sayfası gösterilecek. Devam edilsin mi?'))setMaint(true,inp.value)};mrow.append(state,inp,b)}
 mc.append(mrow,el('div','sub','Açıkken yalnızca bu yönetim sayfası ve sağlık denetimi çalışır; tarayıcıdan gelen veri akışı durmaz. Sunucunun kendisinden (SSH tüneli) siteyi görmeye devam edersiniz.'));app.append(mc);
 const h=d.health||{},up=h.ready,dataOk=(d.servers||[]).some(s=>s.available);
 const status=el('div','grid');
 const st=el('div');st.append(el('div','big'),el('div','sub'));st.firstChild.append(Object.assign(el('span','dot '+(up?'ok':'bad')),{}),document.createTextNode(up?'Çalışıyor':'Sorun var'));st.lastChild.textContent='açık kalma '+((h.uptimeSeconds||0)<3600?Math.round((h.uptimeSeconds||0)/60)+' dk':Math.round((h.uptimeSeconds||0)/3600*10)/10+' sa')+' · bellek '+Math.round((h.memoryRSS||0)/1048576)+' MB';
 status.append(card('Site',st),
  card('Disk',big((h.disk?.freeGB??'—')+' GB','boş · %'+(h.disk?.usedPercent??'—')+' dolu')),
  card('Canlı bağlantı',big(nf.format(d.events||0),'açık canlı güncelleme bağlantısı')),
  card('Veri akışı',big(dataOk?'Geliyor':'Yok',(d.servers||[]).filter(s=>s.available).map(s=>s.name+' '+ago(s.lastSeen)).join(' · ')||'hiçbir sunucuda veri yok')));
 const vis=el('div','grid');
 vis.append(card('Bugün ziyaretçi',big(nf.format(today.visitors||0),nf.format(today.views||0)+' sayfa görüntüleme')),
  card('Bugün arama',big(nf.format(today.searches||0),nf.format(today.itemOpens||0)+' ilan açıldı')),
  card(days===1?'Bugün':'Son '+days+' gün',big(nf.format(sum(slice,'visitors')),'ziyaretçi toplamı (günlük tekil)')),
  card('Bot / tarayıcı',big(nf.format(sum(slice,'botViews')),'arama motoru ve otomatik istek')));
 app.append(status,vis);
 const dayBars=card('Günlük ziyaretçi (son '+list30.length+' gün)',bars(list30.map(x=>x.visitors||0),(i,v)=>list30[i].day.slice(5)+': '+v+' ziyaretçi'),Object.assign(el('div','axis'),{}));
 dayBars.lastChild.append(el('span',null,list30[0]?.day.slice(5)||''),el('span',null,list30.at(-1)?.day.slice(5)||''));
 const hourBars=card('Saatlere göre (bugün, İstanbul)',bars(today.hours||Array(24).fill(0),(i,v)=>String(i).padStart(2,'0')+':00 → '+v),el('div','axis'));
 hourBars.lastChild.append(el('span',null,'00'),el('span',null,'12'),el('span',null,'23'));
 const c1=el('div','cols');c1.append(dayBars,hourBars);app.append(c1);
 const c2=el('div','cols');
 c2.append(card('En çok aranan',list(top(merge(slice,'terms'),15))),
  card('Kategoriler',list(top(merge(slice,'categories'),12),k=>CATS[k]||'Kategori '+k)),
  card('Sunucular',list(top(merge(slice,'servers'),12))));
 app.append(c2);
 const c3=el('div','cols');
 c3.append(card('Ülkeler',list(top(merge(slice,'countries'),10),k=>k==='??'?'Bilinmiyor':k)),
  card('Cihaz',list(top(merge(slice,'devices'),6))),
  card('Nereden geldiler',list(top(merge(slice,'referrers'),10))),
  card('Sayfalar',list(top(merge(slice,'pages'),10))));
 app.append(c3);
 const g=d.guard||{},c4=el('div','cols');
 c4.append(card('Koruma (sunucu açıldığından beri)',list([['İzin verilen',g.allowed||0],['Hız sınırına takılan',g.limited||0],['Geçici engellenen',g.blocked||0],['Reddedilen (geçersiz istek)',g.denied||0],['Doğrulanan ziyaretçi',g.verified||0]])),
  card('API istekleri (açıldığından beri)',(()=>{const rows=Object.entries(d.routes||{}).sort((a,b)=>b[1].requests-a[1].requests).slice(0,10);if(!rows.length)return el('div','empty','Henüz istek yok');const t=el('table'),hd=el('tr');['Uç nokta','İstek','Ort. ms','Hata'].forEach(x=>hd.append(el('th',null,x)));t.append(hd);for(const [k,m] of rows){const r=el('tr');r.append(el('td',null,k.replace('/api/','')),el('td',null,nf.format(m.requests)),el('td',null,String(Math.round(m.totalMs/Math.max(1,m.requests)))),el('td',null,nf.format(m.errors||0)));t.append(r)}return t})()));
 app.append(c4)}
async function setMaint(on,message=''){try{const r=await fetch('/api/yonetim/bakim?set='+(on?'on':'off')+(on&&message?'&message='+encodeURIComponent(message):''),{headers:{'X-Yonetim':'1'},cache:'no-store'});if(!r.ok)throw 0;await load()}catch{alert('Bakım modu değiştirilemedi.')}}
async function load(){try{const r=await fetch('/api/yonetim?days=30',{cache:'no-store'});if(!r.ok)throw 0;last=await r.json();render()}catch{document.getElementById('app').replaceChildren(el('div','empty','Veri alınamadı. Yetkiniz olmayabilir ya da site yanıt vermiyor.'))}}
document.getElementById('range').addEventListener('click',e=>{const b=e.target.closest('button');if(!b)return;days=Number(b.dataset.d);document.querySelectorAll('#range button').forEach(x=>x.classList.toggle('on',x===b));render()});
load();setInterval(load,30000);
})();
