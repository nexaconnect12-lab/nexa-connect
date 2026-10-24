import {test,expect} from "@playwright/test";
const branch="22222222-2222-2222-2222-222222222222",org="33333333-3333-3333-3333-333333333333",product="44444444-4444-4444-4444-444444444444",id="55555555-5555-5555-5555-555555555555";
const visit={visitorId:"guest",entryId:"entry",restaurantName:"Restaurant",branchName:"Branch",tableCode:"A1",currency:"THB",expiresAtUtc:new Date(Date.now()+3600000).toISOString(),menuAvailable:true,requestsAvailable:true,orderingAvailable:false};
const quote={quoteId:id,fingerprint:"A".repeat(64),currency:"THB",expiresAtUtc:new Date(Date.now()+120000).toISOString(),lines:[{productId:product,name:"Soup",quantity:1,unitPrice:107,lineTotal:107}],menuAmount:107,subtotalAmount:100,serviceChargeAmount:10,taxAmount:7.7,totalAmount:117.7,taxPercent:7,taxInclusive:true,serviceChargePercent:10};
const proposal={id,tableCode:"A1",status:"pending",version:1,submittedAtUtc:new Date().toISOString(),deadlineUtc:new Date(Date.now()+900000).toISOString(),quote};
async function setup(page,{lose=false}={}){
 const inputs=[];let saved=false;
 await page.route("**/bff/**",async route=>{
  const path=new URL(route.request().url()).pathname;
  if(path==="/bff/guest/qr/context")return route.fulfill({json:visit});
  if(path==="/bff/guest/qr/menu")return route.fulfill({json:{items:[{productId:product,name:"Soup",unitPrice:107,currency:"THB",available:true}]}});
  if(path==="/bff/guest/qr/quote")return route.fulfill({json:quote});
  if(path==="/bff/guest/qr/requests"){
   if(route.request().method()==="GET")return route.fulfill({json:{items:saved?[proposal]:[],hasMore:false}});
   inputs.push(route.request().postDataJSON());saved=true;if(lose&&inputs.length===1)return route.abort("failed");return route.fulfill({json:proposal});
  }
  return route.fulfill({status:404,json:{}});
 });
 await page.goto("/order");await expect(page.getByRole("button",{name:"Add Soup",exact:true})).toBeVisible();
 await page.getByRole("button",{name:"Add Soup",exact:true}).click();await page.getByRole("button",{name:"Review totals",exact:true}).click();await expect(page.getByRole("button",{name:"Submit for staff review",exact:true})).toBeVisible();
 return inputs;
}
test("submission retains original identity and refresh recovers only this visit's pending request",async({page})=>{
 const inputs=await setup(page);await page.getByRole("button",{name:"Submit for staff review",exact:true}).click();
 await expect(page.getByText("A request is pending.",{exact:false})).toBeVisible();expect(inputs).toEqual([{quoteId:id,fingerprint:quote.fingerprint}]);
 await page.reload();await expect(page.getByText("A request is pending.",{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Submit for staff review",exact:true})).toHaveCount(0);
 await expect(page.getByText("Ending this visit or rescanning does not cancel a request.",{exact:false})).toBeVisible();
 expect(await page.evaluate(()=>Object.keys(localStorage).concat(Object.keys(sessionStorage)))).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
test("lost committed response retries only the original identity",async({page})=>{
 const inputs=await setup(page,{lose:true});await page.getByRole("button",{name:"Submit for staff review",exact:true}).click();
 await expect(page.getByText("Submission outcome is unknown.",{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Submit for staff review",exact:true})).toHaveCount(0);
 await page.getByRole("button",{name:"Retry original request",exact:true}).click();await expect(page.getByText("A request is pending.",{exact:false})).toBeVisible();expect(inputs).toHaveLength(2);expect(inputs[1]).toEqual(inputs[0]);
});
test("lost response can be recovered by private status instead of resubmitting",async({page})=>{
 const inputs=await setup(page,{lose:true});await page.getByRole("button",{name:"Submit for staff review",exact:true}).click();
 await expect(page.getByText("Submission outcome is unknown.",{exact:false})).toBeVisible();await page.getByRole("button",{name:"Check request status",exact:true}).click();
 await expect(page.getByText("A request is pending.",{exact:false})).toBeVisible();expect(inputs).toHaveLength(1);await expect(page.getByRole("button",{name:"Retry original request",exact:true})).toHaveCount(0);
});
test("staff queue has versioned CSRF rejection and no acceptance command",async({page})=>{
 let saved=proposal;const writes=[];
 await page.route("**/bff/customer/**",async route=>{
  const path=new URL(route.request().url()).pathname;let json;
  if(path.endsWith("/me"))json={subjectId:"operator"};else if(path.endsWith("/access"))json={organizations:[{organizationId:org,organizationName:"One",applicationCode:"nexa_connect"}]};
  else if(path.endsWith("/tenant"))json={subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"};
  else if(path.includes("operational-branches"))json=[{branchId:branch,name:"Branch",code:"B1",currency:"THB",timeZone:"Asia/Singapore"}];
  else if(path.endsWith("/csrf"))json={requestToken:"synthetic-csrf"};
  else if(path.endsWith("/reject")){writes.push({body:route.request().postDataJSON(),csrf:route.request().headers()["x-nexa-csrf"]});saved={...proposal,status:"rejected",version:2,reason:"closing"};json=saved;}
  else if(path.includes("/qr-requests/branches/"))json={items:saved.status===(new URL(route.request().url()).searchParams.get("state")??"pending")?[saved]:[],hasMore:false};
  else return route.fulfill({status:404,json:{}});return route.fulfill({json});
 });
 await page.goto("/#qr-requests");await page.getByLabel("QR review branch").selectOption(branch);await expect(page.getByText("Table A1 · pending",{exact:false})).toBeVisible();
 await expect(page.getByRole("button",{name:/accept/i})).toHaveCount(0);await page.getByRole("button",{name:"Reject: closing",exact:true}).click();
 await expect(page.getByText("No pending requests.")).toBeVisible();expect(writes).toEqual([{body:{expectedVersion:1,reason:"closing"},csrf:"synthetic-csrf"}]);
 await page.getByLabel("Request state",{exact:true}).selectOption("rejected");await expect(page.getByText("Table A1 · rejected",{exact:false})).toBeVisible();
});
