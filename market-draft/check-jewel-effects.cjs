const assert=require('node:assert/strict');
const {jewelEffectsBySlot}=require('./jewel-effects.cjs');
assert.deepEqual(jewelEffectsBySlot(['Büyü Hızı +%2','Delici Vuruş için şansı %2','Büyü Hızı +%5','Delici Vuruş için şansı %4','Kalan Süre : 1sa. 54dk.'],3),[['Büyü Hızı +%2','Delici Vuruş için şansı %2'],['Büyü Hızı +%2','Delici Vuruş için şansı %2'],['Büyü Hızı +%5','Delici Vuruş için şansı %4']]);
assert.deepEqual(jewelEffectsBySlot(['Saldırı Hızı +%1','HP yenileme şansı %1','Ninja saldırılarına karşı savunma şansı %1','Saldırı Hızı +%2','Kalan Süre : 4sa. 54dk.'],3)[2],['Saldırı Hızı +%2','HP yenileme şansı %1','Ninja saldırılarına karşı savunma şansı %1']);
assert.deepEqual(jewelEffectsBySlot(['Tamlık 1','1 SungMa iradesi (RES)','Kalan Süre : 9sa. 27dk.','Boş'],2),[['Tamlık 1','1 SungMa iradesi (RES)'],['Tamlık 1','1 SungMa iradesi (RES)']]);
assert.deepEqual(jewelEffectsBySlot(['Büyü Hızı +%2','Delici Vuruş için şansı %2'],1),[['Büyü Hızı +%2','Delici Vuruş için şansı %2']]);
console.log('Cevher yuvası özellik kontrolleri geçti.');
