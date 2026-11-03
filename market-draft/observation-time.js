function observationInfo(value,now=Date.now()){
 const stamp=Date.parse(value),valid=Number.isFinite(stamp);
 if(!valid)return {label:'Gözlem zamanı bilinmiyor',exact:'Gözlem zamanı bilinmiyor',old:false};
 const age=Math.max(0,now-stamp),minutes=Math.floor(age/60000),hours=Math.floor(age/3600000),days=Math.floor(age/86400000);
 return {label:age<60000?'Az önce görüldü':hours<1?minutes+' dk önce görüldü':days<1?hours+' saat önce görüldü':days+' gün önce görüldü',exact:new Date(stamp).toLocaleString('tr-TR',{timeZone:'Europe/Istanbul',day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit'}),old:age>=86400000};
}
function observationStamp(value){const info=observationInfo(value);return `<time class="item-observed${info.old?' older-observation':''}" data-observed="${esc(value||'')}" title="${esc(info.exact)} · Satılmış veya kaldırılmış olabilir.">${esc(info.label)}${info.old?'<span class="observation-old-mark" aria-label="Eski gözlem">◷</span>':''}</time>`}
if(typeof module!=='undefined'&&module.exports)module.exports={observationInfo};
