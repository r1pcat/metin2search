(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.SocketDuration=api})(typeof globalThis==='object'?globalThis:this,function(){
const clean=s=>String(s||'').replace(/^Kalan\s+Süre\s*:\s*/i,'').replace(/\s+/g,' ').trim();
function timers(sockets){const seen=new Set();return sockets.filter(s=>s.duration).flatMap(s=>{const value=clean(s.duration),key=s.kind+'|'+s.name+'|'+value;if(!value||seen.has(key))return [];seen.add(key);return [{name:s.name,value,raw:String(s.duration)}]})}
function itemRemaining(item,sockets){const value=item.special?.remaining;return value&&!timers(sockets).some(t=>t.value===clean(value))?value:null}
return {clean,timers,itemRemaining};
});
