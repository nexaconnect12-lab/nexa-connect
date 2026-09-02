import {test,expect} from "@playwright/test";
import {readSettings} from "./settings.mjs";
import {setProxyEnabled} from "./fault-control.mjs";
const settings=readSettings(process.env),root="/bff/customer/payment-reviews";

async function protectContext(context){
  const allowed=new Set([settings.baseURL,new URL(settings.issuer).origin]);
  await context.route("**/*",route=>allowed.has(new URL(route.request().url()).origin)?route.continue():route.abort());
}
async function call(page,path,body,csrf){
  return page.evaluate(async({path,body,csrf})=>{
    const response=await fetch(path,{method:body?"POST":"GET",credentials:"same-origin",headers:body?{"Content-Type":"application/json",...(csrf?{"X-Nexa-CSRF":csrf}:{})}:{},body:body?JSON.stringify(body):undefined});
    let data;try{data=await response.json();}catch{}
    return {status:response.status,data};
  },{path,body,csrf});
}
async function signIn(page,identity){
  await protectContext(page.context());await page.goto("/bff/customer/login?returnUrl=%2F");
  await expect(page.locator("#username")).toBeVisible();
  const target=new URL(page.url());
  if(target.origin!==new URL(settings.issuer).origin||target.pathname!==new URL(settings.issuer).pathname+"/protocol/openid-connect/auth")throw new Error("Refusing to enter credentials outside the configured acceptance realm.");
  try{
    await page.locator("#username").fill(identity.username);await page.locator("#password").fill(identity.password);await page.locator("#kc-login").click();
    await page.waitForURL(url=>url.origin===new URL(settings.baseURL).origin,{timeout:15000});
  }
  catch{throw new Error("Acceptance OIDC sign-in failed; credentials suppressed.");}
  if(new URL(page.url()).origin===new URL(settings.issuer).origin){
    if(await page.locator("#input-error").isVisible().catch(()=>false))throw new Error("Acceptance OIDC credentials were rejected.");
    if(new URL(page.url()).pathname.includes("required-action"))throw new Error("Acceptance OIDC required action blocked callback.");
    throw new Error("Acceptance OIDC did not return to the BFF callback.");
  }
  await expect(page.getByText("Customer Portal",{exact:true}).first()).toBeVisible();
  const selected=await call(page,"/bff/customer/tenant",{organizationId:settings.organizationId,applicationCode:"nexa_connect"});expect(selected.status).toBe(200);
  await page.goto("/#payment-reviews");await page.reload();await expect(page.getByLabel("Branch UUID")).toBeVisible();
}
async function fixture(page,id,{fresh=true}={}){
  const result=await call(page,`${root}/${id}`);expect(result.status).toBe(200);
  const review=result.data;
  // A marker is a second guard, not proof that an environment is disposable.
  if(review.organizationId!==settings.organizationId||review.branchId!==settings.branchId||review.reason!==settings.fixtureReason||review.status!=="open")throw new Error("Review fixture ownership, run marker or open state is invalid; refusing mutation.");
  const history=await call(page,`${root}/${id}/history`);expect(history.status).toBe(200);
  if(fresh&&history.data.length!==0)throw new Error("Review fixture has history; provision a fresh fixture instead of replaying the suite.");
  return review;
}
async function open(page,id){
  await page.getByLabel("Branch UUID").fill(settings.branchId);await page.getByRole("button",{name:"Load reviews",exact:true}).click();
  await page.getByRole("row").filter({hasText:id}).getByRole("button",{name:"View review"}).click();
  await expect(page.getByText("Immutable decision history",{exact:true})).toBeVisible();
}
async function choose(page,label,reason){
  await page.getByLabel("Decision",{exact:true}).click();await page.getByTitle(label,{exact:true}).click();
  await page.getByLabel("Reason for decision").fill(reason);await page.getByRole("button",{name:"Review decision",exact:true}).click();
  await expect(page.getByRole("dialog")).toBeVisible();
}
async function resolve(page,id,review,resolution,reason){
  const csrf=await call(page,root+"/csrf");expect(csrf.status).toBe(200);
  return call(page,`${root}/${id}/resolve`,{resolution,reason,expectedConcurrencyVersion:review.concurrencyVersion},csrf.data.requestToken);
}

test("read-only identity is enforced by both UI and server",async({page})=>{
  await signIn(page,settings.reader);const review=await fixture(page,settings.orders.concurrency);
  const access=await call(page,`${root}/branches/${settings.branchId}/access`);expect(access.data).toEqual({canRead:true,canResolve:false});
  await open(page,settings.orders.concurrency);await expect(page.getByRole("button",{name:"Review decision",exact:true})).toHaveCount(0);
  expect((await resolve(page,settings.orders.concurrency,review,"escalate","acceptance denied attempt")).status).toBe(403);
  expect((await call(page,`${root}/${settings.orders.concurrency}/history`)).data).toHaveLength(0);
});

test("tenant switching and missing CSRF fail closed",async({page})=>{
  await signIn(page,settings.resolver);const id=settings.orders.concurrency,review=await fixture(page,id);
  expect((await call(page,`${root}/${id}/resolve`,{resolution:"escalate",reason:"acceptance missing csrf",expectedConcurrencyVersion:review.concurrencyVersion})).status).toBe(400);
  expect((await call(page,"/bff/customer/tenant",{organizationId:settings.otherOrganizationId,applicationCode:"nexa_connect"})).status).toBe(200);
  expect((await call(page,`${root}/${id}`)).status).toBe(404);
  expect((await call(page,`${root}/${id}/history`)).status).toBe(404);
  expect((await resolve(page,id,review,"escalate","acceptance cross tenant")).status).toBe(404);
  await page.reload();await expect(page.getByLabel("Branch UUID")).toHaveValue("");await expect(page.getByText("Review details",{exact:true})).toHaveCount(0);
});

test("competing sessions permit one commit and stale UI requires a new decision",async({page,browser})=>{
  await signIn(page,settings.resolver);const id=settings.orders.concurrency,review=await fixture(page,id);await open(page,id);await choose(page,"Escalate for investigation","acceptance stale UI");
  const otherContext=await browser.newContext({baseURL:settings.baseURL,ignoreHTTPSErrors:true,serviceWorkers:"block"});
  try{
    const other=await otherContext.newPage();await signIn(other,settings.resolver);
    const results=await Promise.all([resolve(page,id,review,"escalate","acceptance contender one"),resolve(other,id,review,"escalate","acceptance contender two")]);
    expect(results.map(x=>x.status).sort()).toEqual([200,409]);
    await page.getByRole("button",{name:"Confirm decision",exact:true}).click();await expect(page.getByText("This review changed.",{exact:false})).toBeVisible();
    await expect(page.getByLabel("Reason for decision")).toHaveValue("");
    const history=await call(page,`${root}/${id}/history`);expect(history.data).toHaveLength(1);
    expect(history.data[0].action).toBe("escalate");expect(history.data[0].concurrencyVersion).toBe(review.concurrencyVersion+1);
    expect(history.data[0].authorizationDecisionId).toMatch(/^[a-f0-9-]{36}$/i);
  }finally{await otherContext.close();}
});

for(const [key,label,resolution] of [["resume","Resume payment","resume_payment"],["void","Confirm void","confirm_void"]]){
  test(`confirmed ${resolution} commits one attributed history entry`,async({page})=>{
    await signIn(page,settings.resolver);const id=settings.orders[key],review=await fixture(page,id);await open(page,id);
    await choose(page,label,`acceptance ${resolution}`);await page.getByRole("button",{name:"Confirm decision",exact:true}).click();
    await expect(page.getByText("Resolution saved.",{exact:false})).toBeVisible();
    const current=await call(page,`${root}/${id}`);expect(current.data.status).toBe("resolved");expect(current.data.resolution).toBe(resolution);
    const history=await call(page,`${root}/${id}/history`);expect(history.data).toHaveLength(1);expect(history.data[0].action).toBe(resolution);
    expect(history.data[0].concurrencyVersion).toBe(review.concurrencyVersion+1);
    const me=await call(page,"/bff/customer/me");expect(history.data[0].actorSubjectId).toBe(me.data.subjectId);
    expect(history.data[0].authorizationDecisionId).toMatch(/^[a-f0-9-]{36}$/i);
  });
}

test("Inventory outage fails closed and requires an explicit fresh confirm-void decision",async({page})=>{
  await signIn(page,settings.resolver);const id=settings.orders.outage;await fixture(page,id);await open(page,id);
  await choose(page,"Confirm void","acceptance controlled outage");
  await setProxyEnabled(settings.faultControl,false);
  try{
    await page.getByRole("button",{name:"Confirm decision",exact:true}).click();
    await expect(page.getByText("Resolution was not confirmed.",{exact:false})).toBeVisible();
    const current=await fixture(page,id);expect(current.status).toBe("open");
    expect((await call(page,`${root}/${id}/history`)).data).toHaveLength(0);
  }finally{await setProxyEnabled(settings.faultControl,true);}
  await choose(page,"Confirm void","acceptance explicit recovery");
  await page.getByRole("button",{name:"Confirm decision",exact:true}).click();
  await expect(page.getByText("Resolution saved.",{exact:false})).toBeVisible();
  const history=await call(page,`${root}/${id}/history`);expect(history.data).toHaveLength(1);
  expect(history.data[0].action).toBe("confirm_void");
});

test("lost committed response refreshes history without automatic replay",async({page})=>{
  await signIn(page,settings.resolver);const id=settings.orders.lost;await fixture(page,id);await open(page,id);
  let submitted=0;
  await page.route(`**${root}/${id}/resolve`,async route=>{
    submitted++;const response=await route.fetch({maxRetries:0});expect(response.status()).toBe(200);await route.abort("failed");
  });
  await choose(page,"Escalate for investigation","acceptance lost response");await page.getByRole("button",{name:"Confirm decision",exact:true}).click();
  await expect(page.getByText("Resolution was not confirmed.",{exact:false})).toBeVisible();
  await expect(page.getByLabel("Reason for decision")).toHaveValue("");
  expect((await call(page,`${root}/${id}/history`)).data).toHaveLength(1);expect(submitted).toBe(1);
});
