const assert=require('node:assert/strict'),path=require('node:path'),{createReader}=require('./api-reader.cjs');
const root=__dirname,runtimeRoot=path.join(root,'data/capacity-simulation'),options={root,runtimeRoot,sourceRoot:path.join(runtimeRoot,'incoming'),sourceDisabled:true};
const projected=createReader(options),legacy=createReader({...options,useProjection:false});
const queries=[];for(const category of [0,1,2,3,4,6,8,9,12])for(const sort of ['unit-asc','total-desc','newest'])queries.push(`category=${category}&sort=${sort}&page=2`);
queries.push('q=Zehir','q=Hava+Kılıcı','seller=EZKAR','level=30&levelMax=90','min=10&max=100','qtyMin=5&qtyMax=100','grade=3&element=100','character=1&bonusCount=5,6,7','petType=6','alchemyClass=3','consumableEffect=critical&consumableMin=15','fishGrilled=1&category=7&sub=2','energyMin=5','absorbMin=15','bonusFilters='+encodeURIComponent(JSON.stringify([{type:72,value:35},{type:15,value:10}])));
for(const q of queries){const route='/api/items?server=safir&'+q,a=projected.query(route),b=legacy.query(route);assert.equal(a.status,200,route);assert.equal(a.body,b.body,route)}
for(const route of ['/api/meta?server=safir','/api/search-suggestions?server=safir&q=Zehir','/api/filter-relief?server=safir&category=2&min=10000&element=100'])assert.equal(projected.query(route).body,legacy.query(route).body,route);
projected.close();legacy.close();console.log(queries.length+' listing cases plus metadata, search suggestions and filter relief match original API exactly.');
