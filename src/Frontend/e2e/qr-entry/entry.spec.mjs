import {test,expect} from "@playwright/test";
const token="E".repeat(43),org="11111111-1111-1111-1111-111111111111",branch="22222222-2222-2222-2222-222222222222",entry="33333333-3333-3333-3333-333333333333";
const visit={visitorId:"guest",entryId:entry,restaurantName:"Restaurant",branchName:"Branch",tableCode:"A1",currency:"THB",expiresAtUtc:new Date(Date.now()+3600000).toISOString(),orderingAvailable:false};
test("scan removes fragment, creates one visit and never boots staff routes",async({page})=>{
 const requests=[];let starts=0;
 await page.route("**/bff/**",async route=>{const path=new URL(route.request().url()).pathname;requests.push(path);
  if(path==="/bff/guest/qr/sessions"){starts++;expect(route.request().postDataJSON()).toEqual({entryToken:token});return route.fulfill({json:visit});}
  return route.fulfill({status:404,json:{}});
 });
 await page.goto(`/order#${token}`);await expect(page.getByText("Table A1",{exact:true})).toBeVisible();
 expect(new URL(page.url()).hash).toBe("");expect(starts).toBe(1);expect(requests.every(p=>p.startsWith("/bff/guest/qr"))).toBe(true);
 await expect(page.getByText("Menu ordering is not available yet.",{exact:false})).toBeVisible();
 expect(await page.evaluate(()=>Object.keys(localStorage).concat(Object.keys(sessionStorage)))).toEqual([]);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});
test("refresh restores context and ending a visit does not expose a staff login",async({page})=>{
 const requests=[];await page.route("**/bff/**",async route=>{requests.push(route.request().method()+" "+new URL(route.request().url()).pathname);return route.request().method()==="DELETE"?route.fulfill({status:204}):route.fulfill({json:visit});});
 await page.goto("/order");await expect(page.getByText("Table A1",{exact:true})).toBeVisible();await page.reload();await expect(page.getByText("Table A1",{exact:true})).toBeVisible();
 await page.getByRole("button",{name:"End this visit"}).click();await expect(page.getByText("Your visit has ended.")).toBeVisible();
 expect(requests).toContain("DELETE /bff/guest/qr/sessions");expect(requests.some(r=>r.includes("customer"))).toBe(false);
});
test("revoked or unavailable QR displays bounded recovery feedback",async({page})=>{
 await page.route("**/bff/guest/qr/**",route=>route.fulfill({status:401,json:{title:"not forwarded"}}));await page.goto(`/order#${token}`);
 await expect(page.getByText("This table entry is unavailable.",{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Restore this visit"})).toBeVisible();
 expect(new URL(page.url()).hash).toBe("");await expect(page.getByRole("button",{name:"End this visit"})).toHaveCount(0);
});
test("staff QR issuance uses CSRF/version and discards secret on branch change",async({page})=>{
 let state={organizationId:org,applicationCode:"nexa_connect"};const writes=[];
 await page.route("**/bff/customer/**",async route=>{const path=new URL(route.request().url()).pathname;let json;
  if(path.endsWith("/me"))json={subjectId:"operator"};else if(path.endsWith("/access"))json={organizations:[{...state,organizationName:"One"}]};
  else if(path.endsWith("/tenant"))json=state;
  else if(path==="/bff/customer/branches"||path.endsWith("/operational-branches"))json=[{branchId:branch,name:"Branch",status:"active"}];
  else if(path.endsWith("/csrf"))json={requestToken:"synthetic-csrf"};
  else if(path.includes("/qr-entries/branches/")){
   if(route.request().method()==="POST"){writes.push({body:route.request().postDataJSON(),headers:route.request().headers()});json={entry:{id:entry,tableCode:"A1",version:writes.length,active:true,expiresAtUtc:visit.expiresAtUtc},entryUrl:`http://127.0.0.1:5199/order#${token}`};}
   else json=[];
  }else return route.fulfill({status:404,json:{}});return route.fulfill({json});
 });
 await page.goto("/#configuration");await page.getByLabel("QR branch").selectOption(branch);await page.getByLabel("Table code",{exact:true}).fill("A1");
 await page.getByRole("button",{name:"Create table QR"}).click();await expect(page.getByRole("link",{name:"Open table entry"})).toBeVisible();
 expect(writes[0].body).toEqual({tableCode:"A1"});expect(writes[0].headers["x-nexa-csrf"]).toBe("synthetic-csrf");
 await page.getByRole("button",{name:"Rotate / reissue"}).click();await expect.poll(()=>writes.length).toBe(2);expect(writes[1].body).toEqual({expectedVersion:1});
 await page.getByLabel("QR branch").selectOption("");await expect(page.getByRole("link",{name:"Open table entry"})).toHaveCount(0);
});
