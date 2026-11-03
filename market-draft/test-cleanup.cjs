const fs=require('node:fs'),{spawn}=require('node:child_process');
const created=new Set(),original=fs.mkdtempSync;
fs.mkdtempSync=function(prefix,...rest){const dir=original.call(fs,prefix,...rest);created.add(dir);return dir};
process.on('exit',()=>{const left=[];for(const dir of created){try{fs.rmSync(dir,{recursive:true,force:true,maxRetries:3,retryDelay:50})}catch{}if(fs.existsSync(dir))left.push(dir)}
 if(left.length){try{const script='const fs=require("fs");const dirs=JSON.parse(process.argv[1]);let n=0;const t=setInterval(()=>{for(const d of dirs){try{fs.rmSync(d,{recursive:true,force:true})}catch{}}if(dirs.every(d=>!fs.existsSync(d))||++n>30)clearInterval(t)},1000)';spawn(process.execPath,['-e',script,JSON.stringify(left)],{detached:true,stdio:'ignore',windowsHide:true}).unref()}catch{}}});
