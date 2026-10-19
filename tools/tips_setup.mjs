// Create the one-time donation products (consumable "tips") for TopMod 3D, priced, localized, available everywhere, with a review screenshot.
import {api} from './asc_api.mjs'; import fs from 'fs'; import os from 'os'; import crypto from 'crypto';
const APP='6779650858', AMTS=[1,5,10,25,50,100,250], PRICE={1:'0.99',5:'4.99',10:'9.99',25:'24.99',50:'49.99',100:'99.99',250:'249.99'};
const SHOT=os.homedir()+'/AMAXiMAL/projects/topmod/dist/appstore/tip_review.png', buf=fs.readFileSync(SHOT), md5=crypto.createHash('md5').update(buf).digest('hex');
let terr=[], url='/v1/territories?limit=200'; while(url){ const r=await api('GET',url); terr.push(...r.j.data.map(t=>({type:'territories',id:t.id}))); url=r.j.links&&r.j.links.next?r.j.links.next.replace('https://api.appstoreconnect.apple.com',''):null; }
const existing=(await api('GET',`/v1/apps/${APP}/inAppPurchasesV2?limit=200`)).j.data||[];
for(const a of AMTS){ const pid='com.amaximal.topmodjs.tip'+a; let iap=existing.find(x=>x.attributes.productId===pid), id=iap&&iap.id;
  if(!id){ const r=await api('POST','/v2/inAppPurchases',{data:{type:'inAppPurchases',attributes:{name:'Donation $'+a,productId:pid,inAppPurchaseType:'CONSUMABLE',
      reviewNote:'Optional one-time donation ("tip") to support free development of TopMod 3D. It unlocks nothing — every feature stays free. Open Help > Donate to see the donation sheet.'},relationships:{app:{data:{type:'apps',id:APP}}}}});
    if(r.status>=300){ console.log(a,'create',r.status,JSON.stringify(r.j).slice(0,300)); continue; } id=r.j.data.id; }
  let r=await api('POST','/v1/inAppPurchaseLocalizations',{data:{type:'inAppPurchaseLocalizations',attributes:{locale:'en-US',name:'Donation',description:'A one-time donation to keep TopMod free'},relationships:{inAppPurchaseV2:{data:{type:'inAppPurchases',id}}}}});
  let pp=null; url=`/v2/inAppPurchases/${id}/pricePoints?filter[territory]=USA&limit=200`; while(url&&!pp){ const q=await api('GET',url); pp=(q.j.data||[]).find(x=>x.attributes.customerPrice===PRICE[a]); url=q.j.links&&q.j.links.next?q.j.links.next.replace('https://api.appstoreconnect.apple.com',''):null; }
  const r2=await api('POST','/v1/inAppPurchasePriceSchedules',{data:{type:'inAppPurchasePriceSchedules',relationships:{inAppPurchase:{data:{type:'inAppPurchases',id}},baseTerritory:{data:{type:'territories',id:'USA'}},manualPrices:{data:[{type:'inAppPurchasePrices',id:'${p0}'}]}}},included:[{type:'inAppPurchasePrices',id:'${p0}',attributes:{startDate:null},relationships:{inAppPurchasePricePoint:{data:{type:'inAppPurchasePricePoints',id:pp.id}}}}]});
  const r3=await api('POST','/v1/inAppPurchaseAvailabilities',{data:{type:'inAppPurchaseAvailabilities',attributes:{availableInNewTerritories:true},relationships:{inAppPurchase:{data:{type:'inAppPurchases',id}},availableTerritories:{data:terr}}}});
  const s=await api('POST','/v1/inAppPurchaseAppStoreReviewScreenshots',{data:{type:'inAppPurchaseAppStoreReviewScreenshots',attributes:{fileName:'tip_review.png',fileSize:buf.length},relationships:{inAppPurchaseV2:{data:{type:'inAppPurchases',id}}}}});
  if(s.status<300){ for(const op of s.j.data.attributes.uploadOperations){ const h={}; op.requestHeaders.forEach(x=>h[x.name]=x.value); await fetch(op.url,{method:op.method,headers:h,body:buf.subarray(op.offset,op.offset+op.length)}); }
    await api('PATCH',`/v1/inAppPurchaseAppStoreReviewScreenshots/${s.j.data.id}`,{data:{type:'inAppPurchaseAppStoreReviewScreenshots',id:s.j.data.id,attributes:{uploaded:true,sourceFileChecksum:md5}}}); }
  console.log('$'+a, id, 'loc',r.status,'price',r2.status,'avail',r3.status,'shot',s.status); }
await new Promise(s=>setTimeout(s,6000));
for(const x of ((await api('GET',`/v1/apps/${APP}/inAppPurchasesV2?limit=200`)).j.data||[])) console.log(x.attributes.productId, x.attributes.state);
