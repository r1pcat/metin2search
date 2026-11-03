const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),zlib=require('node:zlib');
const root=__dirname,dir=path.join(root,'assets','site');fs.mkdirSync(dir,{recursive:true});
const report={at:new Date().toISOString(),pages:{}},keep=new Set();
const stripCss=s=>s.replace(/\/\*[\s\S]*?\*\//g,'').replace(/\n{3,}/g,'\n\n');
const stripJs=s=>s.replace(/^[ \t]*\/\*[\s\S]*?\*\/[ \t]*$/gm,'').replace(/^[ \t]*\/\/.*$/gm,'').replace(/\n{3,}/g,'\n\n');
for(const [name,file]of [['market','index.html'],['home','home.html']]){
 let html=fs.readFileSync(path.join(root,file),'utf8'),styles=[],scripts=[],imports=[];
 html=html.replace(/<link\b[^>]*rel="stylesheet"[^>]*href="([^"]+)"[^>]*>/g,(tag,src)=>{if(/^(https?:)?\/\//.test(src))return tag;styles.push(src);return ''});
 html=html.replace(/<script\s+src="([^"]+)"\s*><\/script>/g,(tag,src)=>{if(/^(https?:)?\/\//.test(src))return tag;scripts.push(src);return ''});
 const css=styles.map(src=>fs.readFileSync(path.join(root,src.split(/[?#]/)[0]),'utf8').replace(/@import\s+url\(([^)]+)\);/g,(_,url)=>{imports.push('@import url('+url+');');return ''})).join('\n');
 const bodies={css:[...new Set(imports)].join('\n')+'\n'+stripCss(css),js:scripts.map(src=>';\n'+stripJs(fs.readFileSync(path.join(root,src.split(/[?#]/)[0]),'utf8'))).join('\n')};
 const emitted={};for(const [ext,body]of Object.entries(bodies)){const hash=crypto.createHash('sha256').update(body).digest('hex').slice(0,12),filename=name+'.'+hash+'.'+ext;fs.writeFileSync(path.join(dir,filename),body);emitted[ext]='/assets/site/'+filename;keep.add(filename)}
 html=html.replace('</head>',`<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin><link rel="stylesheet" href="${emitted.css}"></head>`).replace('</body>',`<script src="${emitted.js}"></script></body>`);
 html=html.replace(/<!--(?!\[if)[\s\S]*?-->\s*/g,'');
 fs.writeFileSync(path.join(root,name+'-built.html'),html);
 report.pages[name]={sourceStyles:styles.length,sourceScripts:scripts.length,requestsBefore:styles.length+scripts.length,requestsAfter:2,cssBytes:Buffer.byteLength(bodies.css),jsBytes:Buffer.byteLength(bodies.js),cssBrotliBytes:zlib.brotliCompressSync(bodies.css).length,jsBrotliBytes:zlib.brotliCompressSync(bodies.js).length,assets:emitted};
}
for(const f of fs.readdirSync(dir))if(!keep.has(f))fs.rmSync(path.join(dir,f),{force:true});
fs.writeFileSync(path.join(root,'data','public-build.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
