import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",other="22222222-2222-2222-2222-222222222222";
const org="33333333-3333-3333-3333-333333333333",otherOrg="44444444-4444-4444-4444-444444444444",restaurant="55555555-5555-5555-5555-555555555555";
async function setup(page){
  let tenant={subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"};
  const state={fail:0,delay:0,wrongScope:false,reads:0,writes:0};
  const organizations=[{organizationId:org,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},{organizationId:otherOrg,organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}];
  await page.route("**/bff/customer/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;let json;
    if(path.endsWith("/me"))json={subjectId:"operator"};
    else if(path==="/bff/customer/access")json={subjectId:"operator",organizations};
    else if(path==="/bff/customer/tenant"){
      if(route.request().method()==="POST")tenant={...tenant,...route.request().postDataJSON()};json=tenant;
    }else if(path.endsWith("/reports/end-of-day")){
      state.reads++;if(route.request().method()!=="GET")state.writes++;
      const fail=state.fail,delay=state.delay,id=url.searchParams.get("branchId");
      const window={organizationId:tenant.organizationId,restaurantId:restaurant,branchId:state.wrongScope?other:id,fromUtc:"2026-08-31T17:00:00Z",toUtc:"2026-09-01T17:00:00Z"};
      const source={window,observedAtUtc:"2026-09-02T00:00:00Z"};const amount=id===other?222:100;
      json={businessDate:url.searchParams.get("businessDate"),status:"draft",window,branch:{organizationId:tenant.organizationId,restaurantId:restaurant,branchId:id,timeZone:"Asia/Bangkok",currency:"THB"},grossSales:amount,completedRefunds:25,netSales:amount-25,cashVariance:2,tenders:[{method:"cash",currency:"THB",amount}],order:{...source,completedOrders:1,unresolvedOrders:0,evidenceGaps:0},payment:{...source,unresolvedPayments:0,unresolvedRefunds:1,evidenceGaps:0},pos:{...source,openShifts:1,openCashSessions:0,pendingCashReviews:1},issues:["open_shifts","unresolved_refunds","pending_cash_reviews","projection_totals_differ","financial_evidence_not_checked"]};
      if(delay)await new Promise(resolve=>setTimeout(resolve,delay));
      if(fail)return route.fulfill({status:fail,json:{title:"restricted-source-error"}}).catch(()=>{});
    }else return route.fulfill({status:404});
    return route.fulfill({json}).catch(()=>{});
  });
  await page.goto("/#end-of-day");await page.getByLabel("End-of-day branch ID").fill(branch);await page.getByLabel("End-of-day business date").fill("2026-09-01");
  return state;
}
const load=page=>page.getByRole("button",{name:"Load day draft"}).click();
test("shows source totals, branch day boundaries, tender and unresolved work without writes",async({page})=>{
  const state=await setup(page);await load(page);await expect(page.getByText("Asia/Bangkok",{exact:true})).toBeVisible();
  await expect(page.getByRole("cell",{name:"THB 75",exact:true})).toBeVisible();await expect(page.getByText("Reporting totals differ from source totals",{exact:true})).toBeVisible();
  await expect(page.getByText("Open shifts",{exact:true})).toBeVisible();expect(state.writes).toBe(0);expect(state.reads).toBe(1);
});
for(const status of [403,503])test(`failure ${status} clears old totals and omits upstream details`,async({page})=>{
  const state=await setup(page);await load(page);await expect(page.getByText("Asia/Bangkok",{exact:true})).toBeVisible();
  state.fail=status;await load(page);await expect(page.getByRole("status")).toContainText("unavailable");
  await expect(page.getByRole("table")).toHaveCount(0);await expect(page.getByText("restricted-source-error")).toHaveCount(0);
});
test("filter changes and delayed old responses cannot restore stale results",async({page})=>{
  const state=await setup(page);state.delay=800;await load(page);await expect.poll(()=>state.reads).toBe(1);
  await page.getByLabel("End-of-day branch ID").fill(other);state.delay=0;await load(page);
  await expect(page.getByRole("cell",{name:"THB 222",exact:true})).toBeVisible();await page.waitForTimeout(1000);
  await expect(page.getByRole("cell",{name:"THB 100",exact:true})).toHaveCount(0);
});
test("tenant switch clears results and pending reads",async({page})=>{
  const state=await setup(page);await load(page);await expect(page.getByText("Asia/Bangkok",{exact:true})).toBeVisible();state.delay=800;await load(page);
  await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByLabel("End-of-day branch ID")).toHaveValue("");await page.waitForTimeout(1000);await expect(page.getByRole("table")).toHaveCount(0);
});
test("mismatched source scope cannot display financial data",async({page})=>{
  const state=await setup(page);state.wrongScope=true;await load(page);await expect(page.getByRole("status")).toContainText("unavailable");await expect(page.getByRole("table")).toHaveCount(0);
});

async function correctionSetup(page){
 await setup(page);const state={status:200,delay:0,wrongScope:false,gap:false};
 await page.route("**/bff/customer/reports/cash-corrections?**",async route=>{
  const query=new URL(route.request().url()).searchParams,delay=state.delay;
  const json={organizationId:state.wrongScope?otherOrg:org,branchId:query.get("branchId"),fromUtc:query.get("fromUtc"),toUtc:query.get("toUtc"),sourceObservedAtUtc:"2026-09-03T00:00:00Z",comparedAtUtc:"2026-09-03T00:00:00Z",manifestHash:"A".repeat(64),status:state.gap?"gaps":"matched",expected:1,matched:state.gap?0:1,missing:state.gap?1:0,conflicting:0,unexpected:0,sourceAdjustment:"-10.0001",projectedAdjustment:state.gap?"0":"-10.0001",items:[{eventId:branch,correctionId:branch,originalSettlementId:org,workId:branch,orderId:branch,tenderId:branch,drawerId:branch,postingDate:"2026-09-01",postedAtUtc:"2026-09-01T01:00:00Z",currency:"THB",adjustment:"-10.0001",status:state.gap?"missing":"matched"}]};
  const status=state.status;if(delay)await new Promise(r=>setTimeout(r,delay));await route.fulfill({status,json:status===200?json:{detail:"restricted-source-error"}}).catch(()=>{});
 });
 await page.getByLabel("Correction from (UTC)").fill("2026-09-01T00:00");await page.getByLabel("Correction to (UTC, exclusive)").fill("2026-09-02T00:00");return state;
}
const compare=page=>page.getByRole("button",{name:"Compare cash correction delivery",exact:true}).click();
test("correction comparison shows separate exact adjustments and delayed delivery gaps",async({page})=>{
 const state=await correctionSetup(page);state.gap=true;await compare(page);await expect(page.getByText("Cash correction delivery has gaps",{exact:true})).toBeVisible();
 await expect(page.getByRole("cell",{name:"-10.0001",exact:true})).toBeVisible();state.gap=false;await compare(page);await expect(page.getByText("Cash correction delivery matched",{exact:true})).toBeVisible();
});
for(const status of [403,503])test(`correction reporting ${status} clears prior evidence`,async({page})=>{
 const state=await correctionSetup(page);await compare(page);await expect(page.getByText("Cash correction delivery matched",{exact:true})).toBeVisible();state.status=status;await compare(page);
 await expect(page.getByRole("status")).toContainText("Cash correction reporting unavailable");await expect(page.getByRole("table")).toHaveCount(0);await expect(page.getByText("restricted-source-error")).toHaveCount(0);
});
test("correction reporting rejects other tenant evidence",async({page})=>{
 const state=await correctionSetup(page);state.wrongScope=true;await compare(page);await expect(page.getByRole("status")).toContainText("Cash correction reporting unavailable");await expect(page.getByRole("table")).toHaveCount(0);
});
test("correction filter change prevents delayed evidence restoring old scope",async({page})=>{
 const state=await correctionSetup(page);state.delay=600;await compare(page);await page.getByLabel("Correction from (UTC)").fill("2026-09-01T00:30");await page.waitForTimeout(800);await expect(page.getByRole("table")).toHaveCount(0);
});
