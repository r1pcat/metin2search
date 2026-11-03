function jewelEffectsBySlot(lines,count){
 const variants=new Map();
 for(const raw of lines){const text=String(raw).trim();if(!text||/Kalan Süre|Süre:|^Boş$/i.test(text))continue;
 const key=text.replace(/[+-]?\d+(?:[.,]\d+)?/g,'#');if(!variants.has(key))variants.set(key,[]);
 const values=variants.get(key);if(!values.includes(text))values.push(text);
 }
 return Array.from({length:count},(_,slot)=>[...variants.values()].map(values=>values.length===1?values[0]:slot===count-1?values.at(-1):values[0]));
}
module.exports={jewelEffectsBySlot};
