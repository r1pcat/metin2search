const assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite'),P=require('./purchase-status.cjs');
const source=new DatabaseSync(':memory:'),db=new DatabaseSync(':memory:');
source.exec('CREATE TABLE purchase_checks(id INTEGER PRIMARY KEY,payload_json TEXT);CREATE TABLE compact_presence(key TEXT PRIMARY KEY,session TEXT,vid INTEGER)');
db.exec('CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT);CREATE TABLE items(id TEXT PRIMARY KEY,listing_key TEXT,seller TEXT,detail TEXT)');
for(const [id,key,seller,vid,session]of [['a','a','Satıcı',22,'s1'],['b','b','Satıcı',22,'s1'],['c','c','Satıcı',23,'s2'],['d','d','Başka',22,'s1']]){db.prepare('INSERT INTO items VALUES(?,?,?,?)').run(id,key,seller,JSON.stringify({id,Seller:seller,SellerVid:vid}));source.prepare('INSERT INTO compact_presence VALUES(?,?,?)').run(key,session,vid)}
function attached(database,id,overrides={}){const row=database.prepare('SELECT seller,detail FROM items WHERE id=?').get(id);return P.attach(database,{id,Seller:row?.seller,SellerVid:row?JSON.parse(row.detail).SellerVid:undefined,serverId:'safir',...overrides})}
const state={source,db,id:'safir'},base={schemaVersion:1,server:'safir',session:'s1',shopVid:22,seller:'Satıcı',requestId:'r1',listingKey:'a',checkedAt:new Date(Date.now()-10000).toISOString(),result:'Preparing',correlation:'request-and-shop',scope:'shop',reason:'Premium pazar hazırlanıyor',evidenceSource:'fixture-correlated'};
let seq=0;function add(extra={}){source.prepare('INSERT INTO purchase_checks VALUES(?,?)').run(++seq,JSON.stringify({...base,requestId:'r'+seq,checkedAt:new Date(Date.now()-10000+seq*5).toISOString(),...extra}));return P.sync(state)}
assert.equal(P.sync(state),0);assert.equal(add({server:'ruby'}),0);assert.equal(add({correlation:'time-only'}),0);assert.equal(add({result:'NotEnoughMoney'}),0);assert.equal(add({session:'other'}),0);assert.equal(add({shopVid:99}),0);
assert.equal(add(),2);assert.equal(attached(db,'a').purchaseStatus.isFake,true);assert.equal(attached(db,'b').purchaseStatus.isFake,true);assert.equal(attached(db,'c').purchaseStatus.isFake,true);assert.equal(attached(db,'d').purchaseStatus,null);assert.equal(P.sync(state),0);
assert.equal(add({result:'NotEnoughMoney',checkedAt:new Date(Date.now()-5000).toISOString()}),2);assert.equal(attached(db,'a').purchaseStatus.isFake,false);assert.equal(attached(db,'a').purchaseStatus.state,'clear');assert.equal(attached(db,'b').purchaseStatus.state,'clear');
assert.equal(add({checkedAt:new Date(Date.now()-4000).toISOString()}),2);assert.equal(attached(db,'a').purchaseStatus.state,'fake');
assert.equal(add({result:'Available',scope:'listing',checkedAt:new Date(Date.now()-2000).toISOString()}),1);assert.equal(attached(db,'a').purchaseStatus.isFake,false);assert.equal(attached(db,'b').purchaseStatus.isFake,true);
assert.equal(add({checkedAt:new Date(Date.now()-12000).toISOString()}),0);assert.equal(P.status({...base,checkedAt:new Date(Date.now()-P.MIN_FRESH_MS-1).toISOString()}).state,'fake-stale');
assert.equal(add({listingKey:'later',checkedAt:new Date(Date.now()-1000).toISOString()}),0);db.prepare('INSERT INTO items VALUES(?,?,?,?)').run('later','later','Satıcı',JSON.stringify({SellerVid:22}));source.prepare('INSERT INTO compact_presence VALUES(?,?,?)').run('later','s1',22);assert.equal(P.sync(state),3);
assert.equal(P.validate({...base,checkedAt:'bad'},'safir'),false);
const {LiveSync}=require('./live-sync.cjs');state.revision=1;
for(const [k,v]of [['revision','1'],['snapshot','{"revision":1}'],['web_context','{"catalogRows":[]}'],['web_context_revision','1']])db.prepare('INSERT OR REPLACE INTO meta VALUES(?,?)').run(k,v);
source.prepare('INSERT INTO purchase_checks VALUES(?,?)').run(++seq,JSON.stringify({...base,checkedAt:new Date().toISOString(),requestId:'revision-check',scope:'listing',result:'Available'}));
let notified=0;LiveSync.prototype.syncPurchaseStatus.call({onChange:()=>notified++,log:()=>{}},state);
assert.equal(state.revision,2);assert.equal(notified,1);assert.equal(db.prepare("SELECT value FROM meta WHERE key='web_context_revision'").get().value,'2');assert.equal(db.prepare("SELECT value FROM meta WHERE key='web_context'").get().value,'{"catalogRows":[]}');
function fixture(){
 const source=new DatabaseSync(':memory:'),db=new DatabaseSync(':memory:');
 source.exec('CREATE TABLE purchase_checks(id INTEGER PRIMARY KEY,payload_json TEXT);CREATE TABLE compact_presence(key TEXT PRIMARY KEY,session TEXT,vid INTEGER)');
 db.exec('CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT);CREATE TABLE items(id TEXT PRIMARY KEY,listing_key TEXT,seller TEXT,detail TEXT)');
 const s={source,db,id:'safir',revision:1};let sequence=0;
 function item(id,session='s1',vid=22,seller='Satıcı',detailVid=vid){db.prepare('INSERT INTO items VALUES(?,?,?,?)').run(id,id,seller,JSON.stringify({SellerVid:detailVid}));source.prepare('INSERT INTO compact_presence VALUES(?,?,?)').run(id,session,vid)}
 function event(extra={}){source.prepare('INSERT INTO purchase_checks VALUES(?,?)').run(++sequence,JSON.stringify({...base,requestId:'f'+sequence,...extra}));return P.sync(s)}
 return {s,item,event,close(){source.close();db.close()}};
}
{
 const f=fixture();f.item('a');f.item('older-observation','s0');assert.equal(f.event(),2);
 assert.equal(attached(f.s.db,'older-observation').purchaseStatus.isFake,true);
 f.item('new-category');f.item('new-session','s2');f.item('new-vid','s1',23);f.item('other-seller','s1',22,'Başka');f.item('detail-mismatch','s1',22,'Satıcı',23);
 f.s.revision++;assert.equal(P.sync(f.s),3);assert.equal(attached(f.s.db,'new-category').purchaseStatus.isFake,true);
 assert.equal(attached(f.s.db,'new-session').purchaseStatus.isFake,true);
 assert.equal(P.attach(f.s.db,{id:'other-seller'}).purchaseStatus,null);
 for(const id of ['new-vid','detail-mismatch'])assert.equal(attached(f.s.db,id).purchaseStatus.isFake,true);
 assert.equal(P.sync(f.s),0);
 assert.equal(f.event({listingKey:'a',result:'Success',scope:'shop',checkedAt:new Date(Date.now()-2000).toISOString()}),1);
 f.item('third-category');f.s.revision++;assert.equal(P.sync(f.s),1);
 assert.equal(attached(f.s.db,'a').purchaseStatus.isFake,false);
 assert.equal(attached(f.s.db,'third-category').purchaseStatus.isFake,true);
 assert.equal(f.event({checkedAt:new Date(Date.now()-12000).toISOString()}),0);
 f.item('wrong-server');f.s.id='ruby';f.s.revision++;assert.equal(P.sync(f.s),0);assert.equal(attached(f.s.db,'wrong-server',{serverId:'ruby'}).purchaseStatus,null);
 f.close();
}
{
 const f=fixture();f.item('a','old-session');f.item('other-current');
 assert.equal(f.event(),0);assert.equal(attached(f.s.db,'a').purchaseStatus,null);assert.equal(attached(f.s.db,'other-current').purchaseStatus,null);f.close();
}
{
 const f=fixture();f.item('a');assert.equal(f.event({correlation:'time-only'}),0);f.item('time-only-new');f.s.revision++;assert.equal(P.sync(f.s),0);assert.equal(attached(f.s.db,'time-only-new').purchaseStatus,null);f.close();
}
{
 const f=fixture();f.item('a');assert.equal(f.event(),1);f.item('expired-new');f.s.revision++;
 const clock=Date.now;try{Date.now=()=>clock()+3*3600e3;assert.equal(P.sync(f.s),1);const s=attached(f.s.db,'expired-new').purchaseStatus;assert.equal(s.isFake,true);assert.equal(s.state,'fake-stale');assert.equal(attached(f.s.db,'a').purchaseStatus.state,'fake-stale')}finally{Date.now=clock}f.close();
}
{
 const f=fixture();f.item('a');P.ensure(f.s.db);f.s.db.prepare('INSERT INTO web_purchase_status VALUES(?,?)').run('a',JSON.stringify(base));
 f.s.db.prepare("INSERT INTO meta VALUES('purchase_check_cursor','0')").run();f.item('migration-new');assert.equal(P.sync(f.s),1);assert.equal(attached(f.s.db,'migration-new').purchaseStatus.isFake,true);assert.equal(P.sync(f.s),0);f.close();
}
{
 const f=fixture();f.item('a');f.event();f.item('live-category');f.s.revision++;
 let changes=0;const live={onChange:()=>changes++,log:()=>{}};
 LiveSync.prototype.syncPurchaseStatus.call(live,f.s);
 assert.equal(attached(f.s.db,'live-category').purchaseStatus.isFake,true);
 assert.equal(changes,1);assert.equal(f.s.revision,3);
 assert.equal(f.s.db.prepare("SELECT value FROM meta WHERE key='purchase_status_revision'").get().value,'3');
 LiveSync.prototype.syncPurchaseStatus.call(live,f.s);assert.equal(changes,1);assert.equal(f.s.revision,3);f.close();
}
{
 const f=fixture();f.item('a');f.event();assert.equal(attached(f.s.db,'a').purchaseStatus.isFake,true);
 f.s.db.prepare('UPDATE items SET detail=? WHERE id=?').run(JSON.stringify({SellerVid:99}),'a');
 assert.equal(attached(f.s.db,'a').purchaseStatus.isFake,true);
 assert.equal(f.s.db.prepare('SELECT COUNT(*) n FROM web_purchase_status WHERE item_id=?').get('a').n,1);
 f.s.db.prepare('UPDATE items SET detail=?,seller=? WHERE id=?').run(JSON.stringify({SellerVid:22}),'Different Seller','a');
 assert.equal(attached(f.s.db,'a').purchaseStatus,null);
 f.s.db.prepare('UPDATE items SET seller=? WHERE id=?').run('Satıcı','a');
 assert.equal(attached(f.s.db,'a',{serverId:'ruby'}).purchaseStatus,null);
 assert.equal(P.attach(f.s.db,{id:'a'}).purchaseStatus,null);
 assert.equal(attached(f.s.db,'a').purchaseStatus.isFake,true);f.close();
}
const ago=ms=>new Date(Date.now()-ms).toISOString(),st=(f,id,o)=>attached(f.s.db,id,o).purchaseStatus,shops=f=>f.s.db.prepare('SELECT session,shop_vid,seller FROM web_purchase_shops').all();
{
 const f=fixture();f.item('a');assert.equal(f.event({checkedAt:ago(90*60e3)}),1);
 let s=st(f,'a');assert.equal(s.isFake,true);assert.equal(s.state,'fake-stale');assert.ok(s.ageSeconds>=5400&&s.ageSeconds<5500);assert.equal(s.checkedAt.length>0,true);
 assert.equal(f.event({checkedAt:ago(60e3)}),1);s=st(f,'a');assert.equal(s.state,'fake');assert.ok(s.ageSeconds>=59&&s.ageSeconds<70);
 const clock=Date.now;try{Date.now=()=>clock()+24*3600e3;s=st(f,'a');assert.equal(s.isFake,true);assert.equal(s.state,'fake-stale')}finally{Date.now=clock}
 f.close();
}
{
 const f=fixture();P.ensure(f.s.db);assert.equal(P.freshMs(f.s.db),30*60e3);
 f.s.db.prepare("INSERT INTO meta VALUES('last_full_round_ms',?)").run(String(20*60e3));assert.equal(P.freshMs(f.s.db),60*60e3);
 process.env.PURCHASE_FRESH_MS='1000';try{assert.equal(P.freshMs(f.s.db),1000)}finally{delete process.env.PURCHASE_FRESH_MS}
 assert.equal(P.status(base,Date.parse(base.checkedAt)+60e3,30e3).state,'fake-stale');assert.equal(P.status(base,Date.parse(base.checkedAt)+10e3,30e3).state,'fake');f.close();
}
{
 const f=fixture();f.item('a');assert.equal(f.event({checkedAt:ago(120e3)}),1);assert.equal(st(f,'a').state,'fake');
 assert.equal(f.event({result:'NotEnoughMoney',checkedAt:ago(60e3)}),1);assert.equal(st(f,'a').state,'clear');assert.equal(st(f,'a').isFake,false);
 assert.equal(f.event({checkedAt:ago(90e3)}),0);assert.equal(st(f,'a').state,'clear');
 f.item('late-after-clear');f.s.revision++;assert.equal(P.sync(f.s),0);assert.equal(st(f,'late-after-clear').state,'clear');
 assert.equal(f.event({checkedAt:ago(10e3)}),2);assert.equal(st(f,'a').state,'fake');assert.equal(st(f,'late-after-clear').state,'fake');
 assert.equal(f.event({result:'NotEnoughMoney',checkedAt:ago(30e3)}),0);assert.equal(st(f,'a').state,'fake');
 assert.equal(f.event({result:'NotEnoughMoney',scope:'listing',correlation:'time-only',checkedAt:ago(1e3)}),0);assert.equal(st(f,'a').state,'fake');
 f.close();
}
{
 const f=fixture();f.item('a');f.event({checkedAt:ago(20e3)});
 for(const extra of [{result:'Unknown'},{correlation:'time-only'},{correlation:'time-only',result:'NotEnoughMoney'},{result:'NotEnoughMoney',correlation:'request-only'}])assert.equal(f.event({...extra,checkedAt:ago(2e3)}),0);
 assert.equal(st(f,'a').state,'fake');const before=JSON.stringify(f.s.db.prepare('SELECT * FROM web_purchase_status').all());
 for(let i=0;i<3;i++){f.s.revision++;assert.equal(P.sync(f.s),0)}assert.equal(JSON.stringify(f.s.db.prepare('SELECT * FROM web_purchase_status').all()),before);
 f.close();
}
{
 const f=fixture();f.item('a');f.item('o','s1',22,'Other');
 assert.equal(f.event({checkedAt:ago(60e3)}),1);assert.equal(f.event({listingKey:'o',seller:'Other',checkedAt:ago(50e3)}),1);
 assert.equal(f.event({result:'NotEnoughMoney',checkedAt:ago(20e3)}),1);assert.equal(st(f,'a').state,'clear');assert.equal(st(f,'o').state,'fake');
 f.item('ns','s2');assert.equal(f.event({session:'s2',listingKey:'ns',checkedAt:ago(10e3)}),2);assert.equal(shops(f).filter(r=>r.seller==='Satıcı').length,1);assert.equal(shops(f).find(r=>r.seller==='Satıcı').session,'s2');
 assert.equal(f.event({checkedAt:ago(15e3)}),0);assert.equal(shops(f).find(r=>r.seller==='Satıcı').session,'s2');assert.equal(st(f,'o').state,'fake');
 f.close();
}
{
 const f=fixture();f.item('a');f.event({checkedAt:ago(5*3600e3)});assert.equal(shops(f).length,1);
 f.s.revision++;P.sync(f.s);assert.equal(shops(f).length,1);assert.equal(st(f,'a').state,'fake-stale');
 f.s.db.exec('CREATE TABLE listing_lifecycle(listing_key TEXT PRIMARY KEY,is_active INTEGER NOT NULL,last_seen TEXT,removed_at TEXT,source_scope TEXT,revision INTEGER)');
 f.s.db.prepare("INSERT INTO listing_lifecycle VALUES('a',1,NULL,NULL,NULL,1)").run();f.s.revision++;P.sync(f.s);assert.equal(shops(f).length,1);
 f.s.db.prepare("UPDATE listing_lifecycle SET is_active=0 WHERE listing_key='a'").run();f.s.revision++;P.sync(f.s);assert.equal(shops(f).length,0);assert.equal(st(f,'a'),null);
 f.close();
 const g=fixture();g.item('a');g.event({checkedAt:ago(1e3)});g.s.source.prepare("UPDATE compact_presence SET vid=23 WHERE key='a'").run();g.s.db.prepare("UPDATE items SET detail=? WHERE id='a'").run(JSON.stringify({SellerVid:23}));
 g.s.revision++;P.sync(g.s);assert.equal(shops(g).length,0);assert.equal(g.s.db.prepare('SELECT COUNT(*) n FROM web_purchase_status').get().n,0);g.close();
}
{
 const f=fixture();P.ensure(f.s.db);f.s.db.prepare("INSERT OR REPLACE INTO meta VALUES('purchase_shop_scope_version','2')").run();f.s.db.prepare("INSERT OR REPLACE INTO meta VALUES('purchase_status_revision','1')").run();
 f.s.db.prepare("INSERT INTO web_purchase_shops VALUES('safir','s1',22,'Satıcı',1,2,'{}')").run();P.ensure(f.s.db);P.ensure(f.s.db);
 assert.equal(f.s.db.prepare("SELECT value FROM meta WHERE key='purchase_shop_scope_version'").get().value,'3');assert.equal(f.s.db.prepare("SELECT value FROM meta WHERE key='purchase_status_revision'").get(),undefined);
 assert.equal(f.s.db.prepare('SELECT COUNT(*) n FROM web_purchase_shops').get().n,1);f.close();
}
{
 const f=fixture();f.item('a');f.item('b');f.item('c','s0');
 assert.equal(f.event({checkedAt:new Date(Date.now()-9000).toISOString()}),3);
 for(const id of ['a','b','c'])assert.equal(attached(f.s.db,id).purchaseStatus.isFake,true);
 f.event({result:'NotEnoughMoney',scope:'listing',listingKey:'a',checkedAt:new Date(Date.now()-3000).toISOString()});
 for(const id of ['a','b','c'])assert.equal(attached(f.s.db,id).purchaseStatus.state,'clear',id+' temiz görünmeli');
 f.event({checkedAt:new Date(Date.now()-1000).toISOString()});
 for(const id of ['a','b','c'])assert.equal(attached(f.s.db,id).purchaseStatus.isFake,true,'daha yeni Preparing yeniden FAKE yapar');
 f.close();
}
{
 const f=fixture();f.item('a');f.item('b');
 assert.equal(f.event({checkedAt:new Date(Date.now()-9000).toISOString()}),2);
 f.s.source.prepare("UPDATE compact_presence SET session='s2'").run();
 f.event({result:'NotEnoughMoney',scope:'listing',listingKey:'a',checkedAt:new Date(Date.now()-3000).toISOString()});
 f.s.revision++;P.sync(f.s);
 for(const id of ['a','b'])assert.equal(attached(f.s.db,id).purchaseStatus.state,'clear',id+' eski oturum kaydına rağmen temizlenmeli');
 f.close();
}
{
 const f=fixture();f.item('a');f.item('b','s0',23);f.item('other','s1',22,'Other');
 const {packItem,unpackItem}=require('./packed-details.cjs');
 f.s.db.exec('CREATE TABLE web_payloads(hash TEXT PRIMARY KEY,bytes BLOB,created INTEGER)');
 const raw={id:'a',Seller:'Satıcı',SellerVid:22,serverId:'safir',IsFake:false,MarketStatus:'Açıldı',Won:5,Yang:123,ClientDetails:{lines:['fixture']}};
 const packed=packItem(f.s.db,raw);
 const read=()=>P.attach(f.s.db,unpackItem(f.s.db,packed));
 const check=(item,expected)=>{assert.equal(item.IsFake,expected);assert.equal(item.IsFake,item.purchaseStatus?.state==='unknown'?null:item.purchaseStatus?.isFake??null);assert.equal(item.MarketStatus,expected===null?'Doğrulanmadı':expected?'FAKE':'Doğrulandı')};
 check(read(),null);
 check(attached(f.s.db,'a',{IsFake:true,MarketStatus:'FAKE'}),null);
 f.event({checkedAt:ago(90*60e3)});check(read(),true);assert.equal(read().purchaseStatus.state,'fake-stale');
 check(attached(f.s.db,'b',{IsFake:false}),true);check(attached(f.s.db,'other',{IsFake:true}),null);
 for(const extra of [{result:'Unknown',scope:'listing'},{result:'NotEnoughMoney',scope:'listing',correlation:'time-only'}]){f.event({...extra,checkedAt:ago(5000)});check(read(),true)}
 f.event({result:'NotEnoughMoney',scope:'listing',checkedAt:ago(4000)});
 check(read(),false);check(attached(f.s.db,'b',{IsFake:true,MarketStatus:'FAKE'}),false);
 f.event({checkedAt:ago(2000)});check(read(),true);
 check(attached(f.s.db,'a',{serverId:'ruby',IsFake:true}),null);
 assert.deepEqual(unpackItem(f.s.db,packed),raw);assert.equal(read().Won,5);assert.equal(read().Yang,123);
 f.close();
 const empty=new DatabaseSync(':memory:');check(P.attach(empty,{IsFake:true,MarketStatus:'FAKE'}),null);empty.close();
}
{
 const f=fixture();f.item('a','s1',22,'Satıcı',1085159);f.item('b','s1',22,'Satıcı',1085159);f.item('older','s0',22,'Satıcı',1085159);
 assert.ok(f.event()>=2);
 for(const id of ['a','b','older'])assert.equal(attached(f.s.db,id).purchaseStatus.isFake,true);
 f.s.revision++;f.s.id='safir';P.sync(f.s);assert.equal(attached(f.s.db,'a').purchaseStatus.isFake,true);
 f.close();
}
{
 const f=fixture();f.item('a','s0',22,'Satıcı',1085159);assert.equal(f.event(),0);assert.equal(attached(f.s.db,'a').purchaseStatus,null);f.close();
 const g=fixture();g.item('a','s1',23,'Satıcı',22);assert.equal(g.event(),0);assert.equal(attached(g.s.db,'a').purchaseStatus,null);g.close();
}
console.log('Purchase status: persistent FAKE (no TTL), stale label, Fake->Clear, newer-wins ordering, shop/session isolation, shop removal, v3 migration, delayed categories, strict isolation, revision reuse, idempotency and canonical API aliases passed.');
source.close();db.close();
