const crypto=require('node:crypto');
const b64=s=>Buffer.from(s.replace(/-/g,'+').replace(/_/g,'/'),'base64');
function createAdminAuth({team=process.env.CF_ACCESS_TEAM||'',aud=process.env.CF_ACCESS_AUD||'',isLocal,fetchJson,now=()=>Date.now()}){
 const configured=/^[a-z0-9-]{2,63}$/i.test(team)&&/^[a-f0-9]{16,128}$/i.test(aud);
 const issuer=`https://${team}.cloudflareaccess.com`;let keys=null,keysAt=0;
 const get=fetchJson||(async url=>{const r=await fetch(url,{signal:AbortSignal.timeout(5000),redirect:'error'});if(!r.ok)throw Error('JWKS '+r.status);return r.json()});
 async function jwks(){if(keys&&now()-keysAt<3600e3)return keys;const j=await get(issuer+'/cdn-cgi/access/certs');keys=(j.keys||[]).filter(k=>k.kty==='RSA');keysAt=now();return keys}
 async function verifyJwt(token){try{const parts=String(token||'').split('.');if(parts.length!==3||token.length>6000)return false;const header=JSON.parse(b64(parts[0]).toString()),claims=JSON.parse(b64(parts[1]).toString());if(header.alg!=='RS256')return false;
   const key=(await jwks()).find(k=>k.kid===header.kid);if(!key)return false;
   const ok=crypto.verify('RSA-SHA256',Buffer.from(parts[0]+'.'+parts[1]),crypto.createPublicKey({key,format:'jwk'}),b64(parts[2]));if(!ok)return false;
   const audOk=Array.isArray(claims.aud)?claims.aud.includes(aud):claims.aud===aud;
   return audOk&&claims.iss===issuer&&Number(claims.exp)*1000>now()&&(claims.nbf==null||Number(claims.nbf)*1000<=now()+60000)}catch{return false}}
 async function allowed(req){if(isLocal(req.clientIp)&&!req.headers['cf-connecting-ip'])return true;if(!configured)return false;return verifyJwt(req.headers['cf-access-jwt-assertion'])}
 return {allowed,verifyJwt,configured};
}
module.exports={createAdminAuth};
