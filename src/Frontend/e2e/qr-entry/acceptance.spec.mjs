import {test,expect} from "@playwright/test";
const org="11111111-1111-1111-1111-111111111111",branch="22222222-2222-2222-2222-222222222222",id="33333333-3333-3333-3333-333333333333",product="44444444-4444-4444-4444-444444444444";
const quote={quoteId:id,fingerprint:"A".repeat(64),currency:"THB",expiresAtUtc:new Date(Date.now()+120000).toISOString(),lines:[{productId:product,name:"Soup",unitPrice:107,quantity:1,lineTotal:107}],menuAmount:107,subtotalAmount:100,serviceChargeAmount:10,taxAmount:7.7,totalAmount:117.7,taxPercent:7,taxInclusive:true,serviceChargePercent:10};
const request={id,tableCode:"A1",status:"pending",version:1,submittedAtUtc:new Date().toISOString(),deadlineUtc:new Date(Date.now()+900000).toISOString(),quote};
test("staff acceptance retains original Order identity, version and CSRF",async({page})=>{
 let current=request;const inputs=[];
 await page.route("**/bff/customer/**",async route=>{const path=new URL(route.request().url()).pathname;let json;
  if(path.endsWith("/me"))json={subjectId:"operator"};else if(path.endsWith("/access"))json={organizations:[{organizationId:org,organizationName:"One",applicationCode:"nexa_connect"}]};
  else if(path.endsWith("/tenant"))json={subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"};
  else if(path.includes("operational-branches"))json=[{branchId:branch,name:"Branch",code:"B",currency:"THB",timeZone:"Asia/Singapore"}];
  else if(path.endsWith("/csrf"))json={requestToken:"synthetic-csrf",acceptanceAvailable:true};
  else if(path.endsWith("/accept")){inputs.push({body:route.request().postDataJSON(),csrf:route.request().headers()["x-nexa-csrf"]});current={...request,status:"accepted",version:2,orderId:id,orderStatus:"submitted"};json=current;}
  else if(path.includes("/qr-requests/branches/"))json={items:current.status===(new URL(route.request().url()).searchParams.get("state")??"pending")?[current]:[],hasMore:false};
  else return route.fulfill({status:404,json:{}});return route.fulfill({json});
 });
 await page.goto("/#qr-requests");await page.getByLabel("QR review branch").selectOption(branch);await page.getByRole("button",{name:"Accept original request",exact:true}).click();
 await expect(page.getByText("No pending requests.")).toBeVisible();expect(inputs).toEqual([{body:{expectedVersion:1},csrf:"synthetic-csrf"}]);
 await page.getByLabel("Request state",{exact:true}).selectOption("accepted");await expect(page.getByText(`Original Order ${id}`,{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Accept original request",exact:true})).toHaveCount(0);
});
test("guest reviews exact changed totals and confirms original request without staff authority",async({page})=>{
 const review={...quote,fingerprint:"B".repeat(64),lines:[{...quote.lines[0],unitPrice:214,lineTotal:214}],totalAmount:235.4,menuAmount:214,subtotalAmount:200,serviceChargeAmount:20,taxAmount:15.4};
 let current={...request,status:"price_review",version:2,reviewQuote:review};const calls=[];
 await page.route("**/bff/**",async route=>{const path=new URL(route.request().url()).pathname;calls.push(path);
  if(path==="/bff/guest/qr/context")return route.fulfill({json:{visitorId:"visitor",entryId:"entry",restaurantName:"R",branchName:"B",tableCode:"A1",currency:"THB",expiresAtUtc:new Date(Date.now()+3600000).toISOString(),menuAvailable:true,requestsAvailable:true,orderingAvailable:false}});
  if(path===`/bff/guest/qr/requests/${id}/confirm-price`){expect(route.request().postDataJSON()).toEqual({expectedVersion:2,fingerprint:review.fingerprint});current={...request,version:3,quote:review};return route.fulfill({json:current});}
  if(path==="/bff/guest/qr/requests")return route.fulfill({json:{items:[current],hasMore:false}});return route.fulfill({status:404,json:{}});
 });
 await page.goto("/order");await expect(page.getByRole("region",{name:"Updated prices"})).toBeVisible();await expect(page.getByText("Prices changed.",{exact:false})).toBeVisible();
 await page.getByRole("button",{name:"Confirm updated prices for staff review",exact:true}).click();await expect(page.getByRole("region",{name:"Updated prices"})).toHaveCount(0);
 expect(calls.every(p=>p.startsWith("/bff/guest/qr"))).toBe(true);await expect(page.getByText("A request is pending.",{exact:false})).toBeVisible();
});
