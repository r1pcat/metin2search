const fs=require('node:fs'),path=require('node:path');
const read=file=>fs.readFileSync(path.join(__dirname,file),'utf8');
const stats={...JSON.parse(read('data/definitions/stat_map.json')),...JSON.parse(read('data/definitions/client-stat-map.json'))};
const output=read('bonus-color-editor.template.html').replace('/*EDITOR_CSS*/',read('bonus-color-editor.css')).replace('/*BONUS_CSS*/',read('bonus-vivid.css')).replace('/*BONUS_JS*/',()=>read('bonus-theme.js').replace(/<\/script/gi,'<\\/script')).replace('/*EDITOR_STATS*/',()=>JSON.stringify(stats).replace(/</g,'\\u003c')).replace('/*EDITOR_JS*/',()=>read('bonus-color-editor.js'));
fs.writeFileSync(path.join(__dirname,'bonus-color-editor.html'),output,'utf8');
console.log(Object.keys(stats).length+' efsun ile bağımsız HTML hazır.');
