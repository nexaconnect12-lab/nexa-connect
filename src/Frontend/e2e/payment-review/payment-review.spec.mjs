import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",order="22222222-2222-2222-2222-222222222222";
async function setup(page,{canRead=true,canResolve=true,conflict=false}={}){
  let version=1,status="open",posts=[],history=[];
  let tenant={subjectId:"operator",organizationId:"33333333-3333-3333-3333-333333333333",applicationCode:"nexa_connect"};
  const organizations=[{organizationId:tenant.organizationId,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},{organizationId:"44444444-4444-4444-4444-444444444444",organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}];
  const review=()=>({orderId:order,organizationId:tenant.organizationId,branchId:branch,status,reason:"void_failed",concurrencyVersion:version,createdAtUtc:"2026-08-30T01:00:00Z",updatedAtUtc:"2026-08-31T01:00:00Z"});
  await page.route("**/bff/customer/**",async route=>{
    const path=new URL(route.request().url()).pathname,method=route.request().method();let json;
    if(path.endsWith("/me"))json={subjectId:"operator"};
    else if(path==="/bff/customer/access")json={subjectId:"operator",organizations};
    else if(path==="/bff/customer/tenant") {if(method==="POST")tenant={...tenant,...route.request().postDataJSON()};json=tenant;}
    else if(path.endsWith("/csrf"))json={requestToken:"test-csrf"};
    else if(path.endsWith("/access"))json={canRead,canResolve};
    else if(path.endsWith("/resolve")){
      posts.push({body:route.request().postDataJSON(),headers:route.request().headers()});version++;
      if(conflict)return route.fulfill({status:409,json:{title:"Conflict"}});
      status="resolved";history=[{id:"history-1",action:posts.at(-1).body.resolution,reason:posts.at(-1).body.reason,actorSubjectId:"operator",authorizationDecisionId:"decision-1",concurrencyVersion:version,occurredAtUtc:"2026-08-31T01:00:00Z"}];json=review();
    }
    else if(path.endsWith("/history"))json=history;
    else if(path.endsWith(`/branches/${branch}`))json=status==="open"?[review()]:[];
    else if(path.endsWith(`/${order}`))json=review();
    else return route.fulfill({status:404,json:{title:"Unexpected route"}});
    return route.fulfill({json});
  });
  await page.goto("/#payment-reviews");await page.getByLabel("Branch UUID").fill(branch);await page.getByRole("button",{name:"Load reviews"}).click();
  return posts;
}
async function openDecision(page){
  await page.getByRole("button",{name:"View review"}).click();
  await page.getByLabel("Decision",{exact:true}).click();await page.getByTitle("Resume payment",{exact:true}).click();
  await page.getByLabel("Reason for decision").fill("Verified externally");await page.getByRole("button",{name:"Review decision",exact:true}).click();
  await expect(page.getByRole("dialog")).toBeVisible();
}
test("read-only and denied branch access do not expose mutation controls",async({page})=>{
  await setup(page,{canResolve:false});await page.getByRole("button",{name:"View review"}).click();
  await expect(page.getByText("Read-only access:",{exact:false})).toBeVisible();
  await expect(page.getByRole("button",{name:"Review decision",exact:true})).toHaveCount(0);
});
test("denied branch has no list or decision form",async({page})=>{
  await setup(page,{canRead:false});await expect(page.getByText("You do not have permission",{exact:false})).toBeVisible();
  await expect(page.getByRole("button",{name:"View review"})).toHaveCount(0);
});
test("required reason, confirmation, CSRF and immutable history",async({page})=>{
  const posts=await setup(page);await page.getByRole("button",{name:"View review"}).click();
  await page.getByRole("button",{name:"Review decision",exact:true}).click();await expect(page.getByRole("dialog")).toHaveCount(0);expect(posts).toHaveLength(0);
  await page.getByLabel("Decision",{exact:true}).click();await page.getByTitle("Resume payment",{exact:true}).click();
  await page.getByLabel("Reason for decision").fill("Verified externally");await page.getByRole("button",{name:"Review decision",exact:true}).click();
  expect(posts).toHaveLength(0);await page.getByRole("button",{name:"Confirm decision",exact:true}).click();
  await expect(page.getByText("Resolution saved.",{exact:false})).toBeVisible();
  expect(posts).toHaveLength(1);expect(posts[0].headers["x-nexa-csrf"]).toBe("test-csrf");
  expect(posts[0].body).toEqual({resolution:"resume_payment",reason:"Verified externally",expectedConcurrencyVersion:1});
  await expect(page.getByRole("cell",{name:"Verified externally",exact:true})).toBeVisible();
  await expect(page.getByRole("button",{name:"Review decision",exact:true})).toHaveCount(0);
});
test("409 refreshes version and clears decision without replay",async({page})=>{
  const posts=await setup(page,{conflict:true});await openDecision(page);await page.getByRole("button",{name:"Confirm decision",exact:true}).click();
  await expect(page.getByText("This review changed.",{exact:false})).toBeVisible();
  await expect(page.getByLabel("Reason for decision")).toHaveValue("");expect(posts).toHaveLength(1);
  await page.getByLabel("Decision",{exact:true}).click();await page.getByTitle("Resume payment",{exact:true}).click();
  await page.getByLabel("Reason for decision").fill("Reviewed new state");await page.getByRole("button",{name:"Review decision",exact:true}).click();
  await page.getByRole("button",{name:"Confirm decision",exact:true}).click();await expect.poll(()=>posts.length).toBe(2);
  expect(posts[1].body.expectedConcurrencyVersion).toBe(2);
});
test("tenant switch clears branch, selected review and pending confirmation",async({page})=>{
  const posts=await setup(page);await openDecision(page);await page.getByRole("button",{name:"Cancel",exact:true}).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByLabel("Branch UUID")).toHaveValue("");await expect(page.getByText("Review details",{exact:true})).toHaveCount(0);
  expect(posts).toHaveLength(0);
});
