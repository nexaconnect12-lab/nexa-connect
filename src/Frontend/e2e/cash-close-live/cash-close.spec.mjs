import {test,expect} from '@playwright/test';
import {createServer,connect} from 'node:net';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {readSettings} from './settings.mjs';
const s=readSettings(process.env),f=s.fixture,root='/bff/customer/reports/cash-close';
let proxy,enabled=true;const sockets=new Set();
test.describe.configure({mode:'serial'});
test.beforeAll(async()=>{
  // Test-process-owned loopback TCP proxy. No control HTTP endpoint or production fault hook.
  proxy=createServer(client=>{
    if(!enabled){client.destroy();return;}
    const upstream=connect(s.posPort,'127.0.0.1');
    for(const socket of [client,upstream]){sockets.add(socket);socket.on('close',()=>sockets.delete(socket));socket.on('error',()=>{client.destroy();upstream.destroy();});}
    client.pipe(upstream);upstream.pipe(client);
  });
  await new Promise((resolve,reject)=>{proxy.once('error',reject);proxy.listen(s.proxyPort,'127.0.0.1',resolve);});
});
test.afterAll(async()=>{for(const socket of sockets)socket.destroy();if(proxy)await new Promise(resolve=>proxy.close(resolve));});
async function fixture(action){
  try{await promisify(execFile)('dotnet',[s.fixtureDll,action],{timeout:30000,windowsHide:true});}
  catch{throw new Error(`Fixture ${action} failed; sensitive diagnostics suppressed.`);}
}
async function call(page,path,body){return page.evaluate(async({path,body})=>{const r=await fetch(path,{method:body?'POST':'GET',headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});let data;try{data=await r.json();}catch{}return {status:r.status,data};},{path,body});}
async function signIn(page,identity){
  const allowed=new Set([s.baseURL,new URL(s.issuer).origin]);
  await page.context().route('**/*',route=>allowed.has(new URL(route.request().url()).origin)?route.continue():route.abort());
  await page.goto('/bff/customer/login?returnUrl=%2F');await expect(page.locator('#username')).toBeVisible();
  const target=new URL(page.url());
  if(target.origin!==new URL(s.issuer).origin||target.pathname!==new URL(s.issuer).pathname+'/protocol/openid-connect/auth')throw new Error('Unexpected identity endpoint.');
  try{await page.locator('#username').fill(identity.username);await page.locator('#password').fill(identity.password);await page.locator('#kc-login').click();await page.waitForURL(u=>u.origin===s.baseURL);}
  catch{throw new Error('OIDC acceptance failed; credentials suppressed.');}
  expect((await call(page,'/bff/customer/tenant',{organizationId:f.organizationId,applicationCode:'nexa_connect'})).status).toBe(200);
  await page.goto('/#cash-close-reports');await page.reload();
  await page.getByLabel('Report branch UUID').fill(f.branchId);await page.getByLabel('Report store UUID').fill(f.storeId);
  await page.getByLabel('Report from UTC').fill(new Date(Date.parse(f.occurredAtUtc)-86400000).toISOString().slice(0,16));
  await page.getByLabel('Report to UTC').fill(new Date(Date.parse(f.occurredAtUtc)+86400000).toISOString().slice(0,16));
}
async function load(page){const response=page.waitForResponse(r=>r.url().includes(root)&&r.request().method()==='GET');await page.getByRole('button',{name:'Load cash-close report',exact:true}).click();return (await response).status();}
async function projected(page,status,version){
  await expect.poll(async()=>{if(await load(page)!==200)return false;const row=page.getByRole('row').filter({hasText:f.sessionId});return await row.count()===1&&await row.getByRole('cell',{name:status,exact:true}).count()===1&&await row.getByRole('cell',{name:version,exact:true}).count()===1;},{timeout:60000,intervals:[1000,2000]}).toBe(true);
}
test('real snapshots propagate close, approval and late-settlement invalidation',async({page})=>{
  await signIn(page,s.resolver);await projected(page,'review_required','2 / 0');
  await fixture('approve');await projected(page,'approved','2 / 1');
  await fixture('late');await projected(page,'review_required','3 / 1');
  const row=page.getByRole('row').filter({hasText:f.sessionId});await expect(row.getByRole('cell',{name:'110',exact:true})).toBeVisible();await expect(row.getByRole('cell',{name:'-15',exact:true})).toBeVisible();
});
test('exact branch/store and foreign tenant access fail closed and clear rows',async({page})=>{
  await signIn(page,s.reader);await projected(page,'review_required','3 / 1');
  await page.getByLabel('Report branch UUID').fill(f.deniedBranchId);await page.getByLabel('Report store UUID').fill(f.deniedStoreId);
  expect(await load(page)).toBe(403);await expect(page.getByRole('cell',{name:f.sessionId,exact:true})).toHaveCount(0);
  await page.getByLabel('Report branch UUID').fill(f.branchId); // valid branch with a store owned by another branch
  expect(await load(page)).toBe(403);
  expect((await call(page,'/bff/customer/tenant',{organizationId:f.otherOrganizationId,applicationCode:'nexa_connect'})).status).toBe(200);
  await page.getByLabel('Report store UUID').fill(f.storeId);expect(await load(page)).toBe(403);
  await page.reload();await expect(page.getByLabel('Report store UUID')).toHaveValue('');
});
test('real POS access dependency failure clears rows and recovers',async({page})=>{
  await signIn(page,s.resolver);await projected(page,'review_required','3 / 1');
  enabled=false;for(const socket of sockets)socket.destroy();
  try{expect(await load(page)).toBe(503);await expect(page.getByRole('status')).toContainText('Report unavailable');await expect(page.getByRole('cell',{name:f.sessionId,exact:true})).toHaveCount(0);}
  finally{enabled=true;}
  await projected(page,'review_required','3 / 1');
});
test('accountant can read the authenticated report without decision controls',async({page})=>{
  await signIn(page,s.reader);await projected(page,'review_required','3 / 1');
  await expect(page.getByRole('button',{name:/approve|resolve|confirm decision/i})).toHaveCount(0);
  expect((await call(page,root,{})).status).toBe(405);
});
test('revoking read permission denies the existing session and removes financial rows',async({page})=>{
  await signIn(page,s.reader);await projected(page,'review_required','3 / 1');await fixture('revoke');
  await expect.poll(()=>load(page),{timeout:30000}).toBe(403);
  await expect(page.getByRole('status')).toContainText('Report unavailable');await expect(page.getByRole('cell',{name:f.sessionId,exact:true})).toHaveCount(0);
});
