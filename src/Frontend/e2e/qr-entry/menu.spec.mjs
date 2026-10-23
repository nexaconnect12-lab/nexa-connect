import {test,expect} from "@playwright/test";
const product="11111111-1111-1111-1111-111111111111";
const visit={visitorId:"guest",entryId:"entry",restaurantName:"Restaurant",branchName:"Branch",tableCode:"A1",currency:"THB",expiresAtUtc:new Date(Date.now()+3600000).toISOString(),menuAvailable:true,orderingAvailable:false};
const items=[{productId:product,name:"Soup",unitPrice:107,currency:"THB",available:true},{productId:"22222222-2222-2222-2222-222222222222",name:"Unavailable dish",unitPrice:20,currency:"THB",available:false}];
function quote(quantity=1,price=107){const menu=quantity*price;return{fingerprint:`${quantity}-${price}`,currency:"THB",expiresAtUtc:new Date(Date.now()+120000).toISOString(),lines:[{productId:product,name:"Soup",unitPrice:price,quantity,lineTotal:menu}],menuAmount:menu,subtotalAmount:menu/1.07,serviceChargeAmount:menu/1.07*.1,taxAmount:menu/1.07*.077,totalAmount:quantity*price*1.1,taxPercent:7,taxInclusive:true,serviceChargePercent:10};}
async function setup(page,options={}){
 await page.route("**/bff/**",async route=>{
  const path=new URL(route.request().url()).pathname;
  if(path==="/bff/guest/qr/context")return route.fulfill({json:visit});
  if(path==="/bff/guest/qr/menu")return route.fulfill({json:{items:options.empty?[]:items}});
  if(path==="/bff/guest/qr/quote"){
   const body=route.request().postDataJSON();expect(Object.keys(body)).toEqual(["lines"]);expect(Object.keys(body.lines[0]).sort()).toEqual(["productId","quantity"]);
   if(options.failure)return route.fulfill({status:options.failure,json:{}});
   if(options.delay)await new Promise(resolve=>setTimeout(resolve,300));
   return route.fulfill({json:quote(body.lines[0].quantity,options.price??107)});
  }
  return route.fulfill({status:404,json:{}});
 });
 await page.goto("/order");await expect(page.getByRole("heading",{name:"Menu",exact:true})).toBeVisible();
}
test("phone cart uses server totals, invalidates edits and clears on refresh",async({page})=>{
 await page.setViewportSize({width:320,height:568});
 await setup(page);await expect(page.getByRole("button",{name:"Add Unavailable dish",exact:true})).toBeDisabled();
 await page.getByRole("button",{name:"Add Soup",exact:true}).click();await page.getByRole("spinbutton",{name:"Quantity for Soup"}).fill("2");
 await page.getByRole("button",{name:"Review totals",exact:true}).click();const totals=page.getByRole("region",{name:"Server totals"});await expect(totals).toBeVisible();await expect(totals.getByText("THB 235.40",{exact:true})).toBeVisible();
 await page.getByRole("spinbutton",{name:"Quantity for Soup"}).fill("3");await expect(totals).toHaveCount(0);
 await page.getByRole("button",{name:"Review totals",exact:true}).click();await expect(totals).toBeVisible();await expect(page.getByText("Prices or pricing policy changed.",{exact:false})).toHaveCount(0);
 await page.setViewportSize({width:844,height:390});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.getByRole("button",{name:"Remove Soup",exact:true}).click();await expect(page.getByText("Your cart is empty.")).toBeVisible();
 await page.getByRole("button",{name:"Add Soup",exact:true}).click();await page.reload();await expect(page.getByText("Your cart is empty.")).toBeVisible();
 await expect(page.getByText("Order submission is not available yet.",{exact:false})).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 expect(await page.evaluate(()=>Object.keys(localStorage).concat(Object.keys(sessionStorage)))).toEqual([]);
});
test("expired quotes disappear and returning to a page requires a fresh review",async({page})=>{
 await setup(page);
 await page.route("**/bff/guest/qr/quote",route=>route.fulfill({json:{...quote(),expiresAtUtc:new Date(Date.now()+1000).toISOString()}}));
 await page.getByRole("button",{name:"Add Soup",exact:true}).click();await page.getByRole("button",{name:"Review totals",exact:true}).click();
 await expect(page.getByRole("region",{name:"Server totals"})).toBeVisible();await expect(page.getByText("This quote has expired.",{exact:false})).toBeVisible();
 await expect(page.getByRole("region",{name:"Server totals"})).toHaveCount(0);
 await page.evaluate(()=>document.dispatchEvent(new Event("visibilitychange")));await expect(page.getByText("Review totals again after returning to this page.")).toBeVisible();
});
test("changed prices require review and stale async totals cannot overwrite edited cart",async({page})=>{
 await setup(page,{price:214,delay:true});await page.getByRole("button",{name:"Add Soup",exact:true}).click();
 await page.getByRole("button",{name:"Review totals",exact:true}).click();await page.getByRole("spinbutton",{name:"Quantity for Soup"}).fill("2");
 await page.waitForTimeout(400);await expect(page.getByRole("region",{name:"Server totals"})).toHaveCount(0);
 await page.getByRole("button",{name:"Review totals",exact:true}).click();await expect(page.getByText("Prices or pricing policy changed.",{exact:false})).toBeVisible();
 await page.evaluate(()=>window.dispatchEvent(new Event("offline")));await expect(page.getByRole("region",{name:"Server totals"})).toHaveCount(0);await expect(page.getByText("You are offline.",{exact:false})).toBeVisible();
});
for(const status of [401,409,503])test(`quote failure ${status} keeps authority closed`,async({page})=>{
 await setup(page,{failure:status});await page.getByRole("button",{name:"Add Soup",exact:true}).click();await page.getByRole("button",{name:"Review totals",exact:true}).click();
 await expect(page.getByText(status===401?"Your visit has expired or been revoked.":status===409?"An item is no longer available.":"Menu or totals unavailable.",{exact:false})).toBeVisible();
 await expect(page.getByRole("region",{name:"Server totals"})).toHaveCount(0);
 if(status!==401)await expect(page.getByRole("spinbutton",{name:"Quantity for Soup"})).toHaveValue("1");
});
test("empty menu and keyboard controls fit narrow and landscape viewports",async({page})=>{
 await setup(page,{empty:true});await expect(page.getByText("No menu items are available.",{exact:false})).toBeVisible();
 await page.setViewportSize({width:320,height:568});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.setViewportSize({width:844,height:390});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.getByRole("button",{name:"Refresh menu",exact:true}).focus();await page.keyboard.press("Enter");await expect(page.getByText("No menu items are available.",{exact:false})).toBeVisible();
});
