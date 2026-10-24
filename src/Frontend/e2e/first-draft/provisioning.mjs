import {chromium,request} from '@playwright/test';
import {readFile,writeFile} from 'node:fs/promises';
import {randomBytes,randomUUID,createHash} from 'node:crypto';

const env=process.env,run=env.NEXACONNECT_DRAFT_RUN,mode=env.NEXACONNECT_DRAFT_VERIFY_MODE;
if(!/^[a-f0-9]{32}$/.test(run)||!['initial','repeat','revoked'].includes(mode))throw new Error('Invalid draft verification configuration.');
const state=JSON.parse(await readFile(env.NEXACONNECT_DRAFT_STATE,'utf8'));
const reference=JSON.parse(await readFile(env.NEXACONNECT_DRAFT_REFERENCE,'utf8'));
if(state.runId!==run||reference.runId!==run||!reference.completed||state.status!=='Ready')throw new Error('Invalid owned draft state.');
if(state.realm!==`nexa-review-it-${run}`||state.authority!==`http://127.0.0.1:${state.infrastructure.keycloak}/realms/${state.realm}`)throw new Error('Owned loopback issuer required.');
for(const [name,value] of Object.entries(state.urls)){const url=new URL(value);if(url.hostname!=='127.0.0.1'||url.protocol!==(name==='Bff'?'https:':'http:'))throw new Error('Loopback draft required.');}
const browser=await chromium.launch(),api=await request.newContext({ignoreHTTPSErrors:true});
const assert=(value,message)=>{if(!value)throw new Error(message);};
const rolePasswords={'cashier':env.NEXACONNECT_DRAFT_CASHIER_PASSWORD,'kitchen-operator':env.NEXACONNECT_DRAFT_KITCHEN_PASSWORD,'store-manager':env.NEXACONNECT_DRAFT_MANAGER_PASSWORD,'accountant':env.NEXACONNECT_DRAFT_ACCOUNTANT_PASSWORD};
async function token(role){
 const context=await browser.newContext(),page=await context.newPage(),verifier=randomBytes(32).toString('base64url'),nonce=randomUUID();let callback;
 const redirect='nexaconnect-pos://oauth/callback';
 await page.route('**/login-actions/authenticate*',async route=>{
  const response=await route.fetch({maxRedirects:0});const location=response.headers().location;
  if(location?.startsWith(redirect)){callback=new URL(location);await route.fulfill({status:200,body:'Authentication complete'});}
  else await route.fulfill({response});
 });
 try{
  await page.goto(state.authority+'/protocol/openid-connect/auth?'+new URLSearchParams({client_id:'nexaconnect-pos',redirect_uri:redirect,response_type:'code',scope:'openid',state:nonce,code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256'}));
  await page.locator('#username').fill('draft-'+role);await page.locator('#password').fill(rolePasswords[role]);await page.locator('#kc-login').click();
  await page.getByText('Authentication complete',{exact:true}).waitFor();assert(callback?.searchParams.get('state')===nonce,'PKCE state mismatch');
  const reply=await api.post(state.authority+'/protocol/openid-connect/token',{form:{grant_type:'authorization_code',client_id:'nexaconnect-pos',redirect_uri:redirect,code:callback.searchParams.get('code'),code_verifier:verifier}});
  assert(reply.status()===200,'PKCE exchange failed');const data=await reply.json();const claims=JSON.parse(Buffer.from(data.access_token.split('.')[1],'base64url').toString());
  assert(claims.sub===reference.subjects[role],'Stable identity mismatch');return data.access_token;
 }finally{await context.close();}
}
const scope={organizationId:reference.organizationId,restaurantId:reference.restaurantId,branchId:reference.branchId};
function headers(token){return {Authorization:'Bearer '+token,'X-Nexa-Organization-Id':reference.organizationId,'X-Nexa-Application-Code':'nexa_connect'};}
async function decide(token,permission,granted,extra={}){
 const response=await api.post(new URL('/api/authorization/v1/decisions',state.urls.Authorization).href,{headers:headers(token),data:{...scope,permission,...extra}});
 assert(response.status()===200&&(await response.json()).granted===granted,'Role decision mismatch: '+permission);
}
async function portal(role,capability){
 const context=await browser.newContext({ignoreHTTPSErrors:true}),page=await context.newPage();
 try{
  await page.goto(new URL('/bff/customer/login?returnUrl=/',state.urls.Bff).href);await page.locator('#username').fill('draft-'+role);await page.locator('#password').fill(rolePasswords[role]);await page.locator('#kc-login').click();
  await page.waitForURL(url=>url.origin===new URL(state.urls.Bff).origin&&url.pathname==='/');
  assert((await context.request.post(new URL('/bff/customer/tenant',state.urls.Bff).href,{data:{organizationId:reference.organizationId,applicationCode:'nexa_connect'}})).status()===200,'Tenant selection failed');
  const response=await context.request.get(new URL('/bff/customer/operational-branches?capability='+capability,state.urls.Bff).href);const branches=await response.json();
  const revoked=mode==='revoked'&&role==='kitchen-operator';assert(response.status()===200&&branches.length===(revoked?0:1),'Permission-filtered discovery failed');
  if(!revoked)assert(branches[0].branchId===reference.branchId&&branches[0].currency==='THB'&&branches[0].timeZone==='Asia/Singapore','Branch data mismatch');
  assert((await context.request.post(new URL('/bff/customer/tenant',state.urls.Bff).href,{data:{organizationId:reference.otherOrganizationId,applicationCode:'nexa_connect'}})).status()===403,'Cross-tenant selection accepted');
  if(role==='kitchen-operator'&&!revoked){
   await page.goto(new URL('/#kitchen',state.urls.Bff).href);await page.reload();await page.getByLabel('Kitchen branch',{exact:true}).selectOption(reference.branchId);
   await page.getByLabel('Kitchen station',{exact:true}).selectOption('bar');await page.getByRole('button',{name:'Refresh queue'}).click();await page.getByText('No active tickets on this page.').waitFor();
   await page.getByLabel('Kitchen branch',{exact:true}).selectOption('');assert(await page.getByText('No active tickets on this page.').count()===0,'Branch change retained stale queue');
  }
 }finally{await context.close();}
}
try{
 const tokens={};for(const role of Object.keys(rolePasswords))tokens[role]=await token(role);
 await decide(tokens.cashier,'order.place',true);await decide(tokens.cashier,'kitchen.ticket.transition',false);await decide(tokens.cashier,'pos.day-close.finalize',false);
 await decide(tokens['kitchen-operator'],'kitchen.ticket.transition',mode!=='revoked');await decide(tokens['kitchen-operator'],'order.place',false);await decide(tokens['kitchen-operator'],'inventory.stock.write',false);
 await decide(tokens.accountant,'pos.day-close.read',true);await decide(tokens.accountant,'pos.day-close.finalize',false);await decide(tokens.accountant,'order.payment-review.resolve',false);
 await decide(tokens['store-manager'],'pos.day-close.prepare',true);await decide(tokens['store-manager'],'pos.day-close.late-cash.post',true,{amount:1000,currency:'THB'});await decide(tokens['store-manager'],'pos.day-close.late-cash.post',false,{amount:1001,currency:'THB'});
 for(const role of Object.keys(tokens)){
  await decide(tokens[role],role==='cashier'?'order.place':role==='kitchen-operator'?'kitchen.ticket.read':'pos.day-close.read',false,{restaurantId:reference.deniedRestaurantId,branchId:reference.deniedBranchId});
  await decide(tokens[role],'order.place',false,{organizationId:reference.otherOrganizationId});
 }
 const menuUrl=new URL(`/api/catalog/v1/branches/${reference.branchId}/menu-items`,state.urls.Catalog).href;
 const stockUrl=new URL(`/api/inventory/v1/branches/${reference.branchId}/stock`,state.urls.Inventory).href;
 let response=await api.get(menuUrl,{headers:headers(tokens.cashier)});assert(response.status()===200,'Cashier menu denied');let menu=await response.json();assert(menu.length===3&&menu.every(p=>p.currency==='THB'),'Representative menu missing');
 response=await api.get(stockUrl,{headers:headers(tokens.cashier)});assert(response.status()===200,'Cashier stock denied');let stock=await response.json();assert(stock.length===3,'Coherent stock missing');
 response=await api.get(new URL(`/api/kitchen/v1/branches/${reference.deniedBranchId}/tickets`,state.urls.Kitchen).href,{headers:headers(tokens['kitchen-operator'])});assert(response.status()===403,'Cross-branch queue accepted');
 response=await api.get(menuUrl,{headers:{...headers(tokens.cashier),'X-Nexa-Organization-Id':reference.otherOrganizationId}});assert(response.status()===403,'Cross-tenant menu accepted');
 if(mode==='initial'){
  assert((await api.put(stockUrl+'/'+reference.products[0],{headers:headers(tokens['store-manager']),data:{quantity:97}})).status()===200,'Stock mutation setup failed');
  const product=menu.find(p=>p.productId===reference.products[0]);assert((await api.post(menuUrl,{headers:headers(tokens['store-manager']),data:{...product,unitPrice:88}})).status()===201,'Menu mutation setup failed');
 }else{assert(stock.find(p=>p.productId===reference.products[0]).availableQuantity===97,'Re-provisioning reset stock');assert(menu.find(p=>p.productId===reference.products[0]).unitPrice===88,'Re-provisioning reset menu');}
 await portal('cashier','catalog');await portal('kitchen-operator','kitchen');await portal('store-manager','day-close');await portal('accountant','reports');
 await writeFile(env.NEXACONNECT_DRAFT_REFERENCE+'.'+mode+'.verification.json',JSON.stringify({runId:run,mode,passed:true,realPkceAndOidc:true,roleAndTenantBoundaries:true,financialTransactionsSeeded:false,stockAndMenuPreserved:mode!=='initial'}));
 console.log('Draft '+mode+' access verified.');
}finally{await api.dispose();await browser.close();}
