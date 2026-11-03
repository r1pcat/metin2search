const normalize=s=>String(s).toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/ı/g,'i');
function baseItemName(name){return String(name).replace(/\s*\+\s*\d+\s*$/,'').trim();}
function buildItemFamilies(entries){const families=new Map();for(const entry of entries){const label=baseItemName(entry.label),key=normalize(label);let family=families.get(key);if(!family){family={label,key,vnum:entry.vnum,count:0,names:[]};families.set(key,family);}family.count+=entry.count;family.names.push(entry.label);if(entry.bookIds)family.bookIds=[...(family.bookIds||[]),...entry.bookIds];if(entry.vnum<family.vnum)family.vnum=entry.vnum;}return [...families.values()];}
module.exports={baseItemName,buildItemFamilies};
