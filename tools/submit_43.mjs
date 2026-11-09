// Create TopMod 4.3 in App Store Connect, attach a build, set What's New, queue the tip IAPs, and submit for review.
import {api} from './asc_api.mjs';
const APP='6779650858', VER='4.3', BUILD=process.argv[2]||'54', DRY=process.argv.includes('--dry');
const WHATS=`New in 4.3
• Make v2 and v3 fabrication tools: stacked layers with dowels and sanding lines, slotted grids and radial fins, panels with finger joints, and papercraft nets you can watch unfold. Export sheets as SVG or DXF.
• Hat Tile: cover or split any model with the aperiodic hat tiling.
• New generators and simulations: Mandelbrot, Light Rays, Black Hole, Splash, Pile, Reaction-Diffusion Lines and Form Find.
• New panel tools: Search, Pie menu, Scrub values and a Nodes view of the modifier stack.
• Many fixes and improvements. TopMod stays free.`;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
// 1) build processed?
let build=null; for(let i=0;i<40;i++){ const r=await api('GET',`/v1/builds?filter[app]=${APP}&filter[version]=${BUILD}&filter[preReleaseVersion.version]=${VER}&limit=5`);
  build=(r.j.data||[])[0]; const st=build&&build.attributes.processingState; console.log('build',BUILD,st||'not found yet'); if(st==='VALID') break; if(st==='INVALID'||st==='FAILED'){ process.exit(1); } await sleep(30000); }
if(!build||build.attributes.processingState!=='VALID'){ console.log('build not ready'); process.exit(2); }
if(build.attributes.usesNonExemptEncryption==null){ const r=await api('PATCH',`/v1/builds/${build.id}`,{data:{type:'builds',id:build.id,attributes:{usesNonExemptEncryption:false}}}); console.log('encryption',r.status); }
// 2) version 4.2
let v=((await api('GET',`/v1/apps/${APP}/appStoreVersions?filter[versionString]=${VER}&filter[platform]=IOS`)).j.data||[])[0];
if(!v){ const r=await api('POST','/v1/appStoreVersions',{data:{type:'appStoreVersions',attributes:{platform:'IOS',versionString:VER,releaseType:'AFTER_APPROVAL'},relationships:{app:{data:{type:'apps',id:APP}}}}}); console.log('create version',r.status,JSON.stringify(r.j).slice(0,300)); v=r.j.data; }
console.log('version',v.id,v.attributes.appStoreState);
// 3) What's New on every localization
const locs=(await api('GET',`/v1/appStoreVersions/${v.id}/appStoreVersionLocalizations`)).j.data||[];
for(const l of locs){ const r=await api('PATCH',`/v1/appStoreVersionLocalizations/${l.id}`,{data:{type:'appStoreVersionLocalizations',id:l.id,attributes:{whatsNew:WHATS}}}); console.log('whatsNew',l.attributes.locale,r.status); }
// 4) attach build
{ const r=await api('PATCH',`/v1/appStoreVersions/${v.id}/relationships/build`,{data:{type:'builds',id:build.id}}); console.log('attach build',r.status,r.status>=300?JSON.stringify(r.j).slice(0,300):''); }
// 5) tip IAPs ride along with this version
const iaps=(await api('GET',`/v1/apps/${APP}/inAppPurchasesV2?limit=200`)).j.data||[];
for(const x of iaps){ if(!/tip\d+$/.test(x.attributes.productId)) continue; console.log(x.attributes.productId,x.attributes.state);
  if(!DRY&&x.attributes.state==='READY_TO_SUBMIT'){ const r=await api('POST','/v1/inAppPurchaseSubmissions',{data:{type:'inAppPurchaseSubmissions',relationships:{inAppPurchaseV2:{data:{type:'inAppPurchases',id:x.id}}}}}); console.log('  queue',r.status,r.status>=300?JSON.stringify(r.j).slice(0,240):''); } }
if(DRY){ console.log('dry run — not submitting'); process.exit(0); }
// 6) review submission
let sub=((await api('GET',`/v1/reviewSubmissions?filter[app]=${APP}&filter[platform]=IOS&filter[state]=READY_FOR_REVIEW`)).j.data||[])[0];
if(!sub){ const r=await api('POST','/v1/reviewSubmissions',{data:{type:'reviewSubmissions',attributes:{platform:'IOS'},relationships:{app:{data:{type:'apps',id:APP}}}}}); console.log('create submission',r.status,r.status>=300?JSON.stringify(r.j).slice(0,300):''); sub=r.j.data; }
{ const r=await api('POST','/v1/reviewSubmissionItems',{data:{type:'reviewSubmissionItems',relationships:{reviewSubmission:{data:{type:'reviewSubmissions',id:sub.id}},appStoreVersion:{data:{type:'appStoreVersions',id:v.id}}}}}); console.log('add item',r.status,r.status>=300?JSON.stringify(r.j).slice(0,400):''); }
{ const r=await api('PATCH',`/v1/reviewSubmissions/${sub.id}`,{data:{type:'reviewSubmissions',id:sub.id,attributes:{submitted:true}}}); console.log('submit',r.status,r.status>=300?JSON.stringify(r.j).slice(0,600):'', r.j.data&&r.j.data.attributes.state); }
