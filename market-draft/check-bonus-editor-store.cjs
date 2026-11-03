const assert=require('node:assert/strict'),fs=require('node:fs');
const {validateSelection,createBonusEditorStore}=require('./bonus-editor-store.cjs');
const stats={1:'HP',15:'Kritik'};
const result=validateSelection({version:1,colors:{1:'#FF0011',15:'#ad4e2b'}},stats);
assert.equal(result.colors[1],'#ff0011');assert(result.savedAt);
for(const input of [{version:1,colors:{1:'#fff'}},{version:1,colors:{1:'#ff0011',15:'red'}},{version:1,colors:{1:'#ff0011',15:'#ad4e2b',999:'#ffffff'}},{version:1,colors:{1:'#ff0011'}}])assert.throws(()=>validateSelection(input,stats));
const handler=createBonusEditorStore(__dirname,stats);
function denied(headers,address){let status;const response={writeHead:s=>status=s,end:()=>{}};handler({method:'POST',headers,socket:{remoteAddress:address}},response,new URL('http://localhost/local/bonus-colors'));return status}
assert.equal(denied({host:'malicious.test'},'127.0.0.1'),403);
assert.equal(denied({host:'127.0.0.1:4180',origin:'https://malicious.test'},'127.0.0.1'),403);
assert.equal(denied({host:'127.0.0.1:4180',origin:'http://127.0.0.1:4180'},'127.0.0.1'),403);
console.log('Renk doğrulama, eksik/bilinmeyen efsun reddi ve yerel kayıt erişim kontrolleri geçti.');
