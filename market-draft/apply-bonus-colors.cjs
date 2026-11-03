const fs=require('node:fs'),path=require('node:path'),{validateSelection}=require('./bonus-editor-store.cjs');
const read=file=>JSON.parse(fs.readFileSync(path.join(__dirname,file),'utf8'));
const selection=read('data/bonus-color-selection.json'),stats={...read('data/definitions/stat_map.json'),...read('data/definitions/client-stat-map.json')};
const {colors}=validateSelection(selection,stats),file=path.join(__dirname,'bonus-theme.js'),source=fs.readFileSync(file,'utf8');
if(!/const BONUS_COLOR_OVERRIDES=\{[^\n]*\};/.test(source))throw Error('Tema renk alanı bulunamadı.');
const previous=require('./bonus-theme.js');let changed=0;
for(const [type,color]of Object.entries(colors))if(previous.bonusAppearance({type,value:1,slot:0}).color!==color)changed++;
fs.writeFileSync(file,source.replace(/const BONUS_COLOR_OVERRIDES=\{[^\n]*\};/,'const BONUS_COLOR_OVERRIDES='+JSON.stringify(colors)+';'),'utf8');
console.log(Object.keys(colors).length+' efsun uygulandı; '+changed+' rengin görünümü değişti. Kaynak kayıt: '+selection.savedAt);
