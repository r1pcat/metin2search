const assert=require('node:assert/strict'),{reconstructDashboard}=require('./reconstruct-dashboard.cjs');
const t=n=>new Date(Date.UTC(2026,9,15,0,n)).toISOString();
const observations=[{listing_key:'a',observed:t(0),seller:'A',won:10,yang:3},{listing_key:'a',observed:t(5),seller:'A',won:12,yang:3},{listing_key:'b',observed:t(10),seller:'A',won:20,yang:7},{listing_key:'c',observed:t(15),seller:'B',won:1,yang:2}];
const lifecycle=[{key:'a',occurred:t(20),is_active:0,id:1},{key:'a',occurred:t(25),is_active:1,id:2},{key:'b',occurred:t(30),is_active:0,id:3}];
const points=reconstructDashboard(observations,lifecycle,{cutoff:Date.parse(t(30))});
assert.equal(points[1].won,12);assert.equal(points[1].listings,1);assert.equal(points[2].sellers,1);assert.equal(points[3].sellers,2);
assert.equal(points[4].won,21);assert.equal(points[4].listings,2);assert.equal(points[5].won,33);assert.equal(points.at(-1).won,13);assert.equal(points.at(-1).yang,5);assert.equal(points.at(-1).sellers,2);
assert.equal(reconstructDashboard(observations,lifecycle,{cutoff:Date.parse(t(5))}).length,2);
console.log('Fiyat değişikliği, tekrar gözlem, satıcı sayımı, kaldırılma ve geri gelme geçmişi doğrulandı.');
