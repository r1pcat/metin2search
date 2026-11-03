const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const ctx=vm.createContext({esc:v=>String(v).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')});
const src=fs.readFileSync(path.join(__dirname,'app.js'),'utf8');
for(const name of ['tooltipLineOrder','tooltipText'])vm.runInContext(src.split('\n').find(l=>l.startsWith('function '+name+'(')),ctx);
const order=(lines,type,extra={})=>Array.from(ctx.tooltipLineOrder(lines,new Set(),{ClientDetails:{itemType:type},...extra})).map(i=>lines[i]);
const pet=['Nakliye Kutusu','Petini nakliye kutusuna koyarsan 30 gün boyunca ticareti yapılır hale gelir.','Öfkeli mini Alastor','Seviye 1 (1g)','Pet süresi: 360 saat','Genç(3)','HP +0.9%','Defans +1.7%','SP +1.6%','Kalan Süre : 20g'];
assert.deepEqual(order(pet,35),pet.slice(1));
const fish=['Izgara Ot Sazanı','Bir süre için saldırı hızını arttırır.','Saldırı Hızı: +20','Süre:10Min.'];assert.deepEqual(order(fish,3),fish.slice(1));
const belt=['Kuşak','Bu kuşak bonusları emer.','Emiş oranı: %25','[ Giyilebilir ]','Saldırı değeri 42 - 60','Saldırı Hızı +%2'];assert.deepEqual(order(belt,28),[belt[1],belt[2],belt[4],belt[5],belt[3]]);
const armor=['Zırh','Defans 30','[ Giyilebilir ]','Savaşçı','Hız Taşı+4','Hareket Hızı %30'];assert.deepEqual(order(armor,2,{socketTextStart:4}),armor.slice(1));
assert.equal(ctx.tooltipText('Güçlü\\nZamanla becerilerini artırabilirsin.'),'Güçlü<br>Zamanla becerilerini artırabilirsin.');assert.equal(ctx.tooltipText('<b>\\nHP'),'&lt;b&gt;<br>HP');
const {DatabaseSync}=require('node:sqlite');let checked=0,petCount=0,newlineCount=0;const servers=[];
for(const file of fs.readdirSync(path.join(__dirname,'data/live')).filter(n=>n.endsWith('.sqlite'))){const db=new DatabaseSync(path.join(__dirname,'data/live',file),{readOnly:true});let count=0;for(const r of db.prepare('SELECT detail FROM items').iterate()){const x=JSON.parse(r.detail),d=x.ClientDetails||{},lines=d.lines||[];count++;checked++;if(d.itemType===35&&lines.some(l=>/^Defans\s/.test(l)))petCount++;if(lines.some(l=>String(l).includes('\\n')))newlineCount++;if(d.itemType!=null&&![1,2,28].includes(d.itemType))assert.deepEqual(order(lines,d.itemType),lines.slice(1));}servers.push({server:file.replace('.sqlite',''),records:count});db.close();}
console.log(JSON.stringify({checked,petCount,newlineCount,servers}));
