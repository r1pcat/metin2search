const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const src=fs.readFileSync(path.join(__dirname,'taslak.js'),'utf8').replace(/\r\n/g,'\n');
const grab=(start,end)=>{const a=src.indexOf(start);assert(a>=0,'not found: '+start);const b=end?src.indexOf(end,a)+end.length:src.indexOf('\n',a);assert(b>a,'no end for '+start);return src.slice(a,b)};
const code=[
  grab('const ELEMENTS='),
  grab('function elementFrom('),
  grab('const elementOf='),
  grab('const elementText='),
  grab('function placeElement(','  return out}'),
  grab('const elementRowHTML='),
].join('\n');
const sandbox={esc:v=>String(v),elImg:e=>`<img ${e.icon}>`};
vm.createContext(sandbox);vm.runInContext(code+';this.api={elementFrom,elementOf,elementText,placeElement,elementRowHTML}',sandbox);
const {elementOf,elementText,placeElement,elementRowHTML}=sandbox.api;
const weapon=(over={})=>({embedded:false,ClientDetails:{element:{text:'Buzun gücü +18%',grade:3,value:18,bonus:8,applyType:101}},
  elementSteps:{power:[6,6,6],attack:[2,3,3],verified:true},displayBonuses:[{text:'Şeytanlara karşı güçlü +%6'}],...over});
const lines=()=>[{t:'Saldırı Hızı +%26',c:'#base'},{t:'Buzun gücü +18%',c:'#yellow'},{t:'Şeytanlara karşı güçlü +%6',c:'#bonus'}];

let out=placeElement(lines(),weapon());
assert.equal(out.length,3);assert.equal(out[1].t,'Buzun gücü +18% (6,6,6)');assert.equal(out[1].c,'#yellow');assert(out[1].el);
assert.deepEqual(out.map(l=>l.t).filter(t=>!/gücü \+18/.test(t)),['Saldırı Hızı +%26','Şeytanlara karşı güçlü +%6']);
out=placeElement(lines(),weapon({elementSteps:{power:[6,6,6],attack:[2,3,3],verified:false}}));
assert.equal(out[1].t,'Buzun gücü +18%');
out=placeElement([{t:'Şimşeklerin gücü +1%',c:'#y'}],weapon({ClientDetails:{element:{text:'Şimşeklerin gücü +1%',grade:1,value:1,bonus:2,applyType:99}},elementSteps:{power:[1,0,0],attack:[2,0,0],verified:true}}));
assert.equal(out[0].t,'Şimşeklerin gücü +1% (1)');
out=placeElement([{t:'Saldırı Hızı +%26',c:'#base'},{t:'Şeytanlara karşı güçlü +%6',c:'#bonus'}],weapon());
assert.deepEqual(out.map(l=>l.t),['Saldırı Hızı +%26','Buzun gücü +18% (6,6,6)','Şeytanlara karşı güçlü +%6']);assert.equal(out[1].c,'#6eaed6');
const before=lines();assert.equal(placeElement(before,weapon({embedded:{name:'x'}})),before);
const none={embedded:false,ClientDetails:{},displayBonuses:[]};assert.equal(elementOf(none),null);assert.equal(placeElement(before,none),before);
const el=elementOf(weapon()),html=elementRowHTML({el,t:'Buzun gücü +18% (6,6,6)',c:'#yellow'});
assert(html.includes('class="elrow"')&&html.includes('Buzun gücü +18% (6,6,6)')&&html.includes('Saldırı artışı: +2 · +3 · +3'));
assert.equal(elementText(sandbox.api.elementFrom({grade:3,value:18,applyType:101},{power:[6,6,6],attack:[0,0,0],verified:true})),'Buz gücü +18% (6,6,6)');
console.log('Element line: game format with verified steps, kept in place above the bonuses; unverified/embedded/none left alone.');
