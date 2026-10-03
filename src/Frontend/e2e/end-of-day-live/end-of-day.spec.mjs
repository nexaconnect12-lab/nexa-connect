import {test,expect} from '@playwright/test';
import {createServer,connect} from 'node:net';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {readSettings} from './settings.mjs';
const s=readSettings(process.env),f=s.fixture,root='/bff/customer/reports/end-of-day';
function faultProxy(port,target){
  const sockets=new Set(),pending=new Set();let enabled=true,held=false;
  const server=createServer(client=>{
    if(!enabled){client.destroy();return;}
    sockets.add(client);client.on('close',()=>sockets.delete(client));client.on('error',()=>client.destroy());
    const forward=()=>{pending.delete(forward);if(client.destroyed)return;
      const upstream=connect(target,'127.0.0.1');sockets.add(upstream);
      upstream.on('close',()=>sockets.delete(upstream));upstream.on('error',()=>{client.destroy();upstream.destroy();});
      client.on('close',()=>upstream.destroy());client.pipe(upstream);upstream.pipe(client);client.resume();};
    if(held){client.pause();pending.add(forward);}else forward();
  });
  const disconnect=()=>{for(const socket of sockets)socket.destroy();};
  return {start:()=>new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);}),
    outage:()=>{enabled=false;disconnect();},restore:()=>{enabled=true;},hold:()=>{held=true;disconnect();},
    release:()=>{held=false;for(const forward of [...pending])forward();},pending:()=>pending.size,
    close:async()=>{held=false;for(const forward of [...pending])forward();disconnect();await new Promise(resolve=>server.close(resolve));}};
}
const reporting=faultProxy(s.proxyPort,s.reportingPort),payment=faultProxy(s.sourceProxyPort,s.paymentPort);
test.describe.configure({mode:'serial'});
test.beforeAll(async()=>{await reporting.start();await payment.start();});
test.afterAll(async()=>{await reporting.close();await payment.close();});
async function fixture(action){
  try{return (await promisify(execFile)('dotnet',[s.fixtureDll,action],{timeout:190000,windowsHide:true})).stdout.trim();}
  catch{throw new Error('End-of-day fixture operation failed; sensitive diagnostics suppressed.');}
}
async function call(page,path,body){return page.evaluate(async({path,body})=>{const r=await fetch(path,{method:body?'POST':'GET',headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});return {status:r.status};},{path,body});}
async function filters(page){await page.getByLabel('End-of-day branch ID').fill(f.branchId);await page.getByLabel('End-of-day business date').fill(f.businessDate);}
async function signIn(page,identity){
  const allowed=new Set([s.baseURL,new URL(s.issuer).origin]);
  await page.context().route('**/*',r=>allowed.has(new URL(r.request().url()).origin)?r.continue():r.abort());
  await page.goto('/bff/customer/login?returnUrl=%2F');await expect(page.locator('#username')).toBeVisible();
  const endpoint=new URL(page.url());
  if(endpoint.origin!==new URL(s.issuer).origin||endpoint.pathname!==new URL(s.issuer).pathname+'/protocol/openid-connect/auth')throw new Error('Unexpected identity endpoint.');
  try{await page.locator('#username').fill(identity.username);await page.locator('#password').fill(identity.password);await page.locator('#kc-login').click();await page.waitForURL(u=>u.origin===s.baseURL);}
  catch{throw new Error('OIDC acceptance failed; credentials suppressed.');}
  expect((await call(page,'/bff/customer/tenant',{organizationId:f.organizationId,applicationCode:'nexa_connect'})).status).toBe(200);
  await page.goto('/#end-of-day');await page.reload();await filters(page);
}
async function load(page){const result=page.waitForResponse(r=>r.url().includes(root)&&r.request().method()==='GET');await page.getByRole('button',{name:'Load day draft',exact:true}).click();const response=await result;expect(response.headers()['cache-control']).toContain('no-store');await expect(page.getByRole('button',{name:'Load day draft',exact:true})).toBeEnabled();return response;}
async function loaded(page){expect((await load(page)).status()).toBe(200);await expect(page.getByText('Asia/Bangkok',{exact:true})).toBeVisible();}
async function empty(page){await expect(page.getByText('Gross sales',{exact:true})).toHaveCount(0);await expect(page.getByText('THB 100',{exact:true})).toHaveCount(0);}
test('completed branch day shows owning totals and resolves actual delayed Reporting delivery',async({page})=>{
  await signIn(page,s.resolver);const response=await load(page);expect(response.status()).toBe(200);const draft=await response.json();
  expect(draft.status).toBe('draft');expect(draft.window.fromUtc).toBe(f.fromUtc);expect(draft.window.toUtc).toBe(f.toUtc);
  expect(draft.grossSales).toBe(100);expect(draft.completedRefunds).toBe(25);expect(draft.netSales).toBe(75);expect(draft.cashVariance).toBe(-5);
  expect(draft.tenders).toEqual([{method:'card',currency:'THB',amount:100}]);
  expect(draft.order.unresolvedOrders).toBe(1);expect(draft.payment.unresolvedPayments).toBe(1);expect(draft.payment.unresolvedRefunds).toBe(1);
  expect(draft.pos.openShifts).toBe(1);expect(draft.pos.openCashSessions).toBe(1);expect(draft.pos.pendingCashReviews).toBe(1);
  await expect(page.getByText('Reporting totals differ from source totals',{exact:true})).toBeVisible();
  await fixture('deliver');expect(await fixture('record')).toBe('observed_complete');await loaded(page);
  await expect(page.getByText('Reporting totals differ from source totals',{exact:true})).toHaveCount(0);
  await expect(page.getByText('Recorded financial check is historical; request a fresh check before closing',{exact:true})).toBeVisible();
  await expect(page.getByText('THB 100',{exact:true})).toBeVisible();await expect(page.getByText('THB 25',{exact:true})).toBeVisible();await expect(page.getByText('THB 75',{exact:true})).toBeVisible();
});
test('completed adjacent and future dates respect branch-local boundaries',async({page})=>{
  await signIn(page,s.resolver);await loaded(page);
  const before=new Date(Date.parse(`${f.businessDate}T00:00:00Z`)-86400000).toISOString().slice(0,10);
  await page.getByLabel('End-of-day business date').fill(before);const response=await load(page);expect(response.status()).toBe(200);
  const draft=await response.json();expect(draft.grossSales).toBe(0);expect(draft.completedRefunds).toBe(0);expect(draft.tenders).toEqual([]);expect(draft.cashVariance).toBe(0);
  await page.getByLabel('End-of-day business date').fill('2099-01-01');expect((await load(page)).status()).toBe(400);await empty(page);
});
test('branch and tenant boundaries deny live reads and clear old totals',async({page})=>{
  await signIn(page,s.reader);await loaded(page);await page.getByLabel('End-of-day branch ID').fill(f.deniedBranchId);expect((await load(page)).status()).toBe(403);await empty(page);
  await filters(page);expect((await call(page,'/bff/customer/tenant',{organizationId:f.otherOrganizationId,applicationCode:'nexa_connect'})).status).toBe(200);
  expect((await load(page)).status()).toBe(403);await empty(page);
});
test('owning Payment and Reporting transport outages return unavailable without partial totals',async({page})=>{
  await signIn(page,s.resolver);await loaded(page);
  for(const proxy of [payment,reporting]){proxy.outage();try{expect((await load(page)).status()).toBe(503);await empty(page);}finally{proxy.restore();}await loaded(page);}
});
test('filter and tenant changes fence delayed actual downstream responses',async({page})=>{
  await signIn(page,s.resolver);await loaded(page);reporting.hold();
  await page.getByRole('button',{name:'Load day draft',exact:true}).click();await expect.poll(reporting.pending).toBeGreaterThan(0);
  await page.getByLabel('End-of-day branch ID').fill(f.deniedBranchId);reporting.release();await empty(page);
  await filters(page);await loaded(page);reporting.hold();await page.getByRole('button',{name:'Load day draft',exact:true}).click();await expect.poll(reporting.pending).toBeGreaterThan(0);
  await page.getByTitle('Financial Acceptance',{exact:false}).click();await page.getByTitle('Other Tenant',{exact:false}).click();
  await expect(page.getByLabel('End-of-day branch ID')).toHaveValue('');reporting.release();await empty(page);await page.waitForTimeout(500);await empty(page);
});
test('accountant workflow exposes no financial commands and rejects write route',async({page})=>{
  await signIn(page,s.reader);const writes=[];page.on('request',r=>{if(r.url().includes('/reports/')&&r.method()!=='GET')writes.push(r.method());});await loaded(page);
  expect(writes).toHaveLength(0);await expect(page.getByRole('button',{name:/approve|lock|repair|replay|settle|record check/i})).toHaveCount(0);
  expect((await call(page,root,{})).status).toBe(405);
});
test('live owning-source permission revocation rejects an existing accountant session',async({page})=>{
  await signIn(page,s.reader);await loaded(page);await fixture('revoke-source');await expect.poll(async()=>(await load(page)).status(),{timeout:30000}).toBe(403);await empty(page);
});
test('live organization membership revocation rejects an existing manager session',async({page})=>{
  await signIn(page,s.resolver);await loaded(page);await fixture('membership');await expect.poll(async()=>(await load(page)).status(),{timeout:30000}).toBe(403);await empty(page);
});
