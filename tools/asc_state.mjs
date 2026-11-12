import {api} from './asc_api.mjs';
const APP='6779650858';
const v=(await api('GET',`/v1/apps/${APP}/appStoreVersions?filter[platform]=IOS&limit=4`)).j.data||[];
v.forEach(x=>console.log('version',x.attributes.versionString,x.attributes.appStoreState,x.attributes.createdDate));
const s=(await api('GET',`/v1/reviewSubmissions?filter[app]=${APP}&limit=5`)).j.data||[];
s.forEach(x=>console.log('submission',x.id,x.attributes.state,x.attributes.submittedDate));
const iap=(await api('GET',`/v1/apps/${APP}/inAppPurchasesV2?limit=20`)).j.data||[];
iap.forEach(x=>console.log('iap',x.attributes.productId,x.attributes.state));
