const LINE=/^(\s*Kalan\s+S[üu]re\s*:\s*)(.*?)\s*$/i,UNITS=[['g',86400,/(\d+)\s*g(?![a-zA-Zçğıöşü])/i,'g'],['sa',3600,/(\d+)\s*sa\.?/i,'sa.'],['dk',60,/(\d+)\s*dk\.?/i,'dk.'],['sn',1,/(\d+)\s*sn\.?/i,'sn.']];
const EXPIRED='Süresi dolmuş olabilir';
function parseSeconds(text){let total=0,found=0,smallest=0;for(const [,size,re] of UNITS){const m=re.exec(text);if(m){total+=Number(m[1])*size;found++;smallest=size}}
 const rest=text.replace(/\d+\s*(?:g|sa\.?|dk\.?|sn\.?)(?![a-zA-Zçğıöşü])/gi,'').trim();return found&&!rest?{total,granularity:smallest}:null}
function format(seconds,granularity){const g=Math.max(granularity,1);let left=Math.max(g,Math.floor(seconds/g)*g);const out=[];for(const [,size,,label] of UNITS){if(size<g)continue;const n=Math.floor(left/size);left-=n*size;if(n)out.push(n+label)}return out.join(' ')}
function adjustLine(line,observedMs,nowMs=Date.now()){if(typeof line!=='string'||!Number.isFinite(observedMs))return line;const m=LINE.exec(line);if(!m)return line;
 const parsed=parseSeconds(m[2]);if(!parsed)return line;const elapsed=Math.floor((nowMs-observedMs)/1000);if(elapsed<=0)return line;
 const left=parsed.total-elapsed;return m[1]+(left<=0?EXPIRED:format(left,parsed.granularity))}
function adjustItem(d,nowMs=Date.now()){const lines=d?.ClientDetails?.lines;if(!Array.isArray(lines)||!lines.some(l=>typeof l==='string'&&LINE.test(l)))return d;
 const observed=Date.parse(d.observed);if(!Number.isFinite(observed))return d;
 d.ClientDetails={...d.ClientDetails,lines:lines.map(l=>adjustLine(l,observed,nowMs))};return d}
module.exports={adjustLine,adjustItem,parseSeconds,EXPIRED};
