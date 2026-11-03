(()=>{
  document.getElementById('bf-type')?.addEventListener('change',e=>{if(e.target.value!=='')setTimeout(()=>document.getElementById('bf-val')?.focus(),60)});

  const subIcon=(ctx,s)=>{const f=window.M2SubIcons?.[ctx.category+':'+s];return f?'/assets/items/'+encodeURIComponent(f):ctx.catIcon(ctx.category)};
  const elementChips=ctx=>{
    const id=ctx.state.filters?.element,el=ctx.ELEMENTS?.[id];
    document.querySelectorAll('#applied .chip').forEach(chip=>{
      if(chip.dataset.elDone)return;
      if(!el||!/^Element:/.test(chip.textContent.trim()))return;
      chip.dataset.elDone='1';chip.classList.add('el-chip');chip.style.setProperty('--c',el[1]);
      const im=document.createElement('img');im.className='el-ico';im.alt='';im.src='/assets/'+el[2]+'.png';chip.prepend(im);
    });
  };
  window.M2Hooks={...(window.M2Hooks||{}),applied:elementChips,subs(ctx){
    document.querySelectorAll('#subs .sub').forEach(btn=>{
      if(btn.querySelector('.sub-ico'))return;
      const s=btn.dataset.sub,im=document.createElement('img');im.className='sub-ico';im.alt='';im.loading='lazy';
      im.onerror=()=>{im.onerror=null;im.src=ctx.catIcon(ctx.category)};
      btn.prepend(im);
      im.src=ctx.catIcon(ctx.category);
      if(s==='')return;
      im.src=subIcon(ctx,s);
    });
  }};
})();
