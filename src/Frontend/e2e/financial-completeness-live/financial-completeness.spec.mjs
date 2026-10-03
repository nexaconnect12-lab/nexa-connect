import {test,expect} from '@playwright/test';
import {createServer,connect} from 'node:net';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {readSettings} from './settings.mjs';
const s=readSettings(process.env),f=s.fixture,root='/bff/customer/reports/financial-completeness';
let proxy,enabled=true,held=false;const sockets=new Set(),pending=new Set();
test.describe.configure({mode:'serial'});
test.beforeAll(async()=>{
  proxy=createServer(client=>{
    if(!enabled){client.destroy();return;}
    sockets.add(client);client.on('close',()=>sockets.delete(client));client.on('error',()=>client.destroy());
    const forward=()=>{
      pending.delete(forward);if(client.destroyed)return;
      const upstream=connect(s.reportingPort,'127.0.0.1');sockets.add(upstream);
      upstream.on('close',()=>sockets.delete(upstream));upstream.on('error',()=>{client.destroy();upstream.destroy();});
      client.on('close',()=>upstream.destroy());client.pipe(upstream);upstream.pipe(client);client.resume();
    };
    if(held){client.pause();pending.add(forward);}else forward();
  });
  await new Promise((resolve,reject)=>{proxy.once('error',reject);proxy.listen(s.proxyPort,'127.0.0.1',resolve);});
});
function release(){held=false;for(const forward of [...pending])forward();}
test.afterAll(async()=>{release();for(const socket of sockets)socket.destroy();if(proxy)await new Promise(resolve=>proxy.close(resolve));});
async function fixture(action){
  try{return (await promisify(execFile)('dotnet',[s.fixtureDll,action],{timeout:190000,windowsHide:true})).stdout.trim();}
  catch{throw new Error('Financial fixture operation failed; sensitive diagnostics suppressed.');}
}
async function call(page,path,body){
  return page.evaluate(async({path,body})=>{const r=await fetch(path,{method:body?'POST':'GET',headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});return {status:r.status};},{path,body});
}
async function signIn(page,identity){
  const allowed=new Set([s.baseURL,new URL(s.issuer).origin]);
  await page.context().route('**/*',route=>allowed.has(new URL(route.request().url()).origin)?route.continue():route.abort());
  await page.goto('/bff/customer/login?returnUrl=%2F');await expect(page.locator('#username')).toBeVisible();
  const endpoint=new URL(page.url());
  if(endpoint.origin!==new URL(s.issuer).origin||endpoint.pathname!==new URL(s.issuer).pathname+'/protocol/openid-connect/auth')throw new Error('Unexpected identity endpoint.');
  try{await page.locator('#username').fill(identity.username);await page.locator('#password').fill(identity.password);await page.locator('#kc-login').click();await page.waitForURL(u=>u.origin===s.baseURL);}
  catch{throw new Error('OIDC acceptance failed; credentials suppressed.');}
  expect((await call(page,'/bff/customer/tenant',{organizationId:f.organizationId,applicationCode:'nexa_connect'})).status).toBe(200);
  await page.goto('/#reports');await page.reload();await filters(page);
}
async function filters(page){
  await page.getByLabel('Sales branch ID').fill(f.branchId);
  await page.getByLabel('Sales from UTC').fill(f.fromUtc.slice(0,16));await page.getByLabel('Sales to UTC').fill(f.toUtc.slice(0,16));
}
const card=page=>page.getByLabel('Financial reconciliation');
async function load(page){
  const response=page.waitForResponse(r=>r.url().includes(root)&&r.request().method()==='GET');
  await page.getByRole('button',{name:'Load sales report',exact:true}).click();
  const status=(await response).status();
  await expect(page.getByRole('button',{name:'Load sales report',exact:true})).toBeEnabled();
  return status;
}
async function complete(page){
  expect(await load(page)).toBe(200);await expect(card(page).getByRole('status')).toContainText('Observed complete');
}
async function empty(page){
  await expect(card(page).getByRole('table')).toHaveCount(0);
  await expect(page.getByRole('cell',{name:'THB 100',exact:true})).toHaveCount(0);
}
test('retained evidence progresses through not checked, gaps and real delivered complete observations',async({page})=>{
  await signIn(page,s.resolver);expect(await load(page)).toBe(200);
  await expect(card(page).getByRole('status')).toContainText('Not checked');
  expect(await fixture('record')).toBe('gaps_detected');expect(await load(page)).toBe(200);
  await expect(card(page).getByRole('status')).toContainText('Gaps detected');
  await fixture('deliver');expect(await fixture('record')).toBe('observed_complete');await complete(page);
  for(const kind of ['Sales','Payments','Refunds'])await expect(card(page).getByRole('cell',{name:kind,exact:true})).toBeVisible();
  for(const label of ['Checked at (UTC)','Order evidence read (UTC)','Refund evidence read (UTC)'])await expect(card(page).getByText(label,{exact:true})).toBeVisible();
  await expect(page.getByRole('cell',{name:'THB 100',exact:true})).toHaveCount(2);
  await expect(page.getByRole('cell',{name:'THB 25',exact:true})).toHaveCount(1);
  await expect(page.getByRole('cell',{name:'THB 75',exact:true})).toHaveCount(1);
});
test('branch and foreign tenant authorization deny the real report and clear financial evidence',async({page})=>{
  await signIn(page,s.reader);await complete(page);
  await page.getByLabel('Sales branch ID').fill(f.deniedBranchId);expect(await load(page)).toBe(403);await empty(page);
  await filters(page);expect((await call(page,'/bff/customer/tenant',{organizationId:f.otherOrganizationId,applicationCode:'nexa_connect'})).status).toBe(200);
  expect(await load(page)).toBe(403);await empty(page);
});
test('actual Reporting transport outage clears results and recovers without repair',async({page})=>{
  await signIn(page,s.resolver);await complete(page);enabled=false;for(const socket of sockets)socket.destroy();
  try{expect(await load(page)).toBe(503);await expect(card(page).getByRole('status')).toContainText('unavailable');await empty(page);}
  finally{enabled=true;}
  await complete(page);
});
test('filter and tenant changes clear evidence while actual downstream requests are delayed',async({page})=>{
  await signIn(page,s.resolver);await complete(page);
  held=true;for(const socket of sockets)socket.destroy();
  await page.getByRole('button',{name:'Load sales report',exact:true}).click();
  await expect.poll(()=>pending.size).toBeGreaterThan(0);
  await page.getByLabel('Sales branch ID').fill(f.deniedBranchId);release();await empty(page);
  await filters(page);await complete(page);held=true;for(const socket of sockets)socket.destroy();
  await page.getByRole('button',{name:'Load sales report',exact:true}).click();await expect.poll(()=>pending.size).toBeGreaterThan(0);
  await page.getByTitle('Financial Acceptance', {exact:false}).click();await page.getByTitle('Other Tenant',{exact:false}).click();
  await expect(page.getByLabel('Sales branch ID')).toHaveValue('');release();await empty(page);
  await page.waitForTimeout(500);await empty(page);
});
test('accountant read sends identical windows and exposes no financial write controls or route',async({page})=>{
  await signIn(page,s.reader);
  const queries=[],writes=[];page.on('request',r=>{if(r.url().includes('/reports/')){if(r.method()==='GET')queries.push(new URL(r.url()).search);else writes.push(r.method());}});
  await complete(page);expect(queries).toHaveLength(2);expect(queries[0]).toBe(queries[1]);expect(writes).toHaveLength(0);
  await expect(page.getByRole('button',{name:/repair|replay|record check|run reconciliation/i})).toHaveCount(0);
  expect((await call(page,root,{})).status).toBe(405);
});
test('live sales permission revocation rejects the already authenticated accountant',async({page})=>{
  await signIn(page,s.reader);await complete(page);await fixture('revoke');
  await expect.poll(()=>load(page),{timeout:30000}).toBe(403);await empty(page);
});
test('live organization membership revocation rejects the already authenticated manager',async({page})=>{
  await signIn(page,s.resolver);await complete(page);await fixture('membership');
  await expect.poll(()=>load(page),{timeout:30000}).toBe(403);await empty(page);
});
