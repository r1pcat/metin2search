function homeSummary(status,states){
 const servers=status.servers.map(server=>{
  const state=states.get(server.id);let snapshot=null;
  if(server.available&&state?.ready)try{snapshot=JSON.parse(state.db.prepare("SELECT json_object('overall',json_extract(value,'$.overall'),'economy',json_extract(value,'$.economy')) value FROM meta WHERE key='web_context'").get()?.value||'null')}catch{}
  let metadata=null;try{metadata=JSON.parse(state?.db.prepare("SELECT value FROM meta WHERE key='snapshot'").get()?.value||'null')}catch{}
  const ready=!!snapshot,overall=snapshot?.overall,economy=snapshot?.economy;
  return {id:server.id,name:server.name,group:server.clientName?.match(/^\[([^\]]+)\]/)?.[1]||'TÜRKİYE',available:ready,lastSeen:server.lastSeen||state?.latestSeen||null,revision:server.revision||0,lastSync:server.lastSync||null,updatedAt:metadata?.importedAt||null,status:!ready?'waiting':server.error?'issue':status.running===false?'paused':!Number.isFinite(Date.parse(server.lastSeen||state?.latestSeen||''))?'unknown':Date.now()-Date.parse(server.lastSeen||state?.latestSeen||'')>600000?'stale':'ready',won:ready?Number(economy?.won||0):null,yang:ready?Number(economy?.yang||0):null,listings:ready?Number(overall?.count||0):null,sellers:ready?Number(overall?.sellers||0):null,distinctItems:ready?Number(overall?.distinctItems||0):null,valueWon:ready?Number(economy?.won||0)+Number(economy?.yang||0)/1e8:null};
 });
 const ready=servers.filter(s=>s.available);
 return {servers,running:status.running!==false,won:ready.reduce((n,s)=>n+s.won,0),yang:ready.reduce((n,s)=>n+s.yang,0),available:ready.length,listings:ready.reduce((n,s)=>n+s.listings,0),sellerRecords:ready.reduce((n,s)=>n+s.sellers,0),valueWon:ready.reduce((n,s)=>n+s.valueWon,0),updated:new Date().toISOString(),basis:'recorded-listing-values'};
}
module.exports={homeSummary};
