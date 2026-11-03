const assert=require('node:assert/strict'),{createGuard}=require('./guard.cjs');
let t=1e9;const mk=(extra={},env={})=>createGuard({now:()=>t,env,...extra});
const ctx=(o={})=>({ip:'203.0.113.5',method:'GET',pathname:'/pazar',query:[],headers:{host:'example.com'},rawUrl:'/pazar',...o});

let g=mk();
assert.equal(g.decide(ctx({method:'POST'})).status,405);
assert.equal(g.decide(ctx({method:'DELETE'})).status,405);
assert.equal(g.decide(ctx({rawUrl:'/x'.repeat(1100)})).status,414);
assert.equal(g.decide(ctx({pathname:'/api/items',query:[['page','20001']]})).status,400);
assert.equal(g.decide(ctx({pathname:'/api/items',query:[['page','abc']]})).status,400);
assert.equal(g.decide(ctx({pathname:'/api/items',query:[['q','a'.repeat(101)]]})).status,400);
assert.equal(g.decide(ctx({pathname:'/api/items',query:[['bonusFilters','x'.repeat(1501)]]})).status,400);
assert.equal(g.decide(ctx({pathname:'/api/items',query:[['page','3500']]})).action,'allow','son sayfalara gidilebilmeli');

g=mk();let ok=0,limited=0;for(let i=0;i<36;i++){const d=g.decide(ctx());d.action==='allow'?ok++:limited++}
assert.equal(ok,30);assert.equal(limited,6);
t+=5000;assert.equal(g.decide(ctx()).action,'allow','5 sn sonra yeniden izin verilmeli');
assert.equal(g.decide(ctx({ip:'198.51.100.7'})).action,'allow');
for(let i=0;i<500;i++)assert.equal(g.decide(ctx({ip:'127.0.0.1'})).action,'allow');
for(let i=0;i<300;i++)assert.equal(g.decide(ctx({ip:'192.0.2.9',pathname:'/assets/site/market.abc.js'})).action,'allow');

g=mk({strikeLimit:5,blockMs:60000});
for(let i=0;i<30;i++)g.decide(ctx({ip:'192.0.2.50'}));
for(let i=0;i<10;i++)g.decide(ctx({ip:'192.0.2.50'}));
const blocked=g.decide(ctx({ip:'192.0.2.50'}));assert.equal(blocked.status,429);assert.equal(blocked.reason,'blocked');assert(blocked.retryAfter>0);
t+=61000;assert.equal(g.decide(ctx({ip:'192.0.2.50'})).action,'allow','engel süresi dolunca izin verilmeli');

g=mk({},{API_ORIGIN_CHECK:'1'});
const api=(h)=>g.decide(ctx({pathname:'/api/items',headers:{host:'example.com',...h}}));
assert.equal(api({}).status,403,'başlıksız (curl gibi) istek reddedilmeli');
assert.equal(g.decide(ctx({pathname:'/api/health',headers:{host:'example.com'}})).action,'allow','izleme servisi sağlık adresine başlıksız girebilmeli');
assert.equal(api({'sec-fetch-site':'cross-site'}).status,403);
assert.equal(api({'sec-fetch-site':'same-origin'}).action,'allow');
assert.equal(api({referer:'https://example.com/pazar'}).action,'allow');
assert.equal(api({referer:'https://evil.example/pazar'}).status,403);
assert.equal(g.decide(ctx({ip:'127.0.0.1',pathname:'/api/items'})).action,'allow','yerel (izleme/test) istekleri serbest');

g=mk({},{ALLOWED_HOSTS:'example.com,www.example.com'});
assert.equal(g.decide(ctx({headers:{host:'example.com'}})).action,'allow');
assert.equal(g.decide(ctx({headers:{host:'203.0.113.1'}})).action,'drop');
assert.equal(g.decide(ctx({headers:{host:'evil.example'}})).action,'drop');

g=mk({strikeLimit:3,blockMs:60000});
const exp=t+3600000,cookie=`m2v=${exp}.${g.sign(exp)}`;
let humanOk=0;for(let i=0;i<200;i++)if(g.decide(ctx({ip:'192.0.2.77',headers:{host:'x',cookie}})).action==='allow')humanOk++;
assert.equal(humanOk,90,'doğrulanmış ziyaretçi 30x3 istek alır');
assert.equal(g.decide(ctx({ip:'192.0.2.78',headers:{host:'x',cookie:`m2v=${exp}.${'0'.repeat(32)}`}})).action,'allow');
for(let i=0;i<40;i++)g.decide(ctx({ip:'192.0.2.79',headers:{host:'x',cookie:`m2v=${exp}.${'0'.repeat(32)}`}}));
assert.equal(g.decide(ctx({ip:'192.0.2.79',headers:{host:'x',cookie:`m2v=${exp}.${'0'.repeat(32)}`}})).status,429,'sahte çerez ayrıcalık vermez');

g=mk();
const reqFrom=(peer,hdr)=>({socket:{remoteAddress:peer},headers:hdr});
assert.equal(g.clientIp(reqFrom('127.0.0.1',{'x-real-ip':'203.0.113.9'})),'203.0.113.9');
assert.equal(g.clientIp(reqFrom('198.51.100.4',{'x-real-ip':'203.0.113.9'})),'198.51.100.4','dışarıdan gelen sahte başlık yok sayılmalı');
assert.equal(g.clientIp(reqFrom('127.0.0.1',{'x-real-ip':'not-an-ip'})),'127.0.0.1');
console.log('Koruma katmanı: yöntem, adres, parametre, hız sınırı, engelleme, API kaynak denetimi, sunucu adı, doğrulanmış ziyaretçi ve gerçek adres kontrolleri geçti.');
