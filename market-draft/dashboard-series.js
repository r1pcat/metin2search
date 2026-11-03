function buildMetricTimeline(rows,ids,days=1,now=Date.now()){
 const state=new Map(),all=[];
 for(const row of rows.filter(r=>ids.includes(r.server)).slice().sort((a,b)=>Date.parse(a.at)-Date.parse(b.at)||a.revision-b.revision)){
  state.set(row.server,row);if(state.size!==ids.length)continue;
  const point={at:row.at,origin:[...state.values()].some(v=>v.origin==='reconstructed')?'reconstructed':'snapshot'};for(const key of ['won','yang','listings','sellers'])point[key]=[...state.values()].reduce((sum,v)=>sum+Number(v[key]||0),0);
  if(all.at(-1)?.at===point.at)all[all.length-1]=point;else all.push(point);
 }
 const filtered=all.filter(p=>Date.parse(p.at)>=now-days*86400000);return filtered.length?filtered:all.slice(-1);
}
if(typeof module!=='undefined'&&module.exports)module.exports={buildMetricTimeline};
