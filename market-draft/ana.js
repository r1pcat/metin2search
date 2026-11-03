(()=>{
const $=id=>document.getElementById(id);
const nf=new Intl.NumberFormat('tr-TR'),nf2=new Intl.NumberFormat('tr-TR',{maximumFractionDigits:2});
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const big=v=>v>=1e9?nf2.format(v/1e9)+'T':v>=1e6?nf2.format(v/1e6)+'M':v>=1e3?nf2.format(v/1e3)+'K':nf.format(Math.round(v));
const short=big;
const rtf=new Intl.RelativeTimeFormat('tr',{numeric:'auto'});
const ago=iso=>{if(!iso)return'—';const s=(new Date(iso)-Date.now())/1000,a=Math.abs(s);return a<3600?rtf.format(Math.round(s/60),'minute'):a<86400?rtf.format(Math.round(s/3600),'hour'):rtf.format(Math.round(s/86400),'day')};
const when=t=>new Date(t).toLocaleString('tr-TR',{timeZone:'Europe/Istanbul',day:'2-digit',month:'long',hour:'2-digit',minute:'2-digit'});
const SERIES=[{key:'won',label:'Won',color:'#69c8f5',fmt:v=>nf.format(Math.round(v))+' Won',axis:v=>nf.format(Math.round(v))},
  {key:'yang',label:'Yang',color:'#edc653',fmt:v=>big(v)+' Yang',axis:short},
  {key:'listings',label:'İlanlar',color:'#b795ef',fmt:v=>nf.format(Math.round(v))+' ilan',axis:v=>nf.format(Math.round(v))},
  {key:'sellers',label:'Satıcılar',color:'#86aaf3',fmt:v=>nf.format(Math.round(v))+' satıcı',axis:v=>nf.format(Math.round(v))}];
const SHARE=['#c9a45e','#7fa6cf','#9c8fd0','#6fb58c','#c47f6a','#8fb3a8','#b3a26f'];
let data=null,server=null,key='won',days=1;

function renderVolume(){
  const ready=data.servers.filter(s=>s.available);$('v-won').textContent=nf.format(data.won||0);$('v-list').textContent=nf.format(data.listings||0);
  $('v-sell').textContent=nf.format(data.sellerRecords||0);$('v-ready').textContent=`${data.available||ready.length} / ${data.servers.length}`;
  const tot=ready.reduce((s,x)=>s+(x.valueWon||x.won||0),0);
  $('v-share').innerHTML=ready.length&&tot?`<div class="share-bar">${ready.map((s,i)=>`<i style="width:${(s.valueWon||s.won)/tot*100}%;background:${SHARE[i%SHARE.length]}" title="${esc(s.name)}"></i>`).join('')}</div>
    <div class="share-legend">${ready.map((s,i)=>`<span><i style="background:${SHARE[i%SHARE.length]}"></i>${esc(s.name)} <b>%${nf2.format((s.valueWon||s.won)/tot*100)}</b> · ${nf.format(s.won||0)} Won</span>`).join('')}</div>`:'';
}

function points(){const all=(data.history||[]).filter(p=>p.server===server).sort((a,b)=>new Date(a.at)-new Date(b.at));if(!all.length)return all;
  const end=new Date(all.at(-1).at).getTime(),r=all.filter(p=>new Date(p.at).getTime()>=end-days*864e5);return r.length>1?r:all}
function renderServerSelect(){const ready=data.servers.filter(s=>s.available);if(!server||!ready.some(s=>s.id===server))server=(ready.find(s=>s.id==='safir')||ready[0])?.id||null;
  const hs=$('h-server'),hv=hs.value;hs.innerHTML=ready.map(s=>`<option value="${esc(s.id)}">${esc(s.name)}</option>`).join('')||'<option value="safir">Safir</option>';if(ready.some(s=>s.id===hv))hs.value=hv;else if(ready.some(s=>s.id==='safir'))hs.value='safir';
  $('p-server').innerHTML=ready.map(s=>`<option value="${esc(s.id)}"${s.id===server?' selected':''}>${esc(s.name)}</option>`).join('')||'<option>Veri bekleniyor</option>'}
function renderMetrics(){const s=data.servers.find(x=>x.id===server)||{},p=points();
  $('metrics').innerHTML=SERIES.map(d=>{const a=p[0]?.[d.key],b=p.at(-1)?.[d.key],ch=a?((b-a)/a*100):null;
    return `<button type="button" class="metric" data-key="${d.key}" style="--c:${d.color}" aria-pressed="${d.key===key}"><span class="cap"><i></i>${d.label}</span><strong>${d.key==='yang'?esc(big(s.yang||0)):nf.format(s[d.key]||0)}</strong>
      <small>${ch==null?'Kayıt bekleniyor':`<b class="${ch>0?'up':ch<0?'down':''}">${ch>0?'+':''}${nf2.format(ch)}%</b> · ${days===1?'son 24 saat':days===7?'son 7 gün':'son 30 gün'}`}</small></button>`}).join('')}
function tangents(c){if(c.length<2)return[0];const m=c.slice(1).map((p,i)=>(p.y-c[i].y)/Math.max(1e-6,p.x-c[i].x));return c.map((p,i)=>{if(!i)return m[0];if(i===c.length-1)return m[i-1];const a=m[i-1],b=m[i];if(a*b<=0)return 0;const h0=p.x-c[i-1].x,h1=c[i+1].x-p.x,w0=2*h1+h0,w1=h1+2*h0;return (w0+w1)/(w0/a+w1/b)})}
function curve(c){if(!c.length)return'';const t=tangents(c),n=v=>v.toFixed(2);return `M${n(c[0].x)},${n(c[0].y)}`+c.slice(1).map((p,i)=>{const a=c[i],h=(p.x-a.x)/3;return ` C${n(a.x+h)},${n(a.y+h*t[i])} ${n(p.x-h)},${n(p.y-h*t[i+1])} ${n(p.x)},${n(p.y)}`}).join('')}
function renderChart(){
  const p=points(),el=$('chart'),d=SERIES.find(s=>s.key===key);$('c-samples').textContent=p.length?`${nf.format(p.length)} gözlem noktası`:'';
  if(p.length<2){el.innerHTML='<div class="empty" style="height:100%;display:grid;place-items:center">Bu sunucu için grafik verisi henüz yok.</div>';$('r-val').textContent='—';return}
  const W=1000,H=280,L=70,R=12,T=14,B=28,t0=new Date(p[0].at).getTime(),t1=new Date(p.at(-1).at).getTime();
  const X=t=>L+(t-t0)/((t1-t0)||1)*(W-L-R);
  const scale=k=>{const v=p.map(x=>x[k]),mn=Math.min(...v),mx=Math.max(...v),pad=(mx-mn)*.1||Math.abs(mx)*.05||1;return {lo:mn-pad,hi:mx+pad}};
  const sc=Object.fromEntries(SERIES.map(s=>[s.key,scale(s.key)])),Y=(k,v)=>T+(1-(v-sc[k].lo)/(sc[k].hi-sc[k].lo))*(H-T-B);
  const pts=Object.fromEntries(SERIES.map(s=>[s.key,p.map(x=>({x:X(new Date(x.at).getTime()),y:Y(s.key,x[s.key])}))]));
  const ticks=[0,.25,.5,.75,1].map(f=>{const v=sc[key].lo+(sc[key].hi-sc[key].lo)*f,y=Y(key,v);return `<line x1="${L}" x2="${W-R}" y1="${y}" y2="${y}"/><text x="${L-10}" y="${y+4}" text-anchor="end">${esc(d.axis(v))}</text>`}).join('');
  const fmtT=t=>new Date(t).toLocaleString('tr-TR',{timeZone:'Europe/Istanbul',...(days===1?{hour:'2-digit',minute:'2-digit'}:{day:'2-digit',month:'short'})});
  const xt=[0,1/3,2/3,1].map(f=>{const t=t0+(t1-t0)*f;return `<text x="${X(t)}" y="${H-8}" text-anchor="${f===0?'start':f===1?'end':'middle'}">${esc(fmtT(t))}</text>`}).join('');
  const main=pts[key],area=curve(main)+` L${main.at(-1).x},${H-B} L${main[0].x},${H-B} Z`;
  el.innerHTML=`<svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none"><defs><linearGradient id="ar" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="${d.color}" stop-opacity=".18"/><stop offset="1" stop-color="${d.color}" stop-opacity="0"/></linearGradient></defs>
    <g class="grid">${ticks}</g>${xt}${SERIES.filter(s=>s.key!==key).map(s=>`<path class="other" d="${curve(pts[s.key])}" stroke="${s.color}"/>`).join('')}
    <path d="${area}" fill="url(#ar)"/><path class="main" d="${curve(main)}" stroke="${d.color}"/>
    <line class="cross" id="cx" y1="${T}" y2="${H-B}"/>${SERIES.map(s=>`<circle class="dot" id="dt-${s.key}" r="${s.key===key?4.5:3}" fill="${s.color}" ${s.key===key?'':'opacity=".6"'}/>`).join('')}</svg><div class="chart-tip" id="tip" hidden></div>`;
  const svg=el.querySelector('svg'),tip=$('tip');
  const show=(i,hover)=>{const x=main[i].x;$('cx').setAttribute('x1',x);$('cx').setAttribute('x2',x);for(const s of SERIES){const c=$('dt-'+s.key);c.setAttribute('cx',x);c.setAttribute('cy',pts[s.key][i].y)}
    $('r-cap').textContent=d.label+(hover?' · seçili zaman':' · son gözlem');$('r-val').textContent=d.fmt(p[i][key]);$('r-time').textContent=when(p[i].at);
    if(!hover){tip.hidden=true;return}tip.innerHTML=`<time>${esc(when(p[i].at))}</time>`+SERIES.map(s=>`<div class="${s.key===key?'on':''}"><span><i style="background:${s.color}"></i>${s.label}</span><b>${esc(s.key==='yang'?big(p[i].yang):nf.format(p[i][s.key]))}</b></div>`).join('');tip.hidden=false;
    const r=svg.getBoundingClientRect(),px=x/W*r.width;tip.style.left=Math.min(r.width-tip.offsetWidth,Math.max(0,px+14))+'px';tip.style.top='10px';if(px+14+tip.offsetWidth>r.width)tip.style.left=Math.max(0,px-14-tip.offsetWidth)+'px'};
  show(p.length-1,false);
  svg.onmousemove=e=>{const r=svg.getBoundingClientRect(),x=(e.clientX-r.left)/r.width*W;let b=0;for(let i=1;i<main.length;i++)if(Math.abs(main[i].x-x)<Math.abs(main[b].x-x))b=i;show(b,true)};
  svg.onmouseleave=()=>show(p.length-1,false);
}

function renderServers(){
  const q=$('srv-q').value.trim().toLocaleLowerCase('tr'),list=data.servers.filter(s=>s.name.toLocaleLowerCase('tr').includes(q)).map((s,i)=>({s,i})).sort((x,y)=>Number(y.s.available)-Number(x.s.available)||(x.s.available?(y.s.listings||0)-(x.s.listings||0):0)||x.i-y.i).map(x=>x.s);
  $('srv-state').textContent=data.available?`${data.available} sunucunun kayıtlarını inceleyebilirsin. Diğer sunucular için veri toplanıyor.`:'Sunucular için ilk kayıtlar bekleniyor.';
  const status={ready:'Kayıtlar güncel',stale:'Gözlem eski',paused:'Aktarım durdu',issue:'Kontrol gerekli'},regions={sapphire:['Sapphire','#5aa7f5'],dia:['Dia','#d8ecff'],ruby:['Ruby','#e65b4e'],'türkiye':['Türkiye','#d3ad63']};
  $('srv-grid').innerHTML=list.map(s=>{const tag=s.available?'a':'article',href=s.available?` href="/pazar?server=${encodeURIComponent(s.id)}"`:'',reg=regions[String(s.group||'Türkiye').replace(/İ/g,'i').toLowerCase()]||[String(s.group),'#d3ad63'];
    return `<${tag} class="srv ${s.available?'ready':'wait'}"${href}><div class="srv-id"><span class="srv-glyph">${ic('shield')}</span><div><h3>${esc(s.name)}</h3><span class="srv-region" style="--rc:${reg[1]}"><i></i>${esc(reg[0])}</span></div></div>
      ${s.available?`<div class="srv-stats">${[['won','Won'],['yang','Yang'],['listings','İlan'],['sellers','Satıcı']].map(([k,l])=>`<div><strong title="${nf.format(s[k]||0)}">${esc(short(s[k]||0))}</strong><small>${l}</small></div>`).join('')}</div>`:'<div class="srv-wait">Veri toplama bekleniyor</div>'}
      <div class="srv-side"><span class="srv-status ${esc(s.status||'')}"><i></i>${s.available?esc(status[s.status]||'Kayıtlar hazır'):'Henüz kayıt yok'}</span>${s.available?`<small>Son gözlem · ${esc(ago(s.lastSeen))}</small><b>Pazara gir ${ic('arrow')}</b>`:''}</div></${tag}>`}).join('')||'<div class="empty">Bu isimde bir sunucu bulunamadı.</div>';
}

function render(){renderVolume();renderServerSelect();renderMetrics();renderChart();renderServers()}
async function load(){try{data=await fetch('/api/home').then(r=>{if(!r.ok)throw r.status;return r.json()});render()}catch{$('srv-state').textContent='Sunucu bilgileri şu an yüklenemiyor. Birkaç saniye sonra sayfayı yenileyin.'}}
document.addEventListener('click',e=>{const m=e.target.closest('[data-key]');if(m){key=m.dataset.key;renderMetrics();renderChart();return}
  const d=e.target.closest('[data-days]');if(d){days=Number(d.dataset.days);document.querySelectorAll('[data-days]').forEach(b=>b.setAttribute('aria-pressed',b===d));renderMetrics();renderChart()}});
$('p-server').addEventListener('change',e=>{server=e.target.value;renderMetrics();renderChart()});
$('srv-q').addEventListener('input',()=>data&&renderServers());
addEventListener('resize',()=>data&&renderChart());

let moving=true;try{moving=localStorage.getItem('m2-home-motion')!=='off'}catch{}
function setMotion(){document.body.classList.toggle('hero-motion',moving);$('motion-btn').textContent=moving?'Ⅱ Hareketi durdur':'▷ Hareketi aç';$('motion-btn').setAttribute('aria-pressed',String(moving))}
$('motion-btn').addEventListener('click',()=>{moving=!moving;try{if(!window.M2Consent||window.M2Consent.allows('prefs'))localStorage.setItem('m2-home-motion',moving?'on':'off')}catch{}setMotion()});setMotion();
load();setInterval(()=>{if(!document.hidden)load()},60000);
})();
