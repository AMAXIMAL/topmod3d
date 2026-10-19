// App Store Connect API helper (ES256 JWT from the local API key). Usage: node asc_api.mjs GET /v1/apps
import crypto from 'crypto'; import fs from 'fs'; import os from 'os';
const KID='PW399VNN7W', ISS='69d7513b-182f-4cfc-afc2-afc77f297803';
const key=fs.readFileSync(os.homedir()+'/.appstoreconnect/private_keys/AuthKey_'+KID+'.p8','utf8');
const b64=o=>Buffer.from(typeof o==='string'?o:JSON.stringify(o)).toString('base64url');
function token(){ const now=Math.floor(Date.now()/1000); const h=b64({alg:'ES256',kid:KID,typ:'JWT'}), p=b64({iss:ISS,iat:now,exp:now+1100,aud:'appstoreconnect-v1'});
  return h+'.'+p+'.'+crypto.sign('sha256',Buffer.from(h+'.'+p),{key,dsaEncoding:'ieee-p1363'}).toString('base64url'); }
export async function api(method,path,body){ const r=await fetch('https://api.appstoreconnect.apple.com'+path,{method,headers:{Authorization:'Bearer '+token(),'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});
  const t=await r.text(); let j; try{ j=JSON.parse(t); }catch(e){ j=t; } return {status:r.status,j}; }
if(process.argv[1]&&process.argv[1].endsWith('asc_api.mjs')){ const [,,m,p,b]=process.argv; const r=await api(m||'GET',p||'/v1/apps',b?JSON.parse(b):undefined); console.log(r.status, JSON.stringify(r.j,null,1).slice(0,4000)); }
