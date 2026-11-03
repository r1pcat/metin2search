'use strict';
const crypto=require('node:crypto'),net=require('node:net');

const LOOPBACK=new Set(['127.0.0.1','::1','::ffff:127.0.0.1']);
const STATIC_EXT=/\.(?:css|js|svg|png|jpe?g|webp|ico|woff2?|ttf|map|txt|xml)$/i;

function createGuard(options={}){
  const env=options.env||process.env,now=options.now||(()=>Date.now()),fetchImpl=options.fetch||globalThis.fetch;
  const envLimits=e=>{try{const v=JSON.parse(e.GUARD_LIMITS||'{}');return v&&typeof v==='object'?v:{}}catch{return {}}};
  const cfg={
    allowedHosts:String(env.ALLOWED_HOSTS||'').split(',').map(s=>s.trim().toLowerCase()).filter(Boolean),
    apiOriginCheck:env.API_ORIGIN_CHECK==='1',
    turnstileSite:env.TURNSTILE_SITE_KEY||'',turnstileSecret:env.TURNSTILE_SECRET||'',
    secret:env.GUARD_SECRET||crypto.randomBytes(32).toString('hex'),
    limits:{page:{cap:30,rate:.6},api:{cap:150,rate:6},suggest:{cap:60,rate:8},...envLimits(env),...(options.limits||{})},
    strikeLimit:options.strikeLimit??8,strikeWindow:options.strikeWindow??300000,blockMs:options.blockMs??900000,
    maxSse:options.maxSse??4,maxEntries:options.maxEntries??50000,maxPage:options.maxPage??20000,verifiedMs:12*3600*1000
  };
  const buckets=new Map(),strikes=new Map(),sse=new Map();
  const stats={allowed:0,limited:0,blocked:0,denied:0,verified:0};

  const clientIp=req=>{
    const peer=String(req.socket?.remoteAddress||'').replace(/^::ffff:/,'');
    if(LOOPBACK.has(req.socket?.remoteAddress)||peer==='127.0.0.1'){
      const forwarded=String(req.headers['x-real-ip']||req.headers['x-forwarded-for']||'').split(',')[0].trim();
      if(net.isIP(forwarded))return forwarded;
    }
    return peer||'0.0.0.0';
  };
  const isLocal=ip=>ip==='127.0.0.1'||ip==='::1';
  const sign=exp=>crypto.createHmac('sha256',cfg.secret).update('m2v|'+exp).digest('hex').slice(0,32);
  const verified=cookieHeader=>{
    const m=/(?:^|;\s*)m2v=(\d+)\.([a-f0-9]{32})/.exec(cookieHeader||'');if(!m)return false;
    const exp=Number(m[1]);if(!(exp>now()))return false;
    const a=Buffer.from(sign(exp)),b=Buffer.from(m[2]);return a.length===b.length&&crypto.timingSafeEqual(a,b);
  };
  const classify=(method,pathname)=>{
    if(pathname==='/api/events')return 'sse';
    if(pathname==='/api/search-suggestions')return 'suggest';
    if(pathname.startsWith('/api/'))return 'api';
    if(STATIC_EXT.test(pathname)||pathname.startsWith('/assets/'))return null;
    return 'page';
  };
  function take(ip,klass,multiplier){
    const spec=cfg.limits[klass],key=ip+'|'+klass,t=now();let b=buckets.get(key);
    if(!b){if(buckets.size>=cfg.maxEntries)buckets.delete(buckets.keys().next().value);b={tokens:spec.cap*multiplier,last:t};buckets.set(key,b)}
    b.tokens=Math.min(spec.cap*multiplier,b.tokens+(t-b.last)/1000*spec.rate*multiplier);b.last=t;
    if(b.tokens>=1){b.tokens-=1;return true}
    return false;
  }
  function strike(ip){
    const t=now();let s=strikes.get(ip);
    if(!s||t-s.first>cfg.strikeWindow){s={count:0,first:t,blockedUntil:0};if(strikes.size>=cfg.maxEntries)strikes.delete(strikes.keys().next().value);strikes.set(ip,s)}
    s.count++;if(s.count>=cfg.strikeLimit)s.blockedUntil=t+cfg.blockMs;return s;
  }
  function decide(c){
    const ip=c.ip,pathname=c.pathname,method=c.method;
    if(method!=='GET'&&method!=='HEAD'&&!(method==='POST'&&pathname==='/dogrula'))return {action:'deny',status:405,reason:'method'};
    if(String(c.rawUrl||'').length>2048)return {action:'deny',status:414,reason:'url'};
    if(cfg.allowedHosts.length&&!isLocal(ip)){const host=String(c.headers.host||'').toLowerCase().replace(/:\d+$/,'');if(!cfg.allowedHosts.includes(host))return {action:'drop',reason:'host'}}
    const isHuman=verified(c.headers.cookie),mult=isHuman?3:1;
    if(!isLocal(ip)){
      const s=strikes.get(ip);
      if(s&&s.blockedUntil>now()&&!isHuman){stats.blocked++;return {action:'deny',status:429,reason:'blocked',retryAfter:Math.ceil((s.blockedUntil-now())/1000)}}
    }
    if(pathname.startsWith('/api/')){
      for(const [k,v] of c.query){
        if(v.length>1500)return {action:'deny',status:400,reason:'param'};
        if((k==='q'||k==='seller')&&v.length>100)return {action:'deny',status:400,reason:'param'};
        if(k==='page'&&(!/^\d{1,5}$/.test(v)||Number(v)>cfg.maxPage))return {action:'deny',status:400,reason:'page'};
      }
      if(c.query.length>40)return {action:'deny',status:400,reason:'param'};
      if(cfg.apiOriginCheck&&!isLocal(ip)&&pathname!=='/api/health'){
        const site=c.headers['sec-fetch-site'];
        if(site){if(site!=='same-origin'&&site!=='none')return {action:'deny',status:403,reason:'origin'}}
        else{const host=String(c.headers.host||'').toLowerCase(),from=String(c.headers.origin||c.headers.referer||'');let fromHost='';try{fromHost=new URL(from).host.toLowerCase()}catch{}if(!fromHost||fromHost!==host)return {action:'deny',status:403,reason:'origin'}}
      }
    }
    const klass=classify(method,pathname);
    if(klass==='sse'){
      const n=sse.get(ip)||0;if(n>=cfg.maxSse&&!isLocal(ip))return {action:'deny',status:429,reason:'sse',retryAfter:30};
      return {action:'allow',klass,human:isHuman};
    }
    if(klass&&!isLocal(ip)&&!take(ip,klass,mult)){
      const s=strike(ip);stats.limited++;return {action:'deny',status:429,reason:'rate',retryAfter:s.blockedUntil>now()?Math.ceil((s.blockedUntil-now())/1000):Math.ceil(1/cfg.limits[klass].rate)+1};
    }
    stats.allowed++;return {action:'allow',klass,human:isHuman};
  }

  const page=(title,text,extra='')=>`<!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex"><title>${title} · Metin2Search</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#16171b;color:#e6e3dc;font:15px/1.6 system-ui,Segoe UI,sans-serif}main{max-width:460px;padding:28px;text-align:center}h1{margin:0 0 10px;font-size:22px;color:#dcbd7f}p{color:#a6a2a8;margin:0 0 16px}a.b,button{display:inline-block;padding:10px 18px;border-radius:9px;border:0;background:#c9a45e;color:#1d170c;font:600 14px inherit;text-decoration:none;cursor:pointer}</style></head><body><main><h1>${title}</h1><p>${text}</p>${extra}</main></body></html>`;
  const challengeLink=cfg.turnstileSite?'<a class="b" href="/dogrula">Ben robot değilim</a>':'<a class="b" href="/">Ana sayfaya dön</a>';

  function respond(req,res,d){
    const accept=String(req.headers.accept||'');
    const headers={'Cache-Control':'no-store'};if(d.retryAfter)headers['Retry-After']=String(d.retryAfter);
    stats.denied++;
    if(req.url.startsWith('/api/')){res.writeHead(d.status,{...headers,'Content-Type':'application/json; charset=utf-8'});return res.end(JSON.stringify({error:d.status===429?'Çok fazla istek. Lütfen biraz bekleyin.':d.status===403?'Erişim reddedildi.':'Geçersiz istek.'}))}
    if(d.status===429&&accept.includes('text/html')){res.writeHead(429,{...headers,'Content-Type':'text/html; charset=utf-8'});return res.end(res.headOnly?undefined:page('Biraz yavaşlayalım','Çok kısa sürede çok fazla istek geldi. Birkaç saniye bekleyip tekrar deneyin.',challengeLink))}
    res.writeHead(d.status,{...headers,'Content-Type':'text/plain; charset=utf-8'});return res.end(d.status===405?'Method Not Allowed':d.status===414?'URI Too Long':d.status===429?'Too Many Requests':d.status===403?'Forbidden':'Bad Request');
  }

  async function verifyToken(token,ip){
    if(!cfg.turnstileSecret||!token)return false;
    try{
      const body=new URLSearchParams({secret:cfg.turnstileSecret,response:String(token).slice(0,2100),remoteip:ip});
      const r=await fetchImpl('https://challenges.cloudflare.com/turnstile/v0/siteverify',{method:'POST',body,signal:AbortSignal.timeout(6000)});
      const j=await r.json();return j.success===true;
    }catch{return false}
  }
  function challengePage(res,next,failed){
    const csp="default-src 'self'; script-src 'self' https://challenges.cloudflare.com; frame-src https://challenges.cloudflare.com; style-src 'self' 'unsafe-inline'; connect-src 'self' https://challenges.cloudflare.com; img-src 'self' data:; form-action 'self'; base-uri 'self'; frame-ancestors 'none'";
    const safeNext=/^\/(?!\/)[^\s]{0,200}$/.test(next||'')?next:'/';
    const form=`<form method="post" action="/dogrula"><input type="hidden" name="next" value="${safeNext.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}"><div class="cf-turnstile" data-sitekey="${cfg.turnstileSite}" data-theme="dark" style="display:inline-block;margin:6px 0 16px"></div><br><button type="submit">Devam et</button></form><script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>`;
    res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','Content-Security-Policy':csp});
    res.end(page('Küçük bir doğrulama',failed?'Doğrulama tamamlanamadı, lütfen tekrar deneyin.':'Siteyi korumak için bir insan olduğunuzu doğrulamamız gerekiyor.',form));
  }
  async function handleVerify(req,res,url,ip){
    if(!cfg.turnstileSite||!cfg.turnstileSecret){res.writeHead(404);return res.end('Not found')}
    if(req.method==='GET')return challengePage(res,url.searchParams.get('next')||'/',false);
    let raw='';for await(const chunk of req){raw+=chunk;if(raw.length>6000){res.writeHead(413);return res.end()}}
    const form=new URLSearchParams(raw),ok=await verifyToken(form.get('cf-turnstile-response'),ip);
    if(!ok)return challengePage(res,form.get('next')||'/',true);
    const exp=now()+cfg.verifiedMs;stats.verified++;
    const next=/^\/(?!\/)[^\s]{0,200}$/.test(form.get('next')||'')?form.get('next'):'/';
    res.writeHead(303,{Location:next,'Set-Cookie':`m2v=${exp}.${sign(exp)}; Max-Age=${Math.floor(cfg.verifiedMs/1000)}; Path=/; HttpOnly; Secure; SameSite=Lax`,'Cache-Control':'no-store'});return res.end();
  }

  function handle(req,res,url){
    const ip=clientIp(req);req.clientIp=ip;
    const d=decide({ip,method:req.method,pathname:url.pathname,query:[...url.searchParams],headers:req.headers,rawUrl:req.url});
    if(d.action==='drop'){req.socket.destroy();return true}
    if(d.action==='deny'){respond(req,res,d);return true}
    if(url.pathname==='/dogrula'){handleVerify(req,res,url,ip).catch(()=>{if(!res.headersSent){res.writeHead(500);res.end()}});return true}
    if(d.klass==='sse'){sse.set(ip,(sse.get(ip)||0)+1);res.once('close',()=>{const n=(sse.get(ip)||1)-1;if(n<=0)sse.delete(ip);else sse.set(ip,n)})}
    return false;
  }
  const timer=setInterval(()=>{const t=now();for(const [k,b] of buckets)if(t-b.last>600000)buckets.delete(k);for(const [k,s] of strikes)if(t-s.first>cfg.strikeWindow&&s.blockedUntil<t)strikes.delete(k)},60000);timer.unref?.();
  return {handle,decide,clientIp,isLocal,stats,config:cfg,sign};
}
module.exports={createGuard};
