import {chromium,request} from '@playwright/test';
import {readFile,writeFile} from 'node:fs/promises';
import {randomBytes,randomUUID,createHash} from 'node:crypto';
const env=process.env,id=env.NEXACONNECT_LAB_ID;
if(!/^[a-f0-9]{32}$/.test(id))throw Error('Invalid lab ID');
const state=JSON.parse(await readFile(env.NEXACONNECT_LAB_STATE,'utf8'));
const reference=JSON.parse(await readFile(env.NEXACONNECT_LAB_REFERENCE,'utf8'));
if(state.runId!==id||reference.runId!==id||state.realm!==`nexa-review-it-${id}`||state.status!=='Ready'||!reference.recoveryLabAccessCompleted)throw Error('Owned lab required');
if(state.authority!==`http://127.0.0.1:${state.infrastructure.keycloak}/realms/${state.realm}`)throw Error('Owned loopback issuer required');
const assert=(value,message)=>{if(!value)throw Error(message);};
let checks=0;const verify=(value,message)=>{assert(value,message);checks++;};
const accounts=[['draft-cashier','cashier','CASHIER'],['lab-cashier-other','lab-cashier-other','OTHER'],['draft-store-manager','store-manager','MANAGER'],['draft-accountant','accountant','RESTRICTED'],['lab-wrong-branch','lab-wrong-branch','WRONG']];
const browser=await chromium.launch(),api=await request.newContext();
const tokens={};
async function login(username,subjectKey,passwordKey){
 const context=await browser.newContext(),page=await context.newPage(),verifier=randomBytes(32).toString('base64url'),nonce=randomUUID();let callback;
 const redirect='nexaconnect-pos://oauth/callback';
 await page.route('**/login-actions/authenticate*',async route=>{
  const response=await route.fetch({maxRedirects:0}),location=response.headers().location;
  if(location?.startsWith(redirect)){callback=new URL(location);await route.fulfill({status:200,body:'Lab authentication complete'});}else await route.fulfill({response});
 });
 try{
  await page.goto(state.authority+'/protocol/openid-connect/auth?'+new URLSearchParams({client_id:'nexaconnect-pos',redirect_uri:redirect,response_type:'code',scope:'openid',state:nonce,code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256',prompt:'login'}));
  await page.locator('#username').fill(username);await page.locator('#password').fill(env['NEXACONNECT_LAB_PASSWORD_'+passwordKey]);await page.locator('#kc-login').click();
  await page.getByText('Lab authentication complete',{exact:true}).waitFor();verify(callback?.searchParams.get('state')===nonce,'PKCE state mismatch');
  const response=await api.post(state.authority+'/protocol/openid-connect/token',{form:{grant_type:'authorization_code',client_id:'nexaconnect-pos',redirect_uri:redirect,code:callback.searchParams.get('code'),code_verifier:verifier}});
  verify(response.status()===200,'PKCE exchange failed');const token=await response.json();
  const user=await api.get(state.authority+'/protocol/openid-connect/userinfo',{headers:{Authorization:'Bearer '+token.access_token}});
  verify(user.status()===200&&(await user.json()).sub===reference.subjects[subjectKey],'Provider identity mismatch');return token;
 }finally{await context.close();}
}
const headers=t=>({Authorization:'Bearer '+t.access_token,'X-Nexa-Organization-Id':reference.organizationId,'X-Nexa-Application-Code':'nexa_connect'});
try{
 for(const [username,key,password] of accounts)tokens[key]=await login(username,key,password);
 for(const [,key] of accounts){
  const response=await api.post(state.authority+'/protocol/openid-connect/token',{form:{grant_type:'refresh_token',client_id:'nexaconnect-pos',refresh_token:tokens[key].refresh_token}});
  verify(response.status()===200,'Real lab refresh failed');tokens[key]=await response.json();
 }
 for(const [key,granted] of [['cashier',true],['lab-cashier-other',true],['store-manager',true],['accountant',false],['lab-wrong-branch',false]]){
  const response=await api.post(new URL('/api/authorization/v1/decisions',state.urls.Authorization).href,{headers:headers(tokens[key]),data:{organizationId:reference.organizationId,restaurantId:reference.restaurantId,branchId:reference.branchId,permission:'order.place'}});
  verify(response.status()===200&&(await response.json()).granted===granted,'Real scoped access decision mismatch');
 }
 const menu=new URL(`/api/catalog/v1/branches/${reference.branchId}/menu-items`,state.urls.Catalog).href;
 verify((await api.get(menu,{headers:headers(tokens.cashier)})).status()===200,'Original cashier menu denied');
 verify((await api.get(menu,{headers:headers(tokens['lab-wrong-branch'])})).status()===403,'Wrong branch menu accepted');
 const ended=await api.post(`http://127.0.0.1:${state.infrastructure.keycloak}/admin/realms/${state.realm}/users/${reference.subjects.cashier}/logout`,{headers:{Authorization:'Bearer '+env.NEXACONNECT_LAB_ADMIN_TOKEN}});
 verify(ended.status()===204,'Owned cashier session termination failed');
 const refreshed=await api.post(state.authority+'/protocol/openid-connect/token',{form:{grant_type:'refresh_token',client_id:'nexaconnect-pos',refresh_token:tokens.cashier.refresh_token}});
 verify([400,401].includes(refreshed.status()),'Ended real session refreshed unexpectedly');
 await writeFile(env.NEXACONNECT_LAB_EVIDENCE,JSON.stringify({version:1,labId:id,kind:'automated-real-oidc-lab-smoke',passed:true,checks,realPkce:true,providerUserInfo:true,scopedDecisions:true,revokedRefreshDenied:true,financialTransactionsSeeded:false,operatorAcceptance:false,releaseReady:false,productionVerified:false}));
 console.log(`PASS: ${checks} real lab PKCE/identity/scope/revocation checks; no operator cases recorded.`);
}finally{await api.dispose();await browser.close();}
