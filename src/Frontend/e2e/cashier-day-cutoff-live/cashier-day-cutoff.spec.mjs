import {test,expect,request} from '@playwright/test';
import {createServer,connect} from 'node:net';
import {createServer as createHttpServer,request as httpRequest} from 'node:http';
import {randomUUID,randomBytes,createHash} from 'node:crypto';
import {writeFileSync,renameSync,readFileSync,statSync} from 'node:fs';
import {join} from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {readSettings} from './settings.mjs';
import {scenarios} from './scenarios.mjs';
const s=readSettings(process.env),f=s.fixture,r=s.refs,root='/bff/customer/day-close-cutoffs';
function faultProxy(port,target){
 const sockets=new Set(),pending=new Set();let enabled=true,held=false;
 const server=createHttpServer((req,res)=>{
  if(!enabled){req.socket.destroy();return;}
  const forward=()=>{pending.delete(forward);if(res.destroyed)return;
   const upstream=httpRequest({hostname:'127.0.0.1',port:target,path:req.url,method:req.method,headers:req.headers},response=>{res.writeHead(response.statusCode,response.headers);response.pipe(res);});
   upstream.on('socket',sock=>{sockets.add(sock);sock.on('close',()=>sockets.delete(sock));});upstream.on('error',()=>{if(!res.destroyed){res.writeHead(503);res.end();}});
   res.on('close',()=>upstream.destroy());req.pipe(upstream);
  };
  if(held&&req.method==='POST'&&req.url==='/api/payment/v1/customer/day-cutoffs'){pending.add(forward);res.on('close',()=>pending.delete(forward));}else forward();
 });
 server.on('connection',sock=>{sockets.add(sock);sock.on('close',()=>sockets.delete(sock));sock.on('error',()=>sock.destroy());});
 return {start:()=>new Promise((ok,bad)=>{server.once('error',bad);server.listen(port,'127.0.0.1',ok);}),hold:()=>{held=true;},pending:()=>pending.size,
  release:()=>{held=false;for(const next of [...pending])next();},outage:()=>{enabled=false;for(const sock of sockets)sock.destroy();},restore:()=>{enabled=true;},
  close:async()=>{held=false;for(const sock of sockets)sock.destroy();pending.clear();await new Promise(ok=>server.close(ok));}};
}
const reporting=faultProxy(s.proxyPort,s.reportingPort),payment=faultProxy(s.sourceProxyPort,s.paymentPort);
let api,cashier,manager,secondManager,shift,session,order,amount,settlement,receipt,completed,completedIdentity=s.resolver;
test.describe.configure({mode:'serial'});
test.beforeAll(async()=>{api=await request.newContext();await reporting.start();await payment.start();});
test.afterAll(async()=>{cashier=manager=secondManager=undefined;await api?.dispose();await reporting.close();await payment.close();});
async function fixture(action){try{return(await promisify(execFile)('dotnet',[s.fixtureDll,action],{timeout:190000,windowsHide:true})).stdout.trim();}catch{throw new Error('Acceptance fixture failed; diagnostics suppressed.');}}
async function control(action){if(!['stop-pos','start-pos','stop-order','start-order-delivery'].includes(action))throw new Error('Invalid POS action.');const requestId=randomUUID().replaceAll('-',''),temp=join(s.controlPath,'request.tmp'),path=join(s.controlPath,'request.json'),ack=join(s.controlPath,'ack.json');writeFileSync(temp,JSON.stringify({runId:s.runId,requestId,action}));renameSync(temp,path);const limit=Date.now()+30000;while(Date.now()<limit){try{if(statSync(ack).size>4096)throw new Error('Oversized runner acknowledgement.');const value=JSON.parse(readFileSync(ack,'utf8').replace(/^\uFEFF/,''));if(value.requestId===requestId&&value.status==='completed')return;}catch(error){if(error.code!=='ENOENT')throw error;}await new Promise(ok=>setTimeout(ok,100));}throw new Error('Owned POS control timed out.');}
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
async function loadUi(page){const response=page.waitForResponse(r=>r.url().includes(root)&&!r.url().endsWith('/csrf')&&r.request().method()==='GET');await page.getByRole('button',{name:'Load cutoff evidence',exact:true}).click();const result=await response;expect(result.status()).toBe(200);return result.json();}
async function prepareUi(page,name){const request=page.waitForRequest(r=>r.url().endsWith(root)&&r.method()==='POST'),response=page.waitForResponse(r=>r.url().endsWith(root)&&r.request().method()==='POST');await page.getByRole('button',{name,exact:true}).click();const sent=await request,result=await response;return {status:result.status(),data:await result.json(),command:sent.postDataJSON()};}
test(scenarios[0],async({browser})=>{
 cashier=await token(browser,s.reader);manager=await token(browser,s.resolver);secondManager=await token(browser,s.secondManager);
 process.stdout.write('acceptance-stage: reference-commands\n');const open={branchId:f.branchId,storeId:r.storeId,terminalId:r.terminalId,shiftNumber:'COMMAND-'+s.runId.slice(0,8)};
 expect((await send('pos','/api/pos/v1/shifts/open',null,open)).status).toBe(401);
 expect((await send('pos','/api/pos/v1/terminals/enroll',cashier,{branchId:f.branchId,storeId:r.storeId,terminalId:r.terminalId,code:'acceptance',deviceType:'pos'})).status).toBe(403);
 expect((await send('pos','/api/pos/v1/terminals/enroll',manager,{branchId:f.branchId,storeId:r.storeId,terminalId:r.terminalId,code:'acceptance',deviceType:'pos'})).status).toBe(201);
 expect((await send('pos','/api/pos/v1/shifts/open',cashier,{...open,branchId:f.deniedBranchId})).status).toBe(403);
 const opened=await send('pos','/api/pos/v1/shifts/open',cashier,open);expect(opened.status).toBe(200);shift=opened.data.shiftId;
 const drawer=await send('pos','/api/pos/v1/cash-sessions/open',cashier,{shiftId:shift,storeId:r.storeId,currency:'THB',openingAmount:100});expect(drawer.status).toBe(200);session=drawer.data.cashSessionId;
 expect((await send('catalog',`/api/catalog/v1/branches/${f.branchId}/menu-items`,manager,{productId:r.productId,name:'Acceptance meal',unitPrice:100,currency:'THB',preparationStation:'kitchen'})).status).toBe(201);
 expect((await send('inventory',`/api/inventory/v1/branches/${f.branchId}/stock/${r.productId}`,manager,{quantity:10},'PUT')).status).toBe(200);
 const checkout={organizationId:f.organizationId,restaurantId:f.restaurantId,branchId:f.branchId,currency:'THB',paymentMethod:'cash_manual',idempotencyKey:randomUUID(),lines:[{productId:r.productId,quantity:1}]};
 expect((await send('order','/api/order/v1/workflows/quote',cashier,checkout,'POST',f.otherOrganizationId)).status).toBe(403);
 process.stdout.write('acceptance-stage: quote-place\n');const quote=await send('order','/api/order/v1/workflows/quote',cashier,checkout);expect(quote.status).toBe(200);amount=quote.data.pricing.totalAmount;expect(amount).toBeGreaterThan(0);
 const placed=await send('order','/api/order/v1/workflows/place',cashier,{...checkout,pricingFingerprint:quote.data.fingerprint});expect(placed.status).toBe(200);order=placed.data.orderId;expect(placed.data.totalAmount).toBe(amount);
 const replay=await send('order','/api/order/v1/workflows/place',cashier,{...checkout,pricingFingerprint:quote.data.fingerprint});expect(replay.status).toBe(200);expect(replay.data.orderId).toBe(order);
 const stock=await send('inventory',`/api/inventory/v1/branches/${f.branchId}/stock`,manager);expect(stock.status).toBe(200);expect(stock.data.find(item=>item.productId===r.productId).availableQuantity).toBe(9);
});
const window=()=>({organizationId:f.organizationId,restaurantId:f.restaurantId,branchId:f.branchId,fromUtc:f.fromUtc,toUtc:f.toUtc});
const sourcePath=(service,id,w=window())=>`/api/${service}/v1/customer/day-cutoffs/${id}?${new URLSearchParams(w)}`;
async function proof(){return JSON.parse(await fixture('cutoff-proof'));}
async function review(){
 const path=`/api/pos/v1/cash-reviews/${session}`,query=new URLSearchParams({organizationId:f.organizationId,branchId:f.branchId,storeId:r.storeId});
 const detail=await send('pos',path+'?'+query,manager);expect(detail.status).toBe(200);
 const decision={organizationId:f.organizationId,branchId:f.branchId,storeId:r.storeId,decision:'approve',reason:'Verified acceptance variance',expectedSessionVersion:detail.data.session.sessionVersion,expectedReviewVersion:detail.data.session.reviewVersion,idempotencyKey:randomUUID()};
 expect((await send('pos',path+'/decisions',cashier,decision)).status).toBe(403);
 expect((await send('pos',path+'/decisions',manager,{...decision,expectedSessionVersion:decision.expectedSessionVersion-1})).status).toBe(409);
 const accepted=await send('pos',path+'/decisions',manager,decision);expect(accepted.status).toBe(200);expect(accepted.data.session.reviewStatus).toBe('approved');
 expect((await send('pos',path+'/decisions',manager,decision)).status).toBe(200);expect((await send('pos',path+'/decisions',manager,{...decision,reason:'Changed replay'})).status).toBe(409);
 return accepted.data.session;
}
function clock(mode){writeFileSync(s.clockPath+'.tmp',JSON.stringify(mode==='live'?{runId:s.runId,mode}:{runId:s.runId,mode,atUtc:new Date(Date.parse(f.fromUtc)+10*3600000).toISOString()}));renameSync(s.clockPath+'.tmp',s.clockPath);}
async function ready(page){const result=await loadUi(page);expect(result.status).toBe('ready_for_review');expect(result.snapshot.cutoff.sourcesCurrent).toBe(true);expect(result.snapshot.cutoff.financialGaps).toBe(0);return result;}
async function refresh(page){await loadUi(page);const result=await prepareUi(page,'Refresh cutoff evidence');expect(result.status).toBe(200);expect(result.data.status).toBe('ready_for_review');completed=result.command;completedIdentity=s.resolver;return result.data;}
async function reportingTotal(){const query=new URLSearchParams({branchId:f.branchId,fromUtc:f.fromUtc,toUtc:f.toUtc});const response=await api.get(`http://127.0.0.1:${s.reportingPort}/api/reporting/v1/customer/organizations/${f.organizationId}/reports/sales?${query}`,{headers:{Authorization:'Bearer '+manager,'X-Correlation-ID':'cashier-'+s.runId}});return response.status()===200?(await response.json()).grossSales:undefined;}
let undelivered;
test(scenarios[1],async()=>{
 settlement={organizationId:f.organizationId,branchId:f.branchId,terminalId:r.terminalId,idempotencyKey:randomUUID(),method:'cash',amount,currency:'THB',receiptConfirmed:true};
 const path=`/api/order/v1/orders/${order}/manual-settlement`;expect((await send('order',path,cashier,settlement)).status).toBe(201);
 const replay=await send('order',path,cashier,settlement);expect(replay.status).toBe(200);expect(replay.data.replayed).toBe(true);expect((await send('order',path,cashier,{...settlement,amount:amount+1})).status).toBe(409);
 const received=await send('order',`/api/order/v1/orders/${order}/receipt?branchId=${f.branchId}`,cashier);expect(received.status).toBe(200);receipt=received.data;
 expect((await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data.netMovementAmount).toBe(0);expect(await reportingTotal()).toBe(0);
});
test(scenarios[2],async({page})=>{
 const summary=(await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data;
 const close={actualClosingAmount:100+amount-5,expectedConcurrencyVersion:summary.concurrencyVersion};
 expect((await send('pos',`/api/pos/v1/cash-sessions/${session}/close`,cashier,{...close,expectedConcurrencyVersion:close.expectedConcurrencyVersion+1})).status).toBe(409);
 expect((await send('pos',`/api/pos/v1/cash-sessions/${session}/close`,cashier,close)).status).toBe(204);expect((await send('pos',`/api/pos/v1/shifts/${shift}/close`,cashier,undefined,'POST')).status).toBe(204);clock('live');
 await signIn(page,s.resolver);await loadUi(page);const blocked=await prepareUi(page,'Capture cutoff evidence');expect(blocked.status).toBe(200);expect(blocked.data.status).toBe('blocked');expect(blocked.data.blockers).toContain('pending_cash_reviews');expect(blocked.data.blockers).toContain('cutoff_financial_gaps');
 expect((await review()).varianceAmount).toBe(amount-5);
 await loadUi(page);const missing=await prepareUi(page,'Refresh cutoff evidence');expect(missing.status).toBe(200);expect(missing.data.status).toBe('blocked');expect(missing.data.blockers).toContain('cutoff_financial_gaps');undelivered=missing.data;
 const original=await send('order',sourcePath('order',missing.data.snapshot.cutoff.order.manifestId),manager);expect(original.status).toBe(200);expect(original.data.manifest.sales).toHaveLength(1);expect(original.data.current).toBe(true);
});
test(scenarios[3],async({page})=>{
 await control('stop-order');await control('start-order-delivery');
 await expect.poll(async()=> (await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data?.netMovementAmount).toBe(amount);await expect.poll(reportingTotal).toBe(amount);
 expect((await review()).varianceAmount).toBe(-5);
 await signIn(page,s.resolver);const value=await refresh(page);expect(value.snapshot.grossSales).toBe(amount);expect(value.snapshot.cashVariance).toBe(-5);
 for(const owner of ['order','payment','pos'])expect(value.snapshot.cutoff[owner].generation).toBeGreaterThan(undelivered.snapshot.cutoff[owner].generation);
 const retained=await send('order',sourcePath('order',undelivered.snapshot.cutoff.order.manifestId),manager);expect(retained.status).toBe(200);expect(retained.data.manifest.sales).toHaveLength(1);expect(retained.data.manifest.manifestId).toBe(undelivered.snapshot.cutoff.order.manifestId);
});
test(scenarios[4],async({page})=>{
 await signIn(page,s.accountant);const value=await ready(page);expect(value.canPrepare).toBe(false);await expect(page.getByRole('button',{name:/Capture cutoff evidence|Refresh cutoff evidence|Resume cutoff|Replace interrupted cutoff/})).toHaveCount(0);
 const before=await proof();const noCsrf=await page.evaluate(async({root,body})=>(await fetch(root,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})).status,{root,body:command(value.version)});expect(noCsrf).toBe(400);
 expect((await bff(page,root,command(value.version))).status).toBe(403);expect((await bff(page,root+'?'+new URLSearchParams({branchId:f.deniedBranchId,businessDate:f.businessDate}))).status).toBe(403);
 for(const owner of ['order','payment','pos']){
  const id=value.snapshot.cutoff[owner].manifestId;expect((await send(owner,sourcePath(owner,id),null)).status).toBe(401);
  expect((await send(owner,sourcePath(owner,id,{...window(),organizationId:f.otherOrganizationId}),manager)).status).toBe(403);
  expect((await send(owner,sourcePath(owner,id),manager)).status).toBe(200);
 }
 expect(await page.evaluate(async organizationId=>(await fetch('/bff/customer/tenant',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({organizationId,applicationCode:'nexa_connect'})})).status,f.otherOrganizationId)).toBe(200);
 expect((await bff(page,readPath())).status).toBe(403);expect((await proof()).audit).toBe(before.audit);
});
test(scenarios[5],async({page,browser})=>{
 await signIn(page,s.resolver);const before=await ready(page);expect((await bff(page,root,completed)).data.version).toBe(before.version);expect((await bff(page,root,{...completed,reasonCode:'recheck'})).status).toBe(409);
 const context=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});try{
  const other=await context.newPage();await signIn(other,s.secondManager);const first=command(before.version),second=command(before.version);payment.hold();const outstanding=bff(page,root,first);
  await expect.poll(payment.pending).toBeGreaterThan(0);expect((await bff(other,root,second)).status).toBe(409);payment.release();const result=await outstanding;expect(result.status).toBe(200);expect(result.data.status).toBe('ready_for_review');completed=first;
 }finally{payment.release();await context.close();}
});
async function interrupt(page){
 const before=await ready(page),cmd=command(before.version),counts=(await proof()).sourceCounts;payment.hold();const outstanding=bff(page,root,cmd);
 await expect.poll(payment.pending).toBeGreaterThan(0);const partial=await proof();expect(partial.status).toBe('preparing');expect(partial.sourceCounts.order).toBe(counts.order+1);expect(partial.sourceCounts.payment).toBe(counts.payment);
 const retained=await send('order','/api/order/v1/customer/day-cutoffs',manager,{operationId:cmd.operationId,window:window()});expect(retained.status).toBe(200);
 await control('stop-pos');expect((await outstanding).status).toBe(503);payment.release();await control('start-pos');return {cmd,manifest:retained.data.manifest};
}
async function expire(){const value=await proof();expect(value.status).toBe('preparing');await new Promise(ok=>setTimeout(ok,value.leaseRemainingMs+300));}
test(scenarios[6],async({page})=>{
 await signIn(page,s.resolver);const partial=await interrupt(page);const loaded=await loadUi(page);expect(loaded.pendingCommand).toEqual(partial.cmd);expect((await bff(page,root,partial.cmd)).status).toBe(409);
 await expire();const resumed=await prepareUi(page,'Resume cutoff');expect(resumed.status).toBe(200);expect(resumed.command).toEqual(partial.cmd);expect(resumed.data.status).toBe('ready_for_review');expect(resumed.data.snapshot.cutoff.order.manifestId).toBe(partial.manifest.manifestId);expect(resumed.data.snapshot.cutoff.order.generation).toBe(partial.manifest.generation);expect((await proof()).pending).toBe(0);completed=partial.cmd;
});
test(scenarios[7],async({page,browser})=>{
 await signIn(page,s.resolver);const partial=await interrupt(page);const context=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});try{
  const other=await context.newPage();await signIn(other,s.secondManager);const pending=await loadUi(other);expect(pending.pendingCommand).toBeNull();expect((await bff(other,root,command(pending.version))).status).toBe(409);
  await expire();const result=await prepareUi(other,'Replace interrupted cutoff');expect(result.status).toBe(200);expect(result.data.status).toBe('ready_for_review');expect(result.command.operationId).not.toBe(partial.cmd.operationId);expect(result.data.snapshot.cutoff.order.manifestId).not.toBe(partial.manifest.manifestId);expect((await bff(page,root,partial.cmd)).status).toBe(409);expect((await proof()).abandoned).toBe(1);
 }finally{payment.release();await context.close();}
});
let sealedDay;
test(scenarios[12],async({page})=>{
 await signIn(page,s.resolver);const reviewed=await ready(page);
 const load=page.waitForResponse(r=>r.url().includes('/bff/customer/day-close-seals?')&&r.request().method()==='GET');
 await page.getByRole('button',{name:'Load sealed evidence',exact:true}).click();expect((await load).status()).toBe(200);
 const response=page.waitForResponse(r=>r.url().endsWith('/bff/customer/day-close-seals')&&r.request().method()==='POST');
 await page.getByRole('button',{name:'Seal reviewed evidence',exact:true}).click();const result=await response;expect(result.status()).toBe(200);sealedDay=await result.json();
 expect(result.request().postDataJSON().reviewedCutoffVersion).toBe(reviewed.version);expect(sealedDay.status).toBe('ready_for_review');expect(sealedDay.pendingSealChanges).toBe(0);
 await expect(page.getByText('sealed evidence',{exact:true})).toBeVisible();
 for(const owner of ['order','payment','pos']){
  const ref=sealedDay.snapshot.seals[owner];expect(ref.manifestId).toBe(reviewed.snapshot.cutoff[owner].manifestId);
  const read=await send(owner,`/api/${owner}/v1/customer/day-cutoffs/seals/${ref.sealId}?${new URLSearchParams(window())}`,manager);
  expect(read.status).toBe(200);expect(read.data.journalComplete).toBe(true);expect(read.data.pendingChanges).toBe(0);expect(read.data.manifest.manifestId).toBe(ref.manifestId);
 }
});
let approvedDay;
test(scenarios[13],async({page,browser})=>{
 await signIn(page,s.resolver);
 const approvalRoot='/bff/customer/day-close-approvals',query=`?branchId=${f.branchId}&businessDate=${f.businessDate}`;
 const loaded=page.waitForResponse(r=>r.url().includes(approvalRoot+'?')&&r.request().method()==='GET');
 await page.getByRole('button',{name:'Load approval',exact:true}).click();const candidate=await(await loaded).json();
 expect(candidate.status).toBe('not_approved');expect(candidate.canApprove).toBe(true);expect(candidate.sealVersion).toBe(sealedDay.version);
 await expect(page.getByRole('button',{name:'Approve reviewed seal',exact:true})).toBeDisabled();
 const sealLoad=page.waitForResponse(r=>r.url().includes('/bff/customer/day-close-seals?')&&r.request().method()==='GET');await page.getByRole('button',{name:'Load sealed evidence',exact:true}).click();expect((await sealLoad).status()).toBe(200);
 const approved=page.waitForResponse(r=>r.url().endsWith(approvalRoot)&&r.request().method()==='POST');await page.getByRole('button',{name:'Approve reviewed seal',exact:true}).click();
 const result=await approved;expect(result.status()).toBe(200);approvedDay=await result.json();expect(approvedDay.status).toBe('approved');
 expect(approvedDay.decision.snapshot.seals).toEqual(sealedDay.snapshot.seals);const request=result.request().postDataJSON();expect(request.reviewedSealVersion).toBe(sealedDay.version);expect(request.organizationId).toBeUndefined();expect(request.actor).toBeUndefined();
 const accounting=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});try{
  const other=await accounting.newPage();await signIn(other,s.accountant);
  const response=await other.request.get(approvalRoot+query);expect(response.status()).toBe(200);expect((await response.json()).canApprove).toBe(false);
  const csrf=await(await other.request.get(approvalRoot+'/csrf')).json();const denied=await other.request.post(approvalRoot,{headers:{'X-Nexa-CSRF':csrf.requestToken},data:{...request,operationId:randomUUID()}});expect(denied.status()).toBe(403);
 }finally{await accounting.close();}
});
test(scenarios[8],async({page,browser})=>{
 cashier=await token(browser,s.reader);manager=await token(browser,s.resolver);await signIn(page,s.resolver);const before=await ready(page);clock('historical');
 try{
  const input={organizationId:f.organizationId,restaurantId:f.restaurantId,branchId:f.branchId,currency:'THB',paymentMethod:'cash_manual',idempotencyKey:randomUUID(),lines:[{productId:r.productId,quantity:1}]};
  const quote=await send('order','/api/order/v1/workflows/quote',cashier,input);expect(quote.status).toBe(200);expect(quote.data.pricing.totalAmount).toBe(amount);
  const placed=await send('order','/api/order/v1/workflows/place',cashier,{...input,pricingFingerprint:quote.data.fingerprint});expect(placed.status).toBe(200);
  const settled=await send('order',`/api/order/v1/orders/${placed.data.orderId}/manual-settlement`,cashier,{...settlement,idempotencyKey:randomUUID()});expect(settled.status).toBe(201);
 }finally{clock('live');}
 await expect.poll(async()=> (await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data?.netMovementAmount).toBe(2*amount);await expect.poll(reportingTotal).toBe(2*amount);
 const invalid=await loadUi(page);expect(invalid.status).toBe('blocked');expect(invalid.blockers).toContain('cutoff_superseded');expect(invalid.snapshot).toEqual(before.snapshot);await expect(page.getByText('ready for review',{exact:true})).toHaveCount(0);
 const old=await send('order',sourcePath('order',before.snapshot.cutoff.order.manifestId),manager);expect(old.status).toBe(200);expect(old.data.current).toBe(false);expect(old.data.manifest.sales).toHaveLength(1);
 const sealRead=await page.request.get(`/bff/customer/day-close-seals?branchId=${f.branchId}&businessDate=${f.businessDate}`);expect(sealRead.status()).toBe(200);
 const changedSeal=await sealRead.json();expect(changedSeal.status).toBe('blocked');expect(changedSeal.blockers).toContain('sealed_changes_pending');expect(changedSeal.pendingSealChanges).toBeGreaterThan(0);expect(changedSeal.snapshot.seals).toEqual(sealedDay.snapshot.seals);
 const approvalRead=await page.request.get(`/bff/customer/day-close-approvals?branchId=${f.branchId}&businessDate=${f.businessDate}`);expect(approvalRead.status()).toBe(200);
 const superseded=await approvalRead.json();expect(superseded.status).toBe('superseded');expect(superseded.decision.approvalId).toBe(approvedDay.decision.approvalId);expect(superseded.decision.snapshot.seals).toEqual(sealedDay.snapshot.seals);
 expect((await review()).varianceAmount).toBe(-amount-5);const fresh=await refresh(page);expect(fresh.snapshot.grossSales).toBe(2*amount);expect(fresh.snapshot.cutoff.order.manifestId).not.toBe(before.snapshot.cutoff.order.manifestId);
});
test(scenarios[9],async({page})=>{
 await signIn(page,s.resolver);await ready(page);payment.outage();try{
  const failed=await loadUi(page);expect(failed.status).toBe('blocked');expect(failed.blockers).toContain('source_unavailable');await expect(page.getByText('ready for review',{exact:true})).toHaveCount(0);
 }finally{payment.restore();}await refresh(page);
});
test(scenarios[10],async({page})=>{
 await signIn(page,s.resolver);const before=await ready(page);await fixture('revoke-manager-source');try{
  expect((await send('payment',sourcePath('payment',before.snapshot.cutoff.payment.manifestId),manager)).status).toBe(403);
  const failed=await loadUi(page);expect(failed.status).toBe('blocked');expect(failed.blockers).toContain('source_unavailable');await expect(page.getByText('ready for review',{exact:true})).toHaveCount(0);
 }finally{await fixture('restore-manager-source');}await refresh(page);
});
test(scenarios[11],async({page,browser})=>{
 await signIn(page,s.resolver);await ready(page);const before=await proof();await fixture('revoke-day-close-prepare');expect((await bff(page,root,completed)).status).toBe(403);expect((await loadUi(page)).canPrepare).toBe(false);
 const reader=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true}),second=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});try{
  const accounting=await reader.newPage();await signIn(accounting,s.accountant);await ready(accounting);await fixture('revoke-day-close-read');expect((await bff(accounting,readPath())).status).toBe(403);
  const other=await second.newPage();await signIn(other,s.secondManager);await ready(other);await fixture('membership-second');expect((await bff(other,readPath())).status).toBe(403);expect((await proof()).audit).toBe(before.audit);
 }finally{await reader.close();await second.close();}
 expect((await send('order',`/api/order/v1/orders/${order}/receipt?branchId=${f.branchId}`,cashier)).data).toEqual(receipt);
 expect((await send('order',`/api/order/v1/orders/${order}/manual-settlement`,cashier,settlement)).status).toBe(200);
 const summary=(await send('pos',`/api/pos/v1/cash-sessions/${session}/summary`,cashier)).data;expect(summary.movements).toHaveLength(2);expect(summary.status).toBe('closed');
 await expect(fixture('deliver')).rejects.toThrow('Acceptance fixture failed');await expect(fixture('late-cash')).rejects.toThrow('Acceptance fixture failed');
 expect(JSON.parse(await fixture('cashier-proof')).verified).toBe(true);expect((await proof()).authorizationVerified).toBe(true);
});
