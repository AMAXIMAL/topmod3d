// Add an App Preview video to an App Store version, first in its set.
// node asc_add_preview.mjs 4.3 IPHONE_65=/path/a.mp4 IPHONE_61=/path/a.mp4 IPAD_PRO_3GEN_129=/path/b.mp4
import {api} from './asc_api.mjs';
import fs from 'fs'; import crypto from 'crypto'; import path from 'path';
const APP='6779650858';
const [,, ver, ...pairs]=process.argv;
const V=(await api('GET',`/v1/apps/${APP}/appStoreVersions?filter[versionString]=${ver}&filter[platform]=IOS`)).j.data[0];
if(!V){ console.log('no version',ver); process.exit(1); }
const loc=(await api('GET',`/v1/appStoreVersions/${V.id}/appStoreVersionLocalizations`)).j.data.find(l=>l.attributes.locale==='en-US');
const sets=(await api('GET',`/v1/appStoreVersionLocalizations/${loc.id}/appPreviewSets?include=appPreviews&limit=50`)).j;
for(const pr of pairs){
  const [type,f]=pr.split('=');
  let set=(sets.data||[]).find(s=>s.attributes.previewType===type);
  if(!set){ const r=await api('POST','/v1/appPreviewSets',{data:{type:'appPreviewSets',attributes:{previewType:type},relationships:{appStoreVersionLocalization:{data:{type:'appStoreVersionLocalizations',id:loc.id}}}}});
    if(r.status>=300){ console.log('set FAIL',type,r.status,JSON.stringify(r.j).slice(0,300)); continue; } set=r.j.data; }
  const existing=(set.relationships&&set.relationships.appPreviews&&set.relationships.appPreviews.data||[]).map(d=>d.id);
  const buf=fs.readFileSync(f), name=path.basename(f);
  const r=await api('POST','/v1/appPreviews',{data:{type:'appPreviews',attributes:{fileName:name,fileSize:buf.length,mimeType:'video/mp4',previewFrameTimeCode:'00:00:12:00'},relationships:{appPreviewSet:{data:{type:'appPreviewSets',id:set.id}}}}});
  if(r.status>=300){ console.log('reserve FAIL',type,r.status,JSON.stringify(r.j).slice(0,400)); continue; }
  const P=r.j.data;
  for(const op of P.attributes.uploadOperations){ const h={}; for(const x of op.requestHeaders||[]) h[x.name]=x.value;
    const u=await fetch(op.url,{method:op.method,headers:h,body:buf.subarray(op.offset,op.offset+op.length)}); if(!u.ok) console.log('chunk',u.status); }
  const md5=crypto.createHash('md5').update(buf).digest('hex');
  const c=await api('PATCH','/v1/appPreviews/'+P.id,{data:{type:'appPreviews',id:P.id,attributes:{uploaded:true,sourceFileChecksum:md5}}});
  console.log('upload',type,name,c.status,c.status>=300?JSON.stringify(c.j).slice(0,300):'');
  const o=await api('PATCH',`/v1/appPreviewSets/${set.id}/relationships/appPreviews`,{data:[P.id,...existing].slice(0,3).map(id=>({type:'appPreviews',id}))});
  console.log('order',type,o.status);
}
