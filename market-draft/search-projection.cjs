const columns=['id','name','seller','vnum','won','yang','quantity','category','sub','job','level','grade','element','observed','feature_key','energy','absorption','upgrade','has_stones','listing_key'];
const schemaVersion='1';
function ensureSearchProjection(db){
 if(db.prepare("SELECT value FROM meta WHERE key='search_projection_schema'").get()?.value===schemaVersion){ensureBonusCounts(db);return}
 const active=alias=>`CASE WHEN (SELECT value FROM meta WHERE key='lifecycle_managed')='1' THEN COALESCE((SELECT is_active FROM listing_lifecycle WHERE listing_key=${alias}.listing_key),0) ELSE COALESCE((SELECT is_active FROM listing_lifecycle WHERE listing_key=${alias}.listing_key),1) END`;
 const detail=alias=>`json_object('special',json_extract(${alias}.detail,'$.special'),'ClientDetails',json_object('lines',json_array(json_extract(${alias}.detail,'$.ClientDetails.lines[0]'))))`;
 const values=alias=>columns.map(c=>alias+'.'+c).concat(detail(alias),active(alias),`COALESCE(json_extract(${alias}.detail,'$.ClientDetails.status')='client-rendered',0)`).join(',');
 const names=columns.concat('detail','is_active','details_complete');
 db.exec('BEGIN IMMEDIATE');try{
  db.exec(`CREATE TABLE web_search_items AS SELECT ${columns.map(c=>'i.'+c).join(',')},${detail('i')} detail,${active('i')} is_active,COALESCE(json_extract(i.detail,'$.ClientDetails.status')='client-rendered',0) details_complete FROM items i;
   CREATE UNIQUE INDEX ws_id ON web_search_items(id);CREATE INDEX ws_key ON web_search_items(listing_key);
   CREATE VIEW web_search_active AS SELECT * FROM web_search_items WHERE is_active=1;
   CREATE TRIGGER ws_item_insert AFTER INSERT ON items BEGIN INSERT INTO web_search_items(${names}) VALUES(${values('NEW')}); END;`);
  db.exec(`CREATE TRIGGER ws_item_update AFTER UPDATE ON items BEGIN
   INSERT INTO web_search_items(${names}) VALUES(${values('NEW')}) ON CONFLICT(id) DO UPDATE SET ${names.slice(1).map(c=>c+'=excluded.'+c).join(',')};END;
   CREATE TRIGGER ws_item_delete AFTER DELETE ON items BEGIN DELETE FROM web_search_items WHERE id=OLD.id;END;
   CREATE TRIGGER ws_life_insert AFTER INSERT ON listing_lifecycle BEGIN UPDATE web_search_items SET is_active=NEW.is_active WHERE listing_key=NEW.listing_key;END;
   CREATE TRIGGER ws_life_update AFTER UPDATE OF is_active ON listing_lifecycle BEGIN UPDATE web_search_items SET is_active=NEW.is_active WHERE listing_key=NEW.listing_key;END;
   CREATE TRIGGER ws_life_delete AFTER DELETE ON listing_lifecycle BEGIN UPDATE web_search_items SET is_active=CASE WHEN (SELECT value FROM meta WHERE key='lifecycle_managed')='1' THEN 0 ELSE 1 END WHERE listing_key=OLD.listing_key;END;
   CREATE TRIGGER ws_managed_insert AFTER INSERT ON meta WHEN NEW.key='lifecycle_managed' BEGIN UPDATE web_search_items SET is_active=${active('web_search_items')};END;
   CREATE TRIGGER ws_managed_update AFTER UPDATE OF value ON meta WHEN NEW.key='lifecycle_managed' AND OLD.value IS NOT NEW.value BEGIN UPDATE web_search_items SET is_active=${active('web_search_items')};END;
   CREATE TRIGGER ws_managed_delete AFTER DELETE ON meta WHEN OLD.key='lifecycle_managed' BEGIN UPDATE web_search_items SET is_active=${active('web_search_items')};END;
  `);
  for(const [name,cols] of Object.entries({category:'category,sub',price:'(won*100000000.0+yang)/MAX(quantity,1),id',category_price:'category,(won*100000000.0+yang)/MAX(quantity,1),id',total:'won,yang,id',recent:'observed DESC,id',quantity:'quantity,id',name:'name',seller:'seller',vnum:'vnum',level:'level',grade:'grade',element:'element',value:'(won+yang/100000000.0)',summary:'category,sub,vnum,name,seller,won,yang,observed,id'}))db.exec(`CREATE INDEX ws_${name} ON web_search_items(${cols}) WHERE is_active=1`);
  db.prepare("INSERT OR REPLACE INTO meta VALUES('search_projection_schema',?)").run(schemaVersion);db.exec('COMMIT');
 }catch(e){db.exec('ROLLBACK');throw e}
 ensureBonusCounts(db);
}
function ensureBonusCounts(db){
 if(!db.prepare("SELECT 1 FROM sqlite_master WHERE name='item_bonuses'").get()||db.prepare("SELECT value FROM meta WHERE key='search_bonus_counts_schema'").get()?.value==='1')return;
 const count=key=>`(SELECT COUNT(DISTINCT slot) FROM item_bonuses WHERE item_id=${key} AND slot>=0 AND slot<7 AND type>0)`;
 db.exec('BEGIN IMMEDIATE');try{db.exec(`ALTER TABLE web_search_items ADD COLUMN bonus_count INTEGER NOT NULL DEFAULT 0;
 UPDATE web_search_items SET bonus_count=${count('web_search_items.id')};
 CREATE INDEX ws_bonus_count ON web_search_items(bonus_count) WHERE is_active=1;
 CREATE TRIGGER ws_bonus_insert AFTER INSERT ON item_bonuses BEGIN UPDATE web_search_items SET bonus_count=${count('NEW.item_id')} WHERE id=NEW.item_id;END;
 CREATE TRIGGER ws_bonus_delete AFTER DELETE ON item_bonuses BEGIN UPDATE web_search_items SET bonus_count=${count('OLD.item_id')} WHERE id=OLD.item_id;END;
 CREATE TRIGGER ws_bonus_update AFTER UPDATE ON item_bonuses BEGIN UPDATE web_search_items SET bonus_count=${count('web_search_items.id')} WHERE id IN (OLD.item_id,NEW.item_id);END;`);
 db.prepare("INSERT OR REPLACE INTO meta VALUES('search_bonus_counts_schema','1')").run();db.exec('COMMIT');}catch(error){db.exec('ROLLBACK');throw error}
}
function searchTable(db){if(!db.prepare("SELECT 1 FROM sqlite_master WHERE name='meta'").get())return 'active_items';return db.prepare("SELECT COUNT(*) n FROM meta WHERE key IN ('search_projection_schema','search_bonus_counts_schema') AND value='1'").get().n===2?'web_search_active':'active_items'}
module.exports={ensureSearchProjection,searchTable};
