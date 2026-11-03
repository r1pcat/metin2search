const {buildItemFamilies}=require('./search-families.cjs');
const normalize=s=>String(s).toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/ı/g,'i');
function buildWebSnapshot(db){const table=require('./search-projection.cjs').searchTable(db),query=sql=>db.prepare(sql.replaceAll('active_items',table));let overall,categories,economy,distribution,economySeries,itemSearch,playerSearch,itemFamilies;
overall=query('SELECT COUNT(*) count,COUNT(DISTINCT vnum) distinctItems,COUNT(DISTINCT seller) sellers FROM active_items items').get();
categories=query('SELECT category,sub,COUNT(*) count FROM active_items items GROUP BY category,sub').all();
economy=query("SELECT SUM(won) won,SUM(yang) yang,COUNT(*) listings FROM active_items items").get();
distribution=query("SELECT category,COUNT(*) count,SUM(won+yang/100000000.0) value FROM active_items items GROUP BY category ORDER BY value DESC").all();
economySeries=[];const observedSlots=new Map();let runningWon=0,runningYang=0,lastBucket=null;
for(const row of query("SELECT listing_key,won,yang,observed FROM active_items items ORDER BY observed,id").iterate()){
 const key=row.listing_key,old=observedSlots.get(key);
 if(old){runningWon-=old.won;runningYang-=old.yang}
 observedSlots.set(key,row);runningWon+=row.won;runningYang+=row.yang;
 const bucket=Math.floor(Date.parse(row.observed)/180000);
 const point={observed:row.observed,value:runningWon+runningYang/1e8,won:runningWon,yang:runningYang};
 if(bucket===lastBucket)economySeries[economySeries.length-1]=point;else{economySeries.push(point);lastBucket=bucket}
}
const rawSearch=query('SELECT name label,MIN(vnum) vnum,COUNT(*) count FROM active_items items WHERE vnum<>50300 GROUP BY name').all();const skillBooks=query("SELECT COALESCE(NULLIF(trim(json_extract(detail,'$.ClientDetails.lines[0]')),''),name) label,50300 vnum,COUNT(*) count,json_group_array(id) bookIds FROM active_items items WHERE vnum=50300 GROUP BY label").all().map(r=>({...r,bookIds:JSON.parse(r.bookIds)}));
const orbs=query("SELECT trim(json_extract(detail,'$.ClientDetails.lines[0]')) label,MIN(vnum) vnum,COUNT(*) count,json_group_array(id) bookIds FROM active_items items WHERE name='Dönüşüm Küresi' AND trim(COALESCE(json_extract(detail,'$.ClientDetails.lines[0]'),''))<>'' GROUP BY label").all().map(r=>({...r,bookIds:JSON.parse(r.bookIds)})).filter(r=>normalize(r.label)!==normalize('Dönüşüm Küresi'));
const allBookIds=skillBooks.flatMap(r=>r.bookIds);itemSearch=[...rawSearch,...orbs,...skillBooks.filter(r=>normalize(r.label)!==normalize('Beceri Kitabı')),...(allBookIds.length?[{label:'Beceri Kitabı',vnum:50300,count:allBookIds.length,bookIds:allBookIds}]:[])].map(r=>({...r,key:normalize(r.label)}));playerSearch=query('SELECT seller label,COUNT(*) count FROM active_items items GROUP BY seller').all().map(r=>({...r,key:normalize(r.label)}));itemFamilies=buildItemFamilies(itemSearch);
const specialOptions={orbs:orbs.map(({label,count})=>({label,count})).sort((a,b)=>a.label.localeCompare(b.label,'tr')),petSpecies:query("SELECT DISTINCT json_extract(detail,'$.special.pet.species') label FROM active_items WHERE vnum=55002 AND json_extract(detail,'$.special.pet.species') IS NOT NULL ORDER BY label").all().map(r=>r.label),petTypes:query("SELECT DISTINCT json_extract(detail,'$.special.pet.type') n FROM active_items WHERE vnum=55002 AND json_extract(detail,'$.special.pet.type') IS NOT NULL ORDER BY n").all().map(r=>r.n)};
const catalogRows=query('SELECT category,sub,vnum,name,COUNT(*) count FROM active_items GROUP BY category,sub,vnum,name').all();
return {catalogRows,overall,categories,economy,distribution,economySeries,itemSearch,playerSearch,itemFamilies,specialOptions};}
function publishWebSnapshot(db){const revision=db.prepare("SELECT value FROM meta WHERE key='revision'").get()?.value;if(!revision)return;const old=db.prepare("SELECT value FROM meta WHERE key='web_context_revision'").get()?.value;if(old===revision)return;const started=performance.now(),value=JSON.stringify(buildWebSnapshot(db));db.exec('SAVEPOINT web_publication');try{db.prepare("INSERT OR REPLACE INTO meta VALUES('web_context',?)").run(value);db.prepare("INSERT OR REPLACE INTO meta VALUES('web_context_revision',?)").run(revision);db.exec('RELEASE web_publication')}catch(e){db.exec('ROLLBACK TO web_publication; RELEASE web_publication');throw e}return {ms:Math.round(performance.now()-started),bytes:Buffer.byteLength(value)};}
function ensureWebIndexes(db){db.exec(`CREATE INDEX IF NOT EXISTS idx_web_name ON items(name,vnum,listing_key);CREATE INDEX IF NOT EXISTS idx_web_vnum ON items(vnum,listing_key);CREATE INDEX IF NOT EXISTS idx_web_seller ON items(seller,listing_key);CREATE INDEX IF NOT EXISTS idx_web_category_price ON items(category,(won*100000000.0+yang)/MAX(quantity,1),id,listing_key);CREATE INDEX IF NOT EXISTS idx_web_totals ON items(won,yang,id,listing_key);CREATE INDEX IF NOT EXISTS idx_web_summary ON items(vnum,seller,category,sub,won,yang,observed,listing_key,id);
 CREATE INDEX IF NOT EXISTS idx_web_level_count ON items(level,category,listing_key);
 CREATE INDEX IF NOT EXISTS idx_web_quantity_count ON items(quantity,category,listing_key);
 CREATE INDEX IF NOT EXISTS idx_web_grade_count ON items(grade,category,listing_key);
 CREATE INDEX IF NOT EXISTS idx_web_element_count ON items(element,category,listing_key);
 CREATE INDEX IF NOT EXISTS idx_web_value_count ON items((won+yang/100000000.0),category,listing_key);
 CREATE INDEX IF NOT EXISTS idx_web_recent ON items(observed DESC,id,listing_key);
 `)}
function indexBonuses(db,x){db.prepare('DELETE FROM item_bonuses WHERE item_id=?').run(x.id);const put=db.prepare('INSERT INTO item_bonuses VALUES(?,?,?,?)');for(const b of x.displayBonuses||x.ClientDetails?.bonuses||[]){const type=Number(b.type),value=Number(b.value),slot=Number(b.slot);if(Number.isFinite(type)&&Number.isFinite(value)&&Number.isFinite(slot))put.run(x.id,type,slot,value)}}
function prepareBonusIndex(db){db.exec('CREATE TABLE IF NOT EXISTS item_bonuses(item_id TEXT,type INTEGER,slot INTEGER,value REAL);CREATE INDEX IF NOT EXISTS idx_bonus_item ON item_bonuses(item_id);CREATE INDEX IF NOT EXISTS idx_bonus_lookup ON item_bonuses(type,value,slot,item_id)');if(db.prepare("SELECT value FROM meta WHERE key='bonus_index_schema'").get()?.value==='1')return;db.exec('BEGIN IMMEDIATE');try{for(const row of db.prepare('SELECT id,detail FROM items').iterate()){const x=JSON.parse(row.detail);indexBonuses(db,{...x,id:row.id})}db.prepare("INSERT OR REPLACE INTO meta VALUES('bonus_index_schema','1')").run();db.exec('COMMIT')}catch(e){db.exec('ROLLBACK');throw e}}
module.exports={buildWebSnapshot,publishWebSnapshot,ensureWebIndexes,indexBonuses,prepareBonusIndex};
