let catalogHint={key:''};
function syncCatalogContext(rows=null){
 const key=[$('search').value.trim(),category,sub].join('|');
 if(rows?.length)catalogHint={key,category:rows.every(x=>x.category===rows[0].category)?rows[0].category:null,sub:rows.every(x=>String(x.sub)===String(rows[0].sub))?String(rows[0].sub):null};
 const hint=catalogHint.key===key?catalogHint:{},cat=category||hint.category;
 let forcedFamily='';
 if(cat===1&&['petState','petType','petSpecies','petLevelMin','petLevelMax'].some(id=>$(id).value!=='')){
  const box=(meta?.categoryCatalog?.families||[]).find(f=>f.category===1&&f.group==='Nakliye kutusu');if(box){forcedFamily=box.id;if(category===1&&sub!==''&&Number(sub)!==box.sub){sub=String(box.sub);renderCategories()}}
 }
 const currentSub=category?sub:hint.sub,fish=cat===7&&String(currentSub)==='2';
 $('fish-context').hidden=!fish;
 if(fish){$('catalogFamily').value='';$('catalog-context').hidden=true;const select=$('fishSpecies'),value=select.value,grilled=$('fishGrilled').value!=='0';$('fish-grilled-toggle').checked=grilled;$('fishGrilled').value=grilled?'1':'0';
  select.innerHTML='<option value="">Tüm balıklar</option>'+(meta?.categoryCatalog?.fish||[]).map(f=>`<option value="${esc(f.id)}">${esc(f.label)} · ${num(grilled?f.grilledCount:f.normalCount)}</option>`).join('');select.value=value;
  if(typeof refreshSelect==='function')refreshSelect(select);
 }else{
  $('fishSpecies').value='';$('fishGrilled').value='';$('fish-grilled-toggle').checked=false;
  const options=(meta?.categoryCatalog?.families||[]).filter(f=>f.category===cat&&(currentSub===''||currentSub==null||Number(currentSub)===f.sub)),select=$('catalogFamily'),value=forcedFamily||select.value;
  select.innerHTML='<option value="">Tüm ürünler</option>'+options.map(f=>`<option value="${esc(f.id)}">${esc(f.label)} · ${num(f.count)}</option>`).join('');select.value=options.some(f=>f.id===value)?value:'';$('catalog-context').hidden=options.length<2&&!select.value;
  $('catalog-family-label').textContent=cat===7&&String(currentSub)==='3'?'Cevher':cat===5&&String(currentSub)==='1'?'Taş / cevher':cat===5&&String(currentSub)==='0'?'Malzeme':cat===1&&String(currentSub)==='0'?'Binek':cat===12&&String(currentSub)==='0'?'Sandık / kutu':'Ürün seç';
  if(typeof refreshSelect==='function')refreshSelect(select);
 }
}
$('catalogFamily').addEventListener('change',()=>{const chosen=(meta?.categoryCatalog?.families||[]).find(f=>f.id===$('catalogFamily').value);if(chosen?.group!=='Nakliye kutusu')for(const id of ['petState','petType','petSpecies','petLevelMin','petLevelMax'])$(id).value='';load(true)});$('fishSpecies').addEventListener('change',()=>load(true));
$('fish-grilled-toggle').addEventListener('change',()=>{$('fishGrilled').value=$('fish-grilled-toggle').checked?'1':'0';load(true)});

function prepareCatalogRestore(){for(const [id,values]of [['catalogFamily',meta?.categoryCatalog?.families||[]],['fishSpecies',meta?.categoryCatalog?.fish||[]]]){const select=$(id);select.innerHTML='<option value="">Tümü</option>'+values.map(v=>`<option value="${esc(v.id)}">${esc(v.label)}</option>`).join('')}}
function clearCatalogContext(){catalogHint={key:''}}
