import {api} from './asc_api.mjs';
const V=(await api('GET','/v1/apps/6779650858/appStoreVersions?filter[versionString]=4.3&filter[platform]=IOS')).j.data[0];
const loc=(await api('GET',`/v1/appStoreVersions/${V.id}/appStoreVersionLocalizations`)).j.data.find(l=>l.attributes.locale==='en-US');
const S=(await api('GET',`/v1/appStoreVersionLocalizations/${loc.id}/appPreviewSets?include=appPreviews&limit=50`)).j;
const inc=Object.fromEntries((S.included||[]).map(x=>[x.id,x.attributes]));
for(const set of S.data){ for(const d of set.relationships.appPreviews.data){ const n=inc[d.id].fileName; if(/^TopMod_preview_(iphone_1920x886|ipad_1600x1200)\.mp4$/.test(n)){ const r=await api('DELETE','/v1/appPreviews/'+d.id); console.log('removed old cut',set.attributes.previewType,n,r.status); } } }
