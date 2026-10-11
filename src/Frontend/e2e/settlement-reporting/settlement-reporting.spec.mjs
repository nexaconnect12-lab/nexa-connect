import {test,expect} from "@playwright/test";
const org="33333333-3333-4333-8333-333333333333",branch="11111111-1111-4111-8111-111111111111",other="22222222-2222-4222-8222-222222222222";
async function setup(page){
 const state={failure:false,malformed:false,delay:0,missing:false,tenant:{subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"}};
 await page.route("**/bff/customer/**",async route=>{
  const u=new URL(route.request().url()),p=u.pathname;let json;
  if(p.endsWith("/me"))json={subjectId:"operator"};
  else if(p.endsWith("/access"))json={subjectId:"operator",organizations:[{organizationId:org,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},{organizationId:other,organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}]};
  else if(p==="/bff/customer/tenant"){if(route.request().method()==="POST")state.tenant={...state.tenant,...route.request().postDataJSON()};json=state.tenant;}
  else if(p.endsWith("/reports/day-settlements")){
   if(state.failure)return route.fulfill({status:403,json:{detail:"private-upstream-detail"}});
   const now=new Date().toISOString();json={organizationId:state.malformed?other:state.tenant.organizationId,branchId:branch,fromDate:u.searchParams.get("fromDate"),toDate:u.searchParams.get("toDate"),sourceObservedAtUtc:now,comparedAtUtc:now,manifestHash:"A".repeat(64),status:state.missing?"gaps":"matched",expected:1,matched:state.missing?0:1,missing:state.missing?1:0,conflicting:0,unexpected:0,
    items:[{eventId:branch,settlementId:branch,businessDate:"2026-09-01",settledAtUtc:"2026-09-02T01:00:00Z",approvalId:org,sealVersion:1,currency:"THB",grossSales:"999999999999999.9999",completedRefunds:"1",netSales:"999999999999998.9999",cashVariance:"-2",contractVersion:2,correctionEvidence:"verified",status:state.missing?"missing":"matched",sources:["Order","Payment","POS"].map(source=>({source,sealId:branch,epoch:org,revision:1})),decisionId:org,fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",corrections:{manifestId:branch,revisionEpoch:org,sourceRevision:1,manifestHash:"a".repeat(64),expected:1,matched:1,missing:0,conflicting:0,unexpected:0,adjustment:"-10",items:[{eventId:other,correctionId:other,originalSettlementId:org,workId:other,orderId:other,tenderId:other,drawerId:other,postingDate:"2026-09-01",postedAtUtc:"2026-09-01T01:00:00Z",adjustment:"-10"}]}}]};
   if(state.delay)await new Promise(ok=>setTimeout(ok,state.delay));
  }else return route.fulfill({status:404});
  return route.fulfill({json}).catch(()=>{});
 });
 await page.goto("/#end-of-day");await page.getByLabel("End-of-day branch ID").fill(branch);await page.getByLabel("End-of-day business date").fill("2026-09-01");
 await page.getByLabel("Settlement from date",{exact:true}).fill("2026-09-01");await page.getByLabel("Settlement to date (exclusive)").fill("2026-09-02");return state;
}
const load=page=>page.getByRole("button",{name:"Load settlement ledger",exact:true}).click();
test("shows exact financial columns and separate retained correction proof",async({page})=>{await setup(page);await load(page);await expect(page.getByText("Settlement delivery matched",{exact:true})).toBeVisible();await expect(page.getByText("999999999999999.9999",{exact:true})).toBeVisible();await page.locator('.ant-table-row-expand-icon').first().click();await expect(page.getByText("Original settlement",{exact:true})).toBeVisible();await expect(page.getByText("verified",{exact:true})).toBeVisible();});
test("missing delivery remains a gap without changing financial values",async({page})=>{const s=await setup(page);s.missing=true;await load(page);await expect(page.getByText("Settlement delivery has gaps",{exact:true})).toBeVisible();await expect(page.getByText("999999999999999.9999",{exact:true})).toBeVisible();});
test("denied and malformed responses clear rows and hide private diagnostics",async({page})=>{const s=await setup(page);await load(page);await expect(page.getByText("Settlement delivery matched",{exact:true})).toBeVisible();s.failure=true;await load(page);await expect(page.getByText("Settlement ledger unavailable.",{exact:false})).toBeVisible();await expect(page.getByText("999999999999999.9999",{exact:true})).toHaveCount(0);await expect(page.getByText("private-upstream-detail")).toHaveCount(0);s.failure=false;s.malformed=true;await load(page);await expect(page.getByText("Settlement ledger unavailable.",{exact:false})).toBeVisible();});
test("date changes discard delayed stale results",async({page})=>{const s=await setup(page);s.delay=700;await load(page);await page.getByLabel("Settlement from date",{exact:true}).fill("2026-09-02");await page.waitForTimeout(900);await expect(page.getByText("Settlement delivery matched",{exact:true})).toHaveCount(0);await expect(page.getByText("999999999999999.9999",{exact:true})).toHaveCount(0);});
