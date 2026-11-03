const assert=require('node:assert/strict'),{bonusAppearance,bonusStyle,bonusHue,bonusFamily,BONUS_PALETTE,BONUS_COLOR_OVERRIDES}=require('./bonus-theme.js');
const hp=[500,1000,1500,2000].map(value=>bonusAppearance({type:1,value,slot:0},[500,2000]));
assert(hp.every(b=>b.color===hp[0].color));assert.deepEqual(hp.map(b=>b.tier),['low','mid','high','max']);assert(hp.every((b,i)=>!i||b.strength>hp[i-1].strength));
assert.notEqual(bonusHue(1),bonusHue(15));assert.equal(bonusHue(214),bonusHue(214));
assert.equal(bonusAppearance({type:1,value:2000,slot:4},[500,2000]).color,hp[3].color,'Color does not depend on row position');
const rare=bonusAppearance({type:214,value:8,slot:5},[3,8]);assert.equal(rare.tier,'max');assert.equal(rare.color,bonusAppearance({type:214,value:3,slot:6},[3,8]).color);
const negative=bonusAppearance({type:72,value:-10,slot:0},[0,60]);assert.equal(negative.tier,'negative');assert.equal(negative.color,bonusAppearance({type:1,value:-5,slot:0}).color);
assert.equal(bonusAppearance({type:72,value:60,slot:0},[0,60]).tier,'max');assert.equal(bonusAppearance({type:999,value:1000,slot:0}).known,false,'Unknown maxima are not invented');
assert.equal(bonusFamily(1),'red');assert.equal(bonusFamily(5),'red');assert.equal(bonusFamily(4),'pink');assert.equal(bonusFamily(72),'purple');
const colors=new Set(Object.values(BONUS_PALETTE).map(p=>p.color));assert.equal(colors.size,7);
for(const color of Object.values(BONUS_COLOR_OVERRIDES))colors.add(color);
for(const [type,color]of Object.entries(BONUS_COLOR_OVERRIDES))assert.equal(bonusAppearance({type,value:1,slot:0}).color,color);
for(let type=1;type<=300;type++)assert(colors.has(bonusAppearance({type,value:10,slot:0}).color));
const fills=hp.map(b=>Number(bonusStyle({},b).match(/--bonus-fill:(\d+)%/)[1]));assert(fills.every((f,i)=>!i||f>fills[i-1]),'Matte fill increases with verified roll');
assert(hp.every(b=>bonusStyle({},b).includes('--bonus-glow:0px')&&bonusStyle({},b).includes('--bonus-text-glow:0px')));
console.log('Semantic matte colors, increasing fill, no glow, stable rare/absorbed colors and negative values passed.');

assert.equal(bonusFamily(15),'ember');assert.equal(bonusFamily(13),'blue');assert.equal(bonusFamily(19),'green');assert.equal(bonusFamily(245),'red');


