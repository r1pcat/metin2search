function reconstructDashboard(observations,lifecycle,{cutoff=Date.now(),bucketMs=300000}={}){
 const events=[...observations.map(r=>({...r,t:Date.parse(r.observed),kind:'observation',order:Number(r.source_version||0)})),...lifecycle.map(r=>({...r,t:Date.parse(r.occurred),kind:'lifecycle',order:Number(r.id||0)}))].filter(r=>Number.isFinite(r.t)&&r.t<=cutoff).sort((a,b)=>a.t-b.t||Number(a.kind==='lifecycle')-Number(b.kind==='lifecycle')||a.order-b.order);
 const known=new Map(),active=new Map(),sellers=new Map(),points=[];let won=0,yang=0,lastBucket=null;
 function remove(key){const old=active.get(key);if(!old)return;won-=old.won;yang-=old.yang;const count=(sellers.get(old.seller)||0)-1;if(count>0)sellers.set(old.seller,count);else sellers.delete(old.seller);active.delete(key)}
 function add(key,row){remove(key);active.set(key,row);won+=row.won;yang+=row.yang;sellers.set(row.seller,(sellers.get(row.seller)||0)+1)}
 for(const e of events){const key=e.listing_key||e.key;if(!key)continue;
  if(e.kind==='observation'){const row={won:Number(e.won||0),yang:Number(e.yang||0),seller:String(e.seller||'')};known.set(key,row);add(key,row)}
  else if(Number(e.is_active)===0)remove(key);else if(known.has(key))add(key,known.get(key));
  const p={at:new Date(e.t).toISOString(),won,yang,listings:active.size,sellers:sellers.size},bucket=Math.floor(e.t/bucketMs);
  if(bucket===lastBucket)points[points.length-1]=p;else{points.push(p);lastBucket=bucket}
 }
 return points;
}
module.exports={reconstructDashboard};
