(()=>{
  const el=document.getElementById('err-path');
  if(!el)return;
  const path=(location.pathname||'/').slice(0,120);
  if(path&&path!=='/hata-404.html'){el.textContent=path;el.hidden=false}
})();
