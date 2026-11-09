import {api} from './asc_api.mjs'; import fs from 'fs'; import os from 'os';
const r=await api('POST','/v1/profiles',{data:{type:'profiles',attributes:{name:'TopMod AppStore 2026-11',profileType:'IOS_APP_STORE'},relationships:{bundleId:{data:{type:'bundleIds',id:'2XHQ6HN5Y8'}},certificates:{data:[{type:'certificates',id:'6WVZHTX7RL'}]}}}});
console.log('status',r.status, r.status>=300?JSON.stringify(r.j).slice(0,500):'');
if(r.status<300){ const a=r.j.data.attributes; const buf=Buffer.from(a.profileContent,'base64');
  for(const d of [os.homedir()+'/Library/MobileDevice/Provisioning Profiles', os.homedir()+'/Library/Developer/Xcode/UserData/Provisioning Profiles']){ fs.mkdirSync(d,{recursive:true}); fs.writeFileSync(d+'/'+a.uuid+'.mobileprovision',buf); }
  console.log('profile',a.name,a.uuid,a.profileState,a.expirationDate); }
