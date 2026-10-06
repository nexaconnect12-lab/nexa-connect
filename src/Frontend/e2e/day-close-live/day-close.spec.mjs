import {test,expect} from '@playwright/test';
import {createServer,connect} from 'node:net';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomUUID} from 'node:crypto';
import {readSettings} from './settings.mjs';
import {scenarios} from './scenarios.mjs';
const s=readSettings(process.env),f=s.fixture,root='/bff/customer/day-close-preparations';
function faultProxy(port,target){
 const sockets=new Set(),pending=new Set();let enabled=true,held=false;
 const server=createServer(client=>{
  if(!enabled){client.destroy();return;}sockets.add(client);client.on('close',()=>sockets.delete(client));client.on('error',()=>client.destroy());
  const forward=()=>{pending.delete(forward);if(client.destroyed)return;const upstream=connect(target,'127.0.0.1');sockets.add(upstream);upstream.on('close',()=>sockets.delete(upstream));upstream.on('error',()=>{client.destroy();upstream.destroy();});client.on('close',()=>upstream.destroy());client.pipe(upstream);upstream.pipe(client);client.resume();};
  if(held){client.pause();pending.add(forward);}else forward();
 });
 const disconnect=()=>{for(const socket of sockets)socket.destroy();};
 return {start:()=>new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);}),outage:()=>{enabled=false;disconnect();},restore:()=>{enabled=true;},hold:()=>{held=true;disconnect();},release:()=>{held=false;for(const forward of [...pending])forward();},pending:()=>pending.size,close:async()=>{held=false;for(const forward of [...pending])forward();disconnect();await new Promise(resolve=>server.close(resolve));}};
}
const reporting=faultProxy(s.proxyPort,s.reportingPort),payment=faultProxy(s.sourceProxyPort,s.paymentPort);
test.describe.configure({mode:'serial'});
test.beforeAll(async()=>{await reporting.start();await payment.start();});test.afterAll(async()=>{await reporting.close();await payment.close();});
async function fixture(action){try{return(await promisify(execFile)('dotnet',[s.fixtureDll,action],{timeout:190000,windowsHide:true})).stdout.trim();}catch{throw new Error('Day-close fixture failed; sensitive diagnostics suppressed.');}}
async function proof(){return JSON.parse(await fixture('preparation-proof'));}
async function call(page,path,body,csrf=true){return page.evaluate(async({path,body,csrf,root})=>{
 const headers=body?{'Content-Type':'application/json'}:{};
 if(body&&csrf){const token=await fetch(root+'/csrf',{cache:'no-store'});headers['X-Nexa-CSRF']=(await token.json()).requestToken;}
 const response=await fetch(path,{method:body?'POST':'GET',headers,body:body?JSON.stringify(body):undefined,cache:'no-store'});
 let data;try{data=await response.json();}catch{}return {status:response.status,data,noStore:response.headers.get('cache-control')?.includes('no-store')};
 },{path,body,csrf,root});}
const readPath=()=>root+'?'+new URLSearchParams({branchId:f.branchId,businessDate:f.businessDate});
async function signIn(page,identity){
 const allowed=new Set([s.baseURL,new URL(s.issuer).origin]);await page.context().route('**/*',r=>allowed.has(new URL(r.request().url()).origin)?r.continue():r.abort());
 await page.goto('/bff/customer/login?returnUrl=%2F');await expect(page.locator('#username')).toBeVisible();const url=new URL(page.url());
 if(url.origin!==new URL(s.issuer).origin||url.pathname!==new URL(s.issuer).pathname+'/protocol/openid-connect/auth')throw new Error('Unexpected identity endpoint.');
 try{await page.locator('#username').fill(identity.username);await page.locator('#password').fill(identity.password);await page.locator('#kc-login').click();await page.waitForURL(u=>u.origin===s.baseURL);}catch{throw new Error('OIDC acceptance failed; credentials suppressed.');}
 expect((await call(page,'/bff/customer/tenant',{organizationId:f.organizationId,applicationCode:'nexa_connect'},false)).status).toBe(200);
 await page.goto('/#end-of-day');await page.reload();await filters(page);
}
async function filters(page){await page.getByLabel('End-of-day branch ID').fill(f.branchId);await page.getByLabel('End-of-day business date').fill(f.businessDate);}
async function load(page){const response=page.waitForResponse(r=>r.url().includes(root)&&!r.url().endsWith('/csrf')&&r.request().method()==='GET');await page.getByRole('button',{name:'Load preparation',exact:true}).click();const result=await response;expect(result.headers()['cache-control']).toContain('no-store');await expect(page.getByRole('button',{name:'Load preparation',exact:true})).toBeEnabled();return {status:result.status(),data:await result.json()};}
async function press(page,name){const request=page.waitForRequest(r=>new URL(r.url()).pathname===root&&r.method()==='POST');const response=page.waitForResponse(r=>new URL(r.url()).pathname===root&&r.request().method()==='POST');await page.getByRole('button',{name,exact:true}).click();const input=(await request).postDataJSON(),result=await response;return {command:input,status:result.status(),data:await result.json()};}
async function ready(page){const r=await load(page);expect(r.status).toBe(200);expect(r.data.status).toBe('ready_for_review');expect(r.data.validatedAtUtc).toBeTruthy();return r.data;}
const command=version=>({branchId:f.branchId,businessDate:f.businessDate,operationId:randomUUID(),expectedVersion:version,reasonCode:'recheck'});
let completedCommand;

test(scenarios[0],async({page})=>{
 await signIn(page,s.resolver);const missing=await load(page);expect(missing.data.status).toBe('not_prepared');const blocked=await press(page,'Prepare day close');expect(blocked.status).toBe(200);expect(blocked.data.status).toBe('blocked');expect(blocked.data.blockers).toEqual(expect.arrayContaining(['open_shifts','open_cash_sessions','pending_cash_reviews','projection_totals_differ','financial_evidence_not_checked']));expect((await proof()).audit).toBe(2);
 await fixture('resolve-day-close');await fixture('deliver');expect(await fixture('record')).toBe('observed_complete');
 const result=await press(page,'Refresh preparation');expect(result.status).toBe(200);expect(result.data.status).toBe('ready_for_review');expect(result.data.snapshot.grossSales).toBe(100);expect(result.data.snapshot.completedRefunds).toBe(25);expect(result.data.snapshot.cashVariance).toBe(-5);expect(result.data.snapshot.posVersion).toMatch(/^[a-f0-9]{64}$/);expect((await proof()).audit).toBe(4);completedCommand=result.command;
});
test(scenarios[1],async({page})=>{
 await signIn(page,s.reader);const value=await ready(page);expect(value.canPrepare).toBe(false);await expect(page.getByRole('button',{name:/Prepare day close|Refresh preparation|Resume preparation|Replace interrupted/})).toHaveCount(0);
 const before=await proof();expect((await call(page,root,command(value.version),false)).status).toBe(400);expect((await call(page,root,command(value.version))).status).toBe(403);
 expect((await call(page,root+'?'+new URLSearchParams({branchId:f.deniedBranchId,businessDate:f.businessDate}))).status).toBe(403);
 expect((await call(page,'/bff/customer/tenant',{organizationId:f.otherOrganizationId,applicationCode:'nexa_connect'},false)).status).toBe(200);expect((await load(page)).status).toBe(403);await expect(page.getByText('ready for review',{exact:true})).toHaveCount(0);expect((await proof()).audit).toBe(before.audit);
});
test(scenarios[2],async({page})=>{
 await signIn(page,s.resolver);const prior=await proof();expect((await call(page,root,completedCommand)).status).toBe(200);expect((await proof()).audit).toBe(prior.audit);
 expect((await call(page,root,{...completedCommand,reasonCode:completedCommand.reasonCode==='recheck'?'routine_close':'recheck'})).status).toBe(409);expect((await proof()).audit).toBe(prior.audit);
});
test(scenarios[3],async({page,browser})=>{
 await signIn(page,s.resolver);const value=await ready(page);const context=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});const other=await context.newPage();try{
  await signIn(other,s.secondManager);const first=command(value.version),second=command(value.version);reporting.hold();const outstanding=call(page,root,first);
  await expect.poll(reporting.pending).toBeGreaterThan(0);await expect.poll(async()=> (await proof()).status).toBe('preparing');expect((await call(other,root,second)).status).toBe(409);reporting.release();expect((await outstanding).status).toBe(200);expect((await proof()).version).toBe(value.version+2);completedCommand=first;
 }finally{reporting.release();await context.close();}
});
test(scenarios[4],async({page})=>{
 await signIn(page,s.resolver);const before=await ready(page);const evidence=await proof();await fixture('stop-pos');expect((await load(page)).status).toBe(503);await expect(page.getByText('ready for review',{exact:true})).toHaveCount(0);await fixture('start-pos');const after=await ready(page);expect(after.version).toBe(before.version);expect(after.snapshot).toEqual(before.snapshot);expect((await call(page,root,completedCommand)).status).toBe(200);expect((await proof()).audit).toBe(evidence.audit);
});
async function interrupt(page){const before=await ready(page);const cmd=command(before.version);reporting.hold();const outstanding=call(page,root,cmd);await expect.poll(reporting.pending).toBeGreaterThan(0);await expect.poll(async()=> (await proof()).status).toBe('preparing');await fixture('stop-pos');expect((await outstanding).status).toBe(503);await fixture('start-pos');reporting.release();return cmd;}
async function expire(){const p=await proof();expect(p.status).toBe('preparing');await new Promise(resolve=>setTimeout(resolve,p.leaseRemainingMs+300));}
test(scenarios[5],async({page})=>{
 await signIn(page,s.resolver);const cmd=await interrupt(page);const loaded=await load(page);expect(loaded.data.pendingCommand).toEqual(cmd);expect((await call(page,root,cmd)).status).toBe(409);await expire();const resumed=await press(page,'Resume preparation');expect(resumed.status).toBe(200);expect(resumed.command).toEqual(cmd);expect(resumed.data.status).toBe('ready_for_review');expect((await proof()).pending).toBe(0);completedCommand=cmd;
});
test(scenarios[6],async({page,browser})=>{
 await signIn(page,s.resolver);const original=await interrupt(page);const context=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});const other=await context.newPage();try{
  await signIn(other,s.secondManager);const value=await load(other);expect(value.data.status).toBe('preparing');expect(value.data.pendingCommand).toBeNull();expect((await call(other,root,command(value.data.version))).status).toBe(409);await expire();const replacement=await press(other,'Replace interrupted preparation');expect(replacement.status).toBe(200);expect(replacement.command.expectedVersion).toBe(value.data.version);expect(replacement.command.operationId).not.toBe(original.operationId);expect((await call(page,root,original)).status).toBe(409);expect((await proof()).abandoned).toBe(1);
 }finally{await context.close();reporting.release();}
});
test(scenarios[7],async({page})=>{
 await signIn(page,s.resolver);const before=await ready(page);await fixture('same-total-evidence');const changed=await load(page);expect(changed.data.status).toBe('blocked');expect(changed.data.blockers).toContain('source_evidence_changed');expect(changed.data.snapshot).toEqual(before.snapshot);let result=await press(page,'Refresh preparation');expect(result.data.status).toBe('ready_for_review');expect(result.data.snapshot.grossSales).toBe(before.snapshot.grossSales);expect(result.data.snapshot.cashVariance).toBe(before.snapshot.cashVariance);expect(result.data.snapshot.orderVersion).not.toBe(before.snapshot.orderVersion);
 await fixture('late-cash');const late=await load(page);expect(late.data.status).toBe('blocked');expect(late.data.blockers).toContain('pending_cash_reviews');await fixture('approve-cash');result=await press(page,'Refresh preparation');expect(result.data.status).toBe('ready_for_review');expect(result.data.snapshot.cashVariance).toBe(-6);completedCommand=result.command;await proof();
});
test(scenarios[8],async({page})=>{
 await signIn(page,s.resolver);await ready(page);payment.outage();try{const blocked=await load(page);expect(blocked.data.status).toBe('blocked');expect(blocked.data.blockers).toContain('source_unavailable');await expect(page.getByText('ready for review',{exact:true})).toHaveCount(0);}finally{payment.restore();}
 expect((await press(page,'Refresh preparation')).data.status).toBe('ready_for_review');const prior=await proof();reporting.hold();const request=call(page,root,command(prior.version));try{const failure=await request;expect(failure.status).toBe(200);expect(failure.data.status).toBe('blocked');expect(failure.data.snapshot).toBeNull();}finally{reporting.release();}
 await load(page);const recovered=await press(page,'Refresh preparation');expect(recovered.data.status).toBe('ready_for_review');completedCommand=recovered.command;
});
test(scenarios[9],async({page})=>{
 await signIn(page,s.resolver);await ready(page);await fixture('revoke-manager-source');const blocked=await load(page);expect(blocked.data.status).toBe('blocked');expect(blocked.data.blockers).toContain('source_unavailable');await fixture('restore-manager-source');const refreshed=await press(page,'Refresh preparation');expect(refreshed.data.status).toBe('ready_for_review');completedCommand=refreshed.command;
});
test(scenarios[10],async({page,browser})=>{
 await signIn(page,s.resolver);await ready(page);const before=await proof();await fixture('revoke-day-close-prepare');expect((await call(page,root,completedCommand)).status).toBe(403);expect((await load(page)).data.canPrepare).toBe(false);expect((await proof()).audit).toBe(before.audit);
 const readerContext=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true}),managerContext=await browser.newContext({baseURL:s.baseURL,ignoreHTTPSErrors:true});try{
  const reader=await readerContext.newPage();await signIn(reader,s.reader);await ready(reader);await fixture('revoke-day-close-read');expect((await load(reader)).status).toBe(403);await expect(reader.getByText('ready for review',{exact:true})).toHaveCount(0);
  const manager=await managerContext.newPage();await signIn(manager,s.secondManager);await ready(manager);await fixture('membership-second');expect((await load(manager)).status).toBe(403);await expect(manager.getByText('ready for review',{exact:true})).toHaveCount(0);expect((await proof()).audit).toBe(before.audit);
 }finally{await readerContext.close();await managerContext.close();}
});
