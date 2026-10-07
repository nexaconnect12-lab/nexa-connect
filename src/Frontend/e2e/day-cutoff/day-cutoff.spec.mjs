import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",other="22222222-2222-2222-2222-222222222222",org="33333333-3333-3333-3333-333333333333",otherOrg="44444444-4444-4444-4444-444444444444",restaurant="55555555-5555-5555-5555-555555555555";
async function setup(page){
  let tenant={subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"};
  const state={canPrepare:true,status:"not_prepared",version:0,fail:false,delay:0,badReady:false,posts:[],headers:[],pendingCommand:null};
  const organizations=[{organizationId:org,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},{organizationId:otherOrg,organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}];
  await page.route("**/bff/customer/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname;let json;
    if(path.endsWith("/me"))json={subjectId:"operator"};
    else if(path==="/bff/customer/access")json={subjectId:"operator",organizations};
    else if(path==="/bff/customer/tenant"){if(route.request().method()==="POST")tenant={...tenant,...route.request().postDataJSON()};json=tenant;}
    else if(path.endsWith("/day-close-cutoffs/csrf"))json={requestToken:"csrf-only"};
    else if(path.endsWith("/day-close-cutoffs")){
      const post=route.request().method()==="POST";const input=post?route.request().postDataJSON():Object.fromEntries(url.searchParams);
      if(post){state.posts.push(input);state.headers.push(route.request().headers());if(state.fail)return route.fulfill({status:503,json:{title:"restricted-source-error"}});state.version+=2;state.status="ready_for_review";}
      const snapshot=state.version?{timeZone:"UTC",currency:"THB",fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",grossSales:100,completedRefunds:25,netSales:75,cashVariance:0,tenders:[{method:"cash",currency:"THB",amount:100}],orderVersion:"a".repeat(64),paymentVersion:"b".repeat(64),posVersion:"c".repeat(64),unresolvedOrders:0,unresolvedPayments:0,unresolvedRefunds:0,openShifts:0,openCashSessions:0,pendingCashReviews:0,issues:["recorded_check_is_historical"],observedAtUtc:"2026-09-03T00:00:00Z",cutoff:{order:{manifestId:branch,generation:1,evidenceVersion:"a".repeat(64),revisionEpoch:org,sourceRevision:0},payment:{manifestId:other,generation:1,evidenceVersion:"b".repeat(64),revisionEpoch:org,sourceRevision:0},pos:{manifestId:org,generation:1,evidenceVersion:"c".repeat(64),revisionEpoch:org,sourceRevision:0},checkId:restaurant,sourcesCurrent:true,financialGaps:0,deliveryComplete:true,evidenceProtocolVersion:2}}:null;
      json={identity:{organizationId:tenant.organizationId,restaurantId:restaurant,branchId:input.branchId,businessDate:input.businessDate},version:state.version,status:state.status,snapshot,blockers:state.status==="blocked"?["source_evidence_changed"]:state.status==="not_prepared"?["not_prepared"]:[],validatedAtUtc:state.status==="ready_for_review"&&!state.badReady?"2026-09-03T00:00:01Z":null,canPrepare:state.canPrepare,pendingCommand:state.status==="preparing"?state.pendingCommand:null};
      if(state.delay)await new Promise(resolve=>setTimeout(resolve,state.delay));
    }else return route.fulfill({status:404});
    return route.fulfill({json}).catch(()=>{});
  });
  await page.goto("/#end-of-day");await page.getByLabel("End-of-day branch ID").fill(branch);await page.getByLabel("End-of-day business date").fill("2026-09-01");return state;
}
const load=page=>page.getByRole("button",{name:"Load cutoff evidence",exact:true}).click();
test("prepares with CSRF operation and reviewed version then displays invalidation without approval controls",async({page})=>{
  const state=await setup(page);await load(page);await page.getByRole("button",{name:"Capture cutoff evidence",exact:true}).click();await expect(page.getByText("ready for review",{exact:true})).toBeVisible();
  expect(state.posts[0]).toMatchObject({branchId:branch,businessDate:"2026-09-01",expectedVersion:0,reasonCode:"routine_close"});expect(state.posts[0].operationId).toMatch(/^[a-f0-9-]{36}$/);expect(state.headers[0]["x-nexa-csrf"]).toBe("csrf-only");expect(state.headers[0].authorization).toBeUndefined();expect(state.posts[0].organizationId).toBeUndefined();
  state.status="blocked";state.version=3;await load(page);await expect(page.getByText("source evidence changed",{exact:true})).toBeVisible();await expect(page.getByText("ready for review",{exact:true})).toHaveCount(0);await expect(page.getByRole("button",{name:/approve|lock/i})).toHaveCount(0);
  await expect(page.getByText("Sources unchanged at this validation.",{exact:false})).toHaveCount(0);
  await expect(page.getByText("Retained evidence is blocked or not freshly validated; resolve blockers and refresh.",{exact:false})).toBeVisible();
  await page.getByRole("button",{name:"Refresh cutoff evidence",exact:true}).click();await expect.poll(()=>state.posts.length).toBe(2);expect(state.posts[1].expectedVersion).toBe(3);expect(state.posts[1].operationId).not.toBe(state.posts[0].operationId);
});
test("accountants have no prepare controls",async({page})=>{const state=await setup(page);state.canPrepare=false;await load(page);await expect(page.getByRole("rowgroup").getByText("not prepared",{exact:true})).toBeVisible();await expect(page.getByRole("button",{name:"Capture cutoff evidence",exact:true})).toHaveCount(0);expect(state.posts).toHaveLength(0);});
test("uncertain POST retries preserve the exact operation and do not show upstream details",async({page})=>{
  const state=await setup(page);await load(page);state.fail=true;await page.getByRole("button",{name:"Capture cutoff evidence",exact:true}).click();await expect(page.getByRole("status")).toContainText("unavailable");await expect(page.getByText("restricted-source-error")).toHaveCount(0);
  state.fail=false;await page.getByRole("button",{name:"Resume cutoff",exact:true}).click();await expect(page.getByText("ready for review",{exact:true})).toBeVisible();expect(state.posts).toHaveLength(2);expect(state.posts[0]).toEqual(state.posts[1]);
});
test("filter change discards a late preparation response",async({page})=>{
  const state=await setup(page);await load(page);state.delay=800;await page.getByRole("button",{name:"Capture cutoff evidence",exact:true}).click();await expect.poll(()=>state.posts.length).toBe(1);await page.getByLabel("End-of-day branch ID").fill(other);await page.waitForTimeout(1000);await expect(page.getByText("ready for review",{exact:true})).toHaveCount(0);await expect(page.getByText(/Saved evidence:/)).toHaveCount(0);
});
test("tenant change clears saved and pending state",async({page})=>{
  const state=await setup(page);await load(page);await page.getByRole("button",{name:"Capture cutoff evidence",exact:true}).click();await expect(page.getByText("ready for review",{exact:true})).toBeVisible();await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();await expect(page.getByLabel("End-of-day branch ID")).toHaveValue("");await expect(page.getByText(/Saved evidence:/)).toHaveCount(0);expect(state.posts).toHaveLength(1);
});
test("malformed ready response cannot display readiness or totals",async({page})=>{const state=await setup(page);await load(page);state.badReady=true;await page.getByRole("button",{name:"Capture cutoff evidence",exact:true}).click();await expect(page.getByRole("status")).toContainText("unavailable");await expect(page.getByText("ready for review",{exact:true})).toHaveCount(0);await expect(page.getByText(/Saved evidence:/)).toHaveCount(0);});

test("browser reload restores the actor's saved pending operation for explicit resume",async({page})=>{
  const state=await setup(page);await load(page);state.fail=true;await page.getByRole("button",{name:"Capture cutoff evidence",exact:true}).click();await expect(page.getByRole("status")).toContainText("unavailable");
  state.status="preparing";state.version=1;state.pendingCommand=state.posts[0];state.fail=false;
  await page.reload();await page.getByLabel("End-of-day branch ID").fill(branch);await page.getByLabel("End-of-day business date").fill("2026-09-01");await load(page);await expect(page.getByText("preparing",{exact:true}).first()).toBeVisible();
  await page.getByRole("button",{name:"Resume cutoff",exact:true}).click();await expect(page.getByText("ready for review",{exact:true})).toBeVisible();expect(state.posts[1]).toEqual(state.posts[0]);
});
test("another manager can explicitly replace interrupted work using the loaded version",async({page})=>{
  const state=await setup(page);state.status="preparing";state.version=1;state.pendingCommand=null;await load(page);
  await page.getByRole("button",{name:"Replace interrupted cutoff",exact:true}).click();await expect(page.getByText("ready for review",{exact:true})).toBeVisible();expect(state.posts[0].expectedVersion).toBe(1);expect(state.posts[0].operationId).toMatch(/^[a-f0-9-]{36}$/);
});
