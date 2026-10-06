import {test,expect,request} from '@playwright/test';
import {createServer,connect} from 'node:net';
import {createServer as createHttpServer} from 'node:http';
import {randomUUID,randomBytes,createHash} from 'node:crypto';
import {writeFileSync,renameSync,readFileSync,statSync} from 'node:fs';
import {join} from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {readSettings} from './settings.mjs';
import {scenarios} from './scenarios.mjs';
const s=readSettings(process.env),f=s.fixture,r=s.refs,root='/bff/customer/day-close-preparations';
function proxy(port,target){const sockets=new Set();const server=createServer(client=>{const upstream=connect(target,'127.0.0.1');for(const sock of [client,upstream]){sockets.add(sock);sock.on('close',()=>sockets.delete(sock));sock.on('error',()=>{client.destroy();upstream.destroy();});}client.on('close',()=>upstream.destroy());client.pipe(upstream);upstream.pipe(client);});return {start:()=>new Promise((ok,bad)=>{server.once('error',bad);server.listen(port,'127.0.0.1',ok);}),close:()=>new Promise(ok=>{for(const sock of sockets)sock.destroy();server.close(ok);})};}
const reporting=proxy(s.proxyPort,s.reportingPort),payment=proxy(s.sourceProxyPort,s.paymentPort);
let api,cashier,manager,secondManager,shift,session,order,amount,settlement,receipt,completed,completedIdentity=s.resolver;
test.describe.configure({mode:'serial'});
test.beforeAll(async()=>{api=await request.newContext();await reporting.start();await payment.start();});
test.afterAll(async()=>{cashier=manager=secondManager=undefined;await api?.dispose();await reporting.close();await payment.close();});
async function fixture(action){try{return(await promisify(execFile)('dotnet',[s.fixtureDll,action],{timeout:190000,windowsHide:true})).stdout.trim();}catch{throw new Error('Acceptance fixture failed; diagnostics suppressed.');}}
async function control(action){if(!['stop-pos','start-pos'].includes(action))throw new Error('Invalid POS action.');const requestId=randomUUID().replaceAll('-',''),temp=join(s.controlPath,'request.tmp'),path=join(s.controlPath,'request.json'),ack=join(s.controlPath,'ack.json');writeFileSync(temp,JSON.stringify({runId:s.runId,requestId,action}));renameSync(temp,path);const limit=Date.now()+30000;while(Date.now()<limit){try{if(statSync(ack).size>4096)throw new Error('Oversized runner acknowledgement.');const value=JSON.parse(readFileSync(ack,'utf8').replace(/^\uFEFF/,''));if(value.requestId===requestId&&value.status==='completed')return;}catch(error){if(error.code!=='ENOENT')throw error;}await new Promise(ok=>setTimeout(ok,100));}throw new Error('Owned POS control timed out.');}
async function token(browser,identity){
 const context=await browser.newContext(),page=await context.newPage(),verifier=randomBytes(32).toString('base64url'),state=randomUUID();let callback;
 const listener=createHttpServer((req,res)=>{const u=new URL(req.url,s.callbackURL);if(u.pathname==='/callback'&&u.searchParams.get('state')===state){callback=u;res.writeHead(200,{'Content-Type':'text/plain','Cache-Control':'no-store'});res.end('Authentication complete');}else{res.writeHead(404);res.end();}});
 await new Promise((ok,bad)=>{listener.once('error',bad);listener.listen(Number(new URL(s.callbackURL).port),'127.0.0.1',ok);});
 await context.route('**/*',async route=>{const u=new URL(route.request().url());if([new URL(s.callbackURL).origin,new URL(s.issuer).origin].includes(u.origin))await route.continue();else await route.abort();});
 try{
  const query=new URLSearchParams({client_id:'nexaconnect-pos',redirect_uri:s.callbackURL,response_type:'code',scope:'openid',state,code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256'});
  process.stdout.write('acceptance-stage: pkce-login\n');await page.goto(s.issuer+'/protocol/openid-connect/auth?'+query,{timeout:20000});await expect(page.locator('#username')).toBeVisible({timeout:20000});await page.locator('#username').fill(identity.username);await page.locator('#password').fill(identity.password);await page.locator('#kc-login').click();process.stdout.write('acceptance-stage: pkce-callback\n');await page.waitForURL(u=>u.origin===new URL(s.callbackURL).origin&&u.pathname==='/callback',{timeout:20000});
  if(!callback||callback.searchParams.get('state')!==state||!callback.searchParams.get('code'))throw new Error();
  process.stdout.write('acceptance-stage: pkce-exchange\n');const response=await api.post(s.issuer+'/protocol/openid-connect/token',{form:{grant_type:'authorization_code',client_id:'nexaconnect-pos',redirect_uri:s.callbackURL,code:callback.searchParams.get('code'),code_verifier:verifier},timeout:20000});
  if(response.status()!==200)throw new Error();const data=await response.json();if(!data.access_token)throw new Error();return data.access_token;
 }catch{const current=new URL(page.url());process.stdout.write('acceptance-stage: pkce-failure-'+(current.origin===new URL(s.issuer).origin?'issuer':current.origin===new URL(s.callbackURL).origin?'callback':'other')+'-'+(callback?'seen':'unseen')+'-'+(await page.locator('#input-error').count()?'login-error':'no-login-error')+'\n');throw new Error('PKCE authentication failed; credentials suppressed.');}finally{await context.close();listener.closeAllConnections();await new Promise(ok=>listener.close(ok));}
}
async function send(service,path,bearer,body,method=body?'POST':'GET',organization=f.organizationId){
 const response=await api.fetch(s.urls[service]+path,{method,headers:{...(bearer?{Authorization:'Bearer '+bearer}:{}),'X-Nexa-Organization-Id':organization,'X-Nexa-Application-Code':'nexa_connect','X-Nexa-Terminal-Id':r.terminalId,'X-Correlation-ID':'cashier-'+s.runId},data:body});
 let data;try{data=await response.json();}catch{}return {status:response.status(),data};
}
async function bff(page,path,body){return page.evaluate(async({path,body,root})=>{const headers=body?{'Content-Type':'application/json','X-Nexa-CSRF':(await(await fetch(root+'/csrf')).json()).requestToken}:{};const response=await fetch(path,{method:body?'POST':'GET',headers,body:body?JSON.stringify(body):undefined,cache:'no-store'});let data;try{data=await response.json();}catch{}return {status:response.status,data};},{path,body,root});}
async function signIn(page,identity){const allowed=new Set([s.baseURL,new URL(s.issuer).origin]);await page.context().route('**/*',route=>allowed.has(new URL(route.request().url()).origin)?route.continue():route.abort());await page.goto('/bff/customer/login?returnUrl=%2F');const url=new URL(page.url());if(url.origin!==new URL(s.issuer).origin||url.pathname!==new URL(s.issuer).pathname+'/protocol/openid-connect/auth')throw new Error('Unexpected identity endpoint.');await page.locator('#username').fill(identity.username);await page.locator('#password').fill(identity.password);await page.locator('#kc-login').click();await page.waitForURL(u=>u.origin===s.baseURL);expect((await page.evaluate(async organizationId=>(await fetch('/bff/customer/tenant',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({organizationId,applicationCode:'nexa_connect'})})).status,f.organizationId))).toBe(200);await page.goto('/#end-of-day');await page.reload();await page.getByLabel('End-of-day branch ID').fill(f.branchId);await page.getByLabel('End-of-day business date').fill(f.businessDate);}
const command=version=>({branchId:f.branchId,businessDate:f.businessDate,operationId:randomUUID(),expectedVersion:version,reasonCode:'routine_close'});
const readPath=()=>root+'?'+new URLSearchParams({branchId:f.branchId,businessDate:f.businessDate});
async function loadUi(page){const response=page.waitForResponse(r=>r.url().includes(root)&&!r.url().endsWith('/csrf')&&r.request().method()==='GET');await page.getByRole('button',{name:'Load preparation',exact:true}).click();const result=await response;expect(result.status()).toBe(200);return result.json();}
async function prepareUi(page,name){const request=page.waitForRequest(r=>r.url().endsWith(root)&&r.method()==='POST'),response=page.waitForResponse(r=>r.url().endsWith(root)&&r.request().method()==='POST');await page.getByRole('button',{name,exact:true}).click();const sent=await request,result=await response;return {status:result.status(),data:await result.json(),command:sent.postDataJSON()};}
test(scenarios[0],async({browser})=>{
 cashier=await token(browser,s.reader);manager=await token(browser,s.resolver);secondManager=await token(browser,s.secondManager);
 process.stdout.write('acceptance-stage: reference-commands\n');const open={branchId:f.branchId,storeId:r.storeId,terminalId:r.terminalId,shiftNumber:'COMMAND-'+s.runId.slice(0,8)};
 expect((await send('pos','/api/pos/v1/shifts/open',null,open)).status).toBe(401);
 expect((await send('pos','/api/pos/v1/terminals/enroll',cashier,{branchId:f.branchId,storeId:r.storeId,terminalId:r.terminalId,code:'ACCEPTANCE',deviceType:'pos'})).status).toBe(403);
 expect((await send('pos','/api/pos/v1/terminals/enroll',manager,{branchId:f.branchId,storeId:r.storeId,terminalId:r.terminalId,code:'ACCEPTANCE',deviceType:'pos'})).status).toBe(201);
 expect((await send('pos','/api/pos/v1/shifts/open',cashier,{...open,branchId:f.deniedBranchId})).status).toBe(403);
 const opened=await send('pos','/api/pos/v1/shifts/open',cashier,open);expect(opened.status).toBe(200);shift=opened.data.shiftId;
 const drawer=await send('pos','/api/pos/v1/cash-sessions/open',cashier,{shiftId:shift,storeId:r.storeId,currency:'THB',openingAmount:100});expect(drawer.status).toBe(200);session=drawer.data.cashSessionId;
 expect((await send('catalog',`/api/catalog/v1/branches/${f.branchId}/menu-items`,manager,{productId:r.productId,name:'Acceptance meal',unitPrice:100,currency:'THB',preparationStation:'kitchen'})).status).toBe(201);
 expect((await send('inventory',`/api/inventory/v1/branches/${f.branchId}/stock/${r.productId}`,manager,{quantity:10},'PUT')).status).toBe(200);
 const checkout={organizationId:f.organizationId,restaurantId:f.restaurantId,branchId:f.branchId,currency:'THB',paymentMethod:'cash_manual',idempotencyKey:randomUUID(),lines:[{productId:r.productId,quantity:1}]};
 expect((await send('order','/api/order/v1/orders/quote',cashier,checkout,'POST',f.otherOrganizationId)).status).toBe(403);
 process.stdout.write('acceptance-stage: quote-place\n');const quote=await send('order','/api/order/v1/orders/quote',cashier,checkout);expect(quote.status).toBe(200);amount=quote.data.pricing.totalAmount;expect(amount).toBeGreaterThan(0);
 const placed=await send('order','/api/order/v1/orders/place',cashier,{...checkout,pricingFingerprint:quote.data.fingerprint});expect(placed.status).toBe(200);order=placed.data.orderId;expect(placed.data.totalAmount).toBe(amount);
 const replay=await send('order','/api/order/v1/orders/place',cashier,{...checkout,pricingFingerprint:quote.data.fingerprint});expect(replay.status).toBe(200);expect(replay.data.orderId).toBe(order);
 const stock=await send('inventory',`/api/inventory/v1/branches/${f.branchId}/stock`,manager);expect(stock.status).toBe(200);expect(stock.data.find(item=>item.productId===r.productId).availableQuantity).toBe(9);
});
test(scenarios[1],async()=>{
 settlement={organizationId:f.organizationId,branchId:f.branchId,terminalId:r.terminalId,idempotencyKey:randomUUID(),method:'cash',amount,currency:'THB',receiptConfirmed:true};
 const path=`/api/order/v1/orders/${order}/manual-settlement`;expect((await send('order',path,cashier,settlement)).status).toBe(201);
 const replay=await send('order',path,cashier,settlement);expect(replay.status).toBe(200);expect(replay.data.replayed).toBe(true);expect((await send('order',path,cashier,{...settlement,amount:amount+1})).status).toBe(409);
 const received=await send('order',`/api/order/v1/orders/${order}/receipt?branchId=${f.branchId}`,cashier);expect(received.status).toBe(200);receipt=received.data;
 await expect.poll(async()=> (await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data?.netMovementAmount).toBe(amount);
 const query=new URLSearchParams({branchId:f.branchId,fromUtc:f.fromUtc,toUtc:f.toUtc});await expect.poll(async()=>{const response=await api.get(`http://127.0.0.1:${s.reportingPort}/api/reporting/v1/customer/organizations/${f.organizationId}/reports/sales?${query}`,{headers:{Authorization:'Bearer '+manager,'X-Correlation-ID':'cashier-'+s.runId}});return response.status()===200?(await response.json()).grossSales:undefined;}).toBe(amount);
 expect(await fixture('record')).toBe('observed_complete');
});
test(scenarios[2],async({page})=>{
 const summary=(await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data;
 const close={actualClosingAmount:100+amount-5,expectedConcurrencyVersion:summary.concurrencyVersion};
 expect((await send('pos',`/api/pos/v1/cash-sessions/${session}/close`,cashier,{...close,expectedConcurrencyVersion:close.expectedConcurrencyVersion-1})).status).toBe(409);
 expect((await send('pos',`/api/pos/v1/cash-sessions/${session}/close`,cashier,close)).status).toBe(204);
 expect((await send('pos',`/api/pos/v1/shifts/${shift}/close`,cashier,undefined,'POST')).status).toBe(204);
 writeFileSync(s.clockPath+'.tmp',JSON.stringify({runId:s.runId,mode:'live'}));renameSync(s.clockPath+'.tmp',s.clockPath);
 await signIn(page,s.resolver);await loadUi(page);const blocked=await prepareUi(page,'Prepare day close');expect(blocked.status).toBe(200);expect(blocked.data.status).toBe('blocked');expect(blocked.data.blockers).toContain('pending_cash_reviews');
 const path=`/api/pos/v1/cash-reviews/${session}`,query=new URLSearchParams({organizationId:f.organizationId,branchId:f.branchId,storeId:r.storeId});
 const detail=await send('pos',path+'?'+query,manager);expect(detail.status).toBe(200);expect(detail.data.session.varianceAmount).toBe(-5);
 const decision={organizationId:f.organizationId,branchId:f.branchId,storeId:r.storeId,decision:'approve',reason:'Verified acceptance variance',expectedSessionVersion:detail.data.session.sessionVersion,expectedReviewVersion:detail.data.session.reviewVersion,idempotencyKey:randomUUID()};
 expect((await send('pos',path+'/decisions',cashier,decision)).status).toBe(403);
 expect((await send('pos',path+'/decisions',manager,{...decision,expectedSessionVersion:decision.expectedSessionVersion-1})).status).toBe(409);
 const approved=await send('pos',path+'/decisions',manager,decision);expect(approved.status).toBe(200);expect(approved.data.session.reviewStatus).toBe('approved');
 expect((await send('pos',path+'/decisions',manager,decision)).status).toBe(200);expect((await send('pos',path+'/decisions',manager,{...decision,reason:'Different replay'})).status).toBe(409);
 expect((await send('pos',path+'?'+new URLSearchParams({...Object.fromEntries(query),organizationId:f.otherOrganizationId}),manager)).status).toBe(404);
});
test(scenarios[3],async({page,browser})=>{
 expect(await fixture('record')).toBe('observed_complete');await signIn(page,s.resolver);await loadUi(page);
 const ready=await prepareUi(page,'Refresh preparation');completed=ready.command;expect(ready.status).toBe(200);expect(ready.data.status).toBe('ready_for_review');expect(ready.data.snapshot.grossSales).toBe(amount);expect(ready.data.snapshot.completedRefunds).toBe(0);expect(ready.data.snapshot.cashVariance).toBe(-5);
 expect((await bff(page,root,completed)).data.version).toBe(ready.data.version);expect((await bff(page,root,{...completed,reasonCode:'recheck'})).status).toBe(409);
 const context=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});try{const other=await context.newPage();await signIn(other,s.secondManager);const a=command(ready.data.version),b=command(ready.data.version);const results=await Promise.all([bff(page,root,a),bff(other,root,b)]);expect(results.map(x=>x.status).sort()).toEqual([200,409]);completed=results[0].status===200?a:b;completedIdentity=results[0].status===200?s.resolver:s.secondManager;}finally{await context.close();}
});
test(scenarios[4],async({page})=>{
 await signIn(page,completedIdentity);const before=(await bff(page,readPath())).data;await control('stop-pos');expect((await bff(page,readPath())).status).toBe(503);await control('start-pos');
 const after=await bff(page,readPath());expect(after.status).toBe(200);expect(after.data.status).toBe('ready_for_review');expect(after.data.version).toBe(before.version);expect(after.data.snapshot).toEqual(before.snapshot);expect((await bff(page,root,completed)).data.version).toBe(before.version);
 expect((await send('order',`/api/order/v1/orders/${order}/manual-settlement`,cashier,settlement)).status).toBe(200);
 expect((await send('order',`/api/order/v1/orders/${order}/receipt?branchId=${f.branchId}`,cashier)).data).toEqual(receipt);
 const summary=await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier);expect(summary.data.movements).toHaveLength(1);expect(summary.data.netMovementAmount).toBe(amount);expect(summary.data.status).toBe('closed');
 expect(JSON.parse(await fixture('cashier-proof')).verified).toBe(true);
});
