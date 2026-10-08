import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",org="33333333-3333-3333-3333-333333333333",restaurant="55555555-5555-5555-5555-555555555555";
const ref={manifestId:branch,generation:1,evidenceVersion:"a".repeat(64),revisionEpoch:org,sourceRevision:0};
const seal={sealId:org,manifestId:branch,revisionEpoch:org,sourceRevision:0};
const snapshot=()=>({timeZone:"UTC",currency:"THB",fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",grossSales:100,completedRefunds:0,netSales:100,cashVariance:0,tenders:[],orderVersion:"a".repeat(64),paymentVersion:"a".repeat(64),posVersion:"a".repeat(64),unresolvedOrders:0,unresolvedPayments:0,unresolvedRefunds:0,openShifts:0,openCashSessions:0,pendingCashReviews:0,issues:[],observedAtUtc:"2026-09-03T00:00:00Z",cutoff:{order:ref,payment:ref,pos:ref,checkId:restaurant,sourcesCurrent:true,financialGaps:0,deliveryComplete:true,evidenceProtocolVersion:2}});
async function setup(page){
 const state={status:"not_prepared",version:0,canPrepare:true,pending:0,fail:false,bad:false,reviewed:true,posts:[],headers:[]};
 await page.route("**/bff/customer/**",async route=>{
  const request=route.request(),url=new URL(request.url()),path=url.pathname;let json;
  if(path.endsWith("/me"))json={subjectId:"manager"};
  else if(path.endsWith("/access"))json={subjectId:"manager",organizations:[{organizationId:org,organizationCode:"one",organizationName:"One",applicationCode:"nexa_connect"}]};
  else if(path.endsWith("/tenant"))json={subjectId:"manager",organizationId:org,applicationCode:"nexa_connect"};
  else if(path.endsWith("/day-close-cutoffs"))json={identity:{organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"},version:2,status:state.reviewed?"ready_for_review":"blocked",snapshot:snapshot(),blockers:state.reviewed?[]:["cutoff_superseded"],validatedAtUtc:"2026-09-03T00:00:00Z",canPrepare:state.canPrepare,pendingCommand:null};
  else if(path.endsWith("/day-close-seals/csrf"))json={requestToken:"csrf-only"};
  else if(path.endsWith("/day-close-seals")){
   if(request.method()==="POST"){
    state.posts.push(request.postDataJSON());state.headers.push(request.headers());
    if(state.fail)return route.fulfill({status:503,json:{title:"restricted-source-error"}});
    state.status="ready_for_review";state.version+=2;
   }
   const saved=state.version?{...snapshot(),seals:{order:seal,payment:seal,pos:seal,pendingChanges:0,journalComplete:!state.bad,deliveryComplete:true}}:null;
   json={identity:{organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"},version:state.version,status:state.status,snapshot:saved,blockers:state.status==="blocked"?["sealed_changes_pending"]:[],validatedAtUtc:state.status==="ready_for_review"?"2026-09-03T00:00:00Z":null,canPrepare:state.canPrepare,pendingCommand:null,pendingSealChanges:state.pending,latestSealComparison:state.comparison??null};
  }else return route.fulfill({status:404});
  return route.fulfill({json});
 });
 await page.goto("/#end-of-day");await page.getByLabel("End-of-day branch ID").fill(branch);await page.getByLabel("End-of-day business date").fill("2026-09-01");return state;
}
async function load(page){await page.getByRole("button",{name:"Load cutoff evidence",exact:true}).click();await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();}
test("shows the sealed baseline beside historical source corrections",async({page})=>{
 const state=await setup(page);await load(page);await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();
 state.status="blocked";state.pending=2;state.comparison={grossSales:120,completedRefunds:5,netSales:115,cashVariance:-10,tenders:[{method:"cash",currency:"THB",amount:120}],unknownChanges:0,checkedAtUtc:"2026-09-03T01:00:00Z",changes:[{source:"Order",revision:1,reason:"sales_date",recordKind:"orders",recordId:org,parentId:org,beforeStatus:"completed",afterStatus:"completed",beforeFinancialVersion:1,afterFinancialVersion:2,beforeFinancialAtUtc:"2026-09-01T01:00:00Z",afterFinancialAtUtc:"2026-08-30T01:00:00Z"}],changesTruncated:true};
 await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();
 await expect(page.getByRole("columnheader",{name:"Sealed baseline",exact:true})).toBeVisible();
 await expect(page.getByRole("row").filter({has:page.getByText("Gross sales",{exact:true})})).toContainText("120");
 await expect(page.getByRole("row").filter({has:page.getByText("Cash variance",{exact:true})})).toContainText("-10");
 await expect(page.getByRole("row").filter({has:page.getByText("Tender: cash",{exact:true})})).toContainText("120");
 await expect(page.getByText(/Saved evidence: THB gross 100/)).toHaveCount(2);
 await expect(page.getByText(/Order: .*sales date/)).toBeVisible();
 await expect(page.getByText("The change list is limited. Pending counts include every relevant or unknown change.")).toBeVisible();
});
test("seals a reviewed version with CSRF and clears readiness when late changes are observed",async({page})=>{
 const state=await setup(page);await load(page);await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();
 expect(state.posts[0]).toMatchObject({expectedVersion:0,reviewedCutoffVersion:2,branchId:branch,businessDate:"2026-09-01"});expect(state.posts[0].organizationId).toBeUndefined();expect(state.headers[0]["x-nexa-csrf"]).toBe("csrf-only");
 state.status="blocked";state.pending=3;state.version=3;await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toHaveCount(0);await expect(page.getByText(/Pending source changes at the last check: 3/)).toBeVisible();
 await expect(page.getByRole("button",{name:/approve|finalize/i})).toHaveCount(0);
});
test("new sealing requires loaded ready cutoff evidence",async({page})=>{
 await setup(page);await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();await expect(page.getByRole("button",{name:"Seal reviewed evidence",exact:true})).toBeDisabled();
 await page.getByRole("button",{name:"Load cutoff evidence",exact:true}).click();await expect(page.getByRole("button",{name:"Seal reviewed evidence",exact:true})).toBeEnabled();
});
test("accountants have read-only seal controls",async({page})=>{
 const state=await setup(page);state.canPrepare=false;await load(page);await expect(page.getByRole("button",{name:"Seal reviewed evidence",exact:true})).toHaveCount(0);expect(state.posts).toHaveLength(0);
});
test("missing journal proof cannot display sealed readiness",async({page})=>{
 const state=await setup(page);state.version=2;state.status="ready_for_review";state.bad=true;await load(page);
 await expect(page.getByText("sealed evidence",{exact:true})).toHaveCount(0);await expect(page.getByText(/Saved evidence:/)).toHaveCount(1);
});
test("an uncertain response retains the exact sealing command",async({page})=>{
 const state=await setup(page);await load(page);state.fail=true;await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();
 await expect(page.getByRole("status")).toContainText("unavailable");await expect(page.getByRole("button",{name:"Resume sealing",exact:true})).toBeVisible();state.fail=false;await page.getByRole("button",{name:"Resume sealing",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();expect(state.posts[1]).toEqual(state.posts[0]);await expect(page.getByText("restricted-source-error")).toHaveCount(0);
});
test("filter changes clear saved sealing state and a blocked cutoff disables new seals",async({page})=>{
 const state=await setup(page);await load(page);await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();
 await page.getByLabel("End-of-day business date").fill("2026-09-02");await expect(page.getByText("sealed evidence",{exact:true})).toHaveCount(0);
 await page.getByLabel("End-of-day business date").fill("2026-09-01");state.reviewed=false;await load(page);await expect(page.getByRole("button",{name:"Reseal reviewed evidence",exact:true})).toBeDisabled();
});
