import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",store="22222222-2222-2222-2222-222222222222";
async function setup(page){
  const state={status:"approved",version:2,empty:false,fail:false,queries:[],writes:0};
  let tenant={subjectId:"operator",organizationId:"33333333-3333-3333-3333-333333333333",applicationCode:"nexa_connect"};
  const organizations=[{organizationId:tenant.organizationId,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},{organizationId:"44444444-4444-4444-4444-444444444444",organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}];
  await page.route("**/bff/customer/**",async route=>{
    const url=new URL(route.request().url()),p=url.pathname;let json;
    if(p.endsWith("/me"))json={subjectId:"operator"};
    else if(p==="/bff/customer/access")json={subjectId:"operator",organizations};
    else if(p==="/bff/customer/tenant"){if(route.request().method()==="POST")tenant={...tenant,...route.request().postDataJSON()};json=tenant;}
    else if(p.endsWith("/reports/cash-close")){
      if(route.request().method()!=="GET")state.writes++;
      state.queries.push(url.searchParams);
      if(state.fail)return route.fulfill({status:403,json:{title:"Denied"}});
      json={items:state.empty?[]:[{snapshot:{sessionId:"session-one",shiftId:"shift-one",currency:"THB",expectedAmount:110,countedAmount:95,varianceAmount:-15,closedAtUtc:"2026-09-22T01:00:00Z",reviewStatus:state.status,financialVersion:state.version,reviewVersion:1,snapshotVersion:3,capturedAtUtc:"2026-09-22T02:00:00Z"},projectedAtUtc:"2026-09-22T02:01:00Z"}],nextCursor:url.searchParams.has("cursor")?null:"next-position"};
    }else return route.fulfill({status:404});
    return route.fulfill({json});
  });
  await page.goto("/#cash-close-reports");await page.getByLabel("Report branch UUID").fill(branch);await page.getByLabel("Report store UUID").fill(store);
  await page.getByRole("button",{name:"Load cash-close report"}).click();return state;
}
test("late settlement refresh replaces approval and displays per-row freshness without writes",async({page})=>{
  const state=await setup(page);await expect(page.getByRole("cell",{name:"approved",exact:true})).toBeVisible();
  await expect(page.getByText("2026-09-22T02:01:00Z")).toBeVisible();
  state.status="review_required";state.version=3;await page.getByRole("button",{name:"Load cash-close report"}).click();
  await expect(page.getByRole("cell",{name:"review_required",exact:true})).toBeVisible();
  expect(state.writes).toBe(0);expect(state.queries.at(-1).get("storeId")).toBe(store);
});
test("pagination preserves scope and tenant switch removes financial rows",async({page})=>{
  const state=await setup(page);await page.getByRole("button",{name:"Next report page"}).click();
  await expect(page.getByRole("button",{name:"Next report page"})).toBeDisabled();expect(state.queries.at(-1).get("cursor")).toBe("next-position");
  await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByLabel("Report store UUID")).toHaveValue("");await expect(page.getByRole("cell",{name:"session-one"})).toHaveCount(0);
});
test("revoked access clears stale financial rows",async({page})=>{
  const state=await setup(page);await expect(page.getByRole("cell",{name:"session-one"})).toBeVisible();state.fail=true;
  await page.getByRole("button",{name:"Load cash-close report"}).click();await expect(page.getByRole("status")).toContainText("Report unavailable");
  await expect(page.getByRole("cell",{name:"session-one"})).toHaveCount(0);
});
test("empty projection is not represented as a complete zero balance",async({page})=>{
  const state=await setup(page);await expect(page.getByRole("cell",{name:"session-one"})).toBeVisible();state.empty=true;
  await page.getByRole("button",{name:"Load cash-close report"}).click();
  await expect(page.getByText("No projected sessions in this page/range.",{exact:false})).toBeVisible();
  await page.getByLabel("Report from UTC").fill("2026-01-01T00:00");await expect(page.getByRole("button",{name:"Load cash-close report"})).toBeDisabled();
});
