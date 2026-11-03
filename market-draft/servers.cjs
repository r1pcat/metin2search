const catalog=[['nite','Nite','[DIA] Nite'],['star','Star','[SAPPHIRE] Star'],['safir','Safir','[SAPPHIRE] Safir'],['lucifer','Lucifer','[RUBY] Lucifer'],['charon','Charon','[RUBY] Charon'],['bagjanamu','Bagjanamu','Bagjanamu'],['arkadaslar','Arkadaşlar','Arkadaşlar'],['marmara','Marmara','Marmara'],['ezel','Ezel','Ezel'],['barbaros','Barbaros','Barbaros'],['dandanakan','Dandanakan','Dandanakan']].map(([id,name,clientName])=>({id,name,clientName}));
const normalize=s=>String(s).replace(/^\[[^\]]+\]\s*/,'').trim().toLocaleLowerCase('tr').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/ı/g,'i');
function resolveServer(name){return catalog.find(s=>normalize(s.name)===normalize(name))}
module.exports={catalog,resolveServer};
