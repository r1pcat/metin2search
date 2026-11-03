(()=>{
const KEY='m2s-cookie',PREF_KEYS=/^(?:m2s-v3-|m2search-|m2-home-)/;
const read=()=>{try{const v=JSON.parse(localStorage.getItem(KEY));return v&&v.v===1?v:null}catch{return null}};
const save=v=>{try{localStorage.setItem(KEY,JSON.stringify({v:1,prefs:!!v.prefs,ads:!!v.ads,at:new Date().toISOString()}))}catch{}};
let state=read();
const api=window.M2Consent={allows(cat){if(cat==='necessary')return true;if(!state)return cat==='prefs';return !!state[cat]},open:()=>openDialog(),get choice(){return state}};
function wipe(){try{for(const k of Object.keys(localStorage))if(PREF_KEYS.test(k))localStorage.removeItem(k)}catch{}}
function apply(v){save(v);state=read()||{prefs:!!v.prefs,ads:!!v.ads};if(!state.prefs)wipe();banner.hidden=true;document.dispatchEvent(new CustomEvent('m2:consent',{detail:state}))}
const el=(html)=>{const t=document.createElement('template');t.innerHTML=html.trim();return t.content.firstElementChild};
const banner=el(`<div class="ck-banner" role="dialog" aria-label="Çerez tercihleri" hidden><strong>Çerez tercihleri</strong><p>Kaydettiğin eşyalar ve görünüm seçimin gibi kolaylıklar için tarayıcı depolamasını kullanırız. Şu anda reklam veya takip çerezi kullanmıyoruz. Ayrıntılar için <a href="/cerez-politikasi">Çerez Politikası</a>’na bakabilirsin.</p><div class="ck-actions"><button type="button" class="ck-btn primary" data-a="all">Tümünü kabul et</button><button type="button" class="ck-btn" data-a="min">Yalnızca gerekli</button><button type="button" class="ck-btn" data-a="set">Ayarlar</button></div></div>`);
const dlg=el(`<dialog class="ck-dialog" aria-labelledby="ck-h"><form method="dialog"><header><h2 id="ck-h">Çerez ayarları</h2><p>Hangi türlerin kullanılacağını sen seçersin. Seçimini sayfa altındaki “Çerez ayarları” bağlantısından istediğin zaman değiştirebilirsin.</p></header><div class="ck-opts">
<label class="ck-opt"><b>Zorunlu</b><input type="checkbox" checked disabled><small>Çerez tercihinin kendisini hatırlamak için gerekir. Kapatılamaz.</small></label>
<label class="ck-opt"><b>Tercihler</b><input type="checkbox" id="ck-prefs"><small>Kaydedilen eşyalar, karşılaştırma listesi, liste/kart görünümü ve ana sayfa hareket seçimi. Yalnızca cihazında kalır.</small></label>
<label class="ck-opt"><b>Reklam ve ölçümleme</b><input type="checkbox" id="ck-ads"><small>Şu anda kullanılmıyor. İleride eklenirse yalnızca onayınla çalışır.</small></label></div>
<footer><button type="button" class="ck-btn" data-a="cancel">Vazgeç</button><button type="button" class="ck-btn primary" data-a="save">Seçimi kaydet</button></footer></form></dialog>`);
function openDialog(){dlg.querySelector('#ck-prefs').checked=api.allows('prefs');dlg.querySelector('#ck-ads').checked=api.allows('ads');dlg.showModal?dlg.showModal():dlg.setAttribute('open','')}
banner.addEventListener('click',e=>{const a=e.target.closest('[data-a]')?.dataset.a;if(a==='all')apply({prefs:true,ads:true});else if(a==='min')apply({prefs:false,ads:false});else if(a==='set')openDialog()});
dlg.addEventListener('click',e=>{const a=e.target.closest('[data-a]')?.dataset.a;if(a==='cancel')dlg.close();else if(a==='save'){apply({prefs:dlg.querySelector('#ck-prefs').checked,ads:dlg.querySelector('#ck-ads').checked});dlg.close()}});
document.addEventListener('click',e=>{if(e.target.closest('[data-cookie-settings]')){e.preventDefault();openDialog()}});
const mount=()=>{document.body.append(banner,dlg);banner.hidden=!!state};
document.readyState==='loading'?document.addEventListener('DOMContentLoaded',mount):mount();
})();