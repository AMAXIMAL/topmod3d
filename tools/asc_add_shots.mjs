// Add screenshots to an App Store version (creates the version if needed) and move them to the front of each set.
// node asc_add_shots.mjs 4.3 APP_IPHONE_61=/path/a.png,/path/b.png APP_IPAD_PRO_3GEN_129=/path/c.png ...
import {api} from './asc_api.mjs';
import fs from 'fs'; import crypto from 'crypto'; import path from 'path';
const APP='6779650858';
const [,, ver, ...pairs]=process.argv;
const log=(...a)=>console.log(...a);
let V=(await api('GET',`/v1/apps/${APP}/appStoreVersions?filter[versionString]=${ver}&filter[platform]=IOS`)).j.data[0];
if(!V){ const r=await api('POST','/v1/appStoreVersions',{data:{type:'appStoreVersions',attributes:{platform:'IOS',versionString:ver,releaseType:'AFTER_APPROVAL'},relationships:{app:{data:{type:'apps',id:APP}}}}});
  log('create version',r.status, r.status>=300?JSON.stringify(r.j).slice(0,300):''); V=r.j.data; await new Promise(r=>setTimeout(r,4000)); }
log('version',V.id,V.attributes.versionString,V.attributes.appStoreState);
const loc=(await api('GET',`/v1/appStoreVersions/${V.id}/appStoreVersionLocalizations`)).j.data.find(l=>l.attributes.locale==='en-US');
const sets=(await api('GET',`/v1/appStoreVersionLocalizations/${loc.id}/appScreenshotSets?include=appScreenshots&limit=50`)).j;
for(const pr of pairs){
  const [type,list]=pr.split('='); const files=list.split(',');
  let set=(sets.data||[]).find(s=>s.attributes.screenshotDisplayType===type);
  if(!set){ const r=await api('POST','/v1/appScreenshotSets',{data:{type:'appScreenshotSets',attributes:{screenshotDisplayType:type},relationships:{appStoreVersionLocalization:{data:{type:'appStoreVersionLocalizations',id:loc.id}}}}}); set=r.j.data; log('new set',type,r.status); }
  const existing=(set.relationships&&set.relationships.appScreenshots&&set.relationships.appScreenshots.data||[]).map(d=>d.id);
  const added=[];
  for(const f of files){
    const buf=fs.readFileSync(f), name=path.basename(f);
    const r=await api('POST','/v1/appScreenshots',{data:{type:'appScreenshots',attributes:{fileName:name,fileSize:buf.length},relationships:{appScreenshotSet:{data:{type:'appScreenshotSets',id:set.id}}}}});
    if(r.status>=300){ log('reserve FAIL',type,name,r.status,JSON.stringify(r.j).slice(0,400)); continue; }
    const S=r.j.data;
    for(const op of S.attributes.uploadOperations){ const h={}; for(const x of op.requestHeaders||[]) h[x.name]=x.value;
      const u=await fetch(op.url,{method:op.method,headers:h,body:buf.subarray(op.offset,op.offset+op.length)}); if(!u.ok) log('chunk',u.status); }
    const md5=crypto.createHash('md5').update(buf).digest('hex');
    const c=await api('PATCH','/v1/appScreenshots/'+S.id,{data:{type:'appScreenshots',id:S.id,attributes:{uploaded:true,sourceFileChecksum:md5}}});
    log('upload',type,name,c.status); added.push(S.id);
  }
  // new ones first, then what was there (max 10 per set)
  const order=[...added,...existing].slice(0,10);
  const o=await api('PATCH',`/v1/appScreenshotSets/${set.id}/relationships/appScreenshots`,{data:order.map(id=>({type:'appScreenshots',id}))});
  log('order',type,o.status, existing.length+added.length>10?'(set over 10 — oldest dropped from order)':'');
}
