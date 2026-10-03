import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",otherBranch="22222222-2222-2222-2222-222222222222";
const org="33333333-3333-3333-3333-333333333333",otherOrg="44444444-4444-4444-4444-444444444444";
async function setup(page){
  const state={status:"observed_complete",fail:0,delay:0,queries:[],writes:0,wrongScope:false};
  let tenant={subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"};
  const organizations=[{organizationId:org,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},
    {organizationId:otherOrg,organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}];
  await page.route("**/bff/customer/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;let json;
    if(path.endsWith("/me"))json={subjectId:"operator"};
    else if(path==="/bff/customer/access")json={subjectId:"operator",organizations};
    else if(path==="/bff/customer/tenant"){
      if(route.request().method()==="POST")tenant={...tenant,...route.request().postDataJSON()};json=tenant;
    }else if(path.endsWith("/reports/sales")){
      state.queries.push({path,params:url.searchParams});if(route.request().method()!=="GET")state.writes++;
      json={items:[{orderId:"sale-one",channel:"pos",serviceType:"takeaway",currency:"THB",totalAmount:100,orderStatus:"completed",orderedAtUtc:"2026-09-01T01:00:00Z"}],grossSales:100,refundedAmount:25,netSales:75,currency:"THB"};
    }else if(path.endsWith("/reports/financial-completeness")){
      state.queries.push({path,params:url.searchParams});if(route.request().method()!=="GET")state.writes++;
      const status=state.status,fail=state.fail,delay=state.delay;
      const counts={expected:1,matched:1,missing:0,conflicting:0,unexpected:0,gaps:0};
      const observation={checkId:"55555555-5555-5555-5555-555555555555",status,
        range:{organizationId:tenant.organizationId,branchId:state.wrongScope?otherBranch:url.searchParams.get("branchId"),fromUtc:url.searchParams.get("fromUtc"),toUtc:url.searchParams.get("toUtc")},
        checkedAtUtc:"2026-09-03T02:00:00Z",orderObservedAtUtc:"2026-09-03T01:59:00Z",refundObservedAtUtc:"2026-09-03T01:59:30Z",
        saleEvidenceGaps:0,refundEvidenceGaps:status==="gaps_detected"?1:0,unretainedSales:0,unretainedRefunds:0,
        sales:counts,payments:counts,refunds:status==="gaps_detected"?{expected:1,matched:0,missing:1,conflicting:0,unexpected:0,gaps:1}:counts};
      json={status,observation:status==="not_checked"?null:observation};
      if(delay)await new Promise(resolve=>setTimeout(resolve,delay));
      if(fail)return route.fulfill({status:fail,json:{title:"restricted-upstream-error"}}).catch(()=>{});
    }else return route.fulfill({status:404});
    return route.fulfill({json}).catch(()=>{});
  });
  await page.goto("/#reports");await page.getByLabel("Sales branch ID").fill(branch);
  await page.getByLabel("Sales from UTC").fill("2026-09-01T00:00");await page.getByLabel("Sales to UTC").fill("2026-09-02T00:00");
  return state;
}
const load=page=>page.getByRole("button",{name:"Load sales report"}).click();
const card=page=>page.getByLabel("Financial reconciliation");
test("shows recorded status, provenance and three inventories for the same report window",async({page})=>{
  const state=await setup(page);await load(page);
  await expect(card(page).getByRole("status")).toContainText("Observed complete");
  await expect(card(page).getByText("2026-09-03T02:00:00Z",{exact:true})).toBeVisible();
  for(const kind of ["Sales","Payments","Refunds"])await expect(card(page).getByRole("cell",{name:kind,exact:true})).toBeVisible();
  expect(state.queries).toHaveLength(2);expect(state.queries[0].params.toString()).toBe(state.queries[1].params.toString());expect(state.writes).toBe(0);
  await expect(page.getByRole("cell",{name:"THB 100",exact:true})).toHaveCount(2);
});
test("not checked and gaps are explicit and never claim certification",async({page})=>{
  const state=await setup(page);state.status="not_checked";await load(page);await expect(card(page).getByRole("status")).toContainText("Not checked");
  await expect(card(page).getByRole("table")).toHaveCount(0);state.status="gaps_detected";await load(page);
  await expect(card(page).getByRole("status")).toContainText("Gaps detected");await expect(card(page).getByText("Sales: 0; refunds: 1",{exact:true})).toBeVisible();
});
test("revoked access clears both totals and previous complete evidence",async({page})=>{
  const state=await setup(page);await load(page);await expect(card(page).getByRole("status")).toContainText("Observed complete");
  state.fail=403;await load(page);await expect(card(page).getByRole("status")).toContainText("access denied");
  await expect(page.getByRole("cell",{name:"THB 100",exact:true})).toHaveCount(0);await expect(card(page).getByRole("table")).toHaveCount(0);
  await expect(page.getByText("restricted-upstream-error")).toHaveCount(0);
});
test("dependency failure removes old observation while current totals remain independently available",async({page})=>{
  const state=await setup(page);await load(page);await expect(card(page).getByRole("status")).toContainText("Observed complete");
  state.fail=503;await load(page);await expect(card(page).getByRole("status")).toContainText("unavailable");
  await expect(card(page).getByRole("table")).toHaveCount(0);await expect(page.getByRole("cell",{name:"THB 100",exact:true})).toHaveCount(2);
});
test("changing filters cancels a delayed response and permits a new request without stale evidence",async({page})=>{
  const state=await setup(page);state.delay=800;await load(page);
  await expect.poll(()=>state.queries.length).toBe(2);await page.getByLabel("Sales branch ID").fill(otherBranch);
  state.delay=0;state.status="not_checked";await load(page);await expect(card(page).getByRole("status")).toContainText("Not checked");
  await page.waitForTimeout(1000);await expect(card(page).getByRole("status")).toContainText("Not checked");
  await expect(card(page).getByText("Observed complete",{exact:true})).toHaveCount(0);
});
test("tenant switch clears results and aborts the previous tenant's pending read",async({page})=>{
  const state=await setup(page);await load(page);await expect(card(page).getByRole("status")).toContainText("Observed complete");state.delay=800;await load(page);
  await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByLabel("Sales branch ID")).toHaveValue("");await page.waitForTimeout(1000);
  await expect(card(page).getByRole("table")).toHaveCount(0);await expect(page.getByRole("cell",{name:"THB 100",exact:true})).toHaveCount(0);
});
test("invalid open or overlong windows cannot send a request",async({page})=>{
  const state=await setup(page);await page.getByLabel("Sales to UTC").fill("2099-09-02T00:00");await expect(page.getByRole("button",{name:"Load sales report"})).toBeDisabled();
  await page.getByLabel("Sales to UTC").fill("2026-09-02T00:00");await page.getByLabel("Sales from UTC").fill("2026-07-01T00:00");
  await expect(page.getByRole("button",{name:"Load sales report"})).toBeDisabled();expect(state.queries).toHaveLength(0);
});
test("a mismatched observation scope fails closed",async({page})=>{
  const state=await setup(page);state.wrongScope=true;await load(page);await expect(card(page).getByRole("status")).toContainText("unavailable");
  await expect(card(page).getByRole("table")).toHaveCount(0);
});
test("a timed-out read cannot restore evidence and a subsequent load can recover",async({page})=>{
  const state=await setup(page);await page.clock.install();state.delay=800;await load(page);
  await expect.poll(()=>state.queries.length).toBe(2);await page.clock.fastForward(26_000);
  await expect(card(page).getByRole("status")).toContainText("unavailable");
  await page.waitForTimeout(1000);
  await expect(card(page).getByRole("table")).toHaveCount(0);
  await expect(page.getByRole("cell",{name:"THB 100",exact:true})).toHaveCount(0);
  state.delay=0;state.status="not_checked";await load(page);
  await expect(card(page).getByRole("status")).toContainText("Not checked");
});
