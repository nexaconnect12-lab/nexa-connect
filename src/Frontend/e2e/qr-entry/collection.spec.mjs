import {test,expect} from "@playwright/test";
const id="33333333-3333-3333-3333-333333333333",product="44444444-4444-4444-4444-444444444444";
test("guest collection reference becomes the original safe paid receipt without a payment command",async({page})=>{
 let paid=false;const calls=[];
 const quote={currency:"THB",totalAmount:117.7,lines:[{productId:product,name:"Soup",quantity:1,unitPrice:107,lineTotal:107}]};
 await page.route("**/bff/**",async route=>{
  const path=new URL(route.request().url()).pathname;calls.push({path,method:route.request().method()});
  if(path==="/bff/guest/qr/context")return route.fulfill({json:{restaurantName:"R",branchName:"B",tableCode:"A1",currency:"THB",expiresAtUtc:new Date(Date.now()+3600000).toISOString(),menuAvailable:true,requestsAvailable:true,orderingAvailable:false}});
  if(path==="/bff/guest/qr/requests")return route.fulfill({json:{items:[{id,tableCode:"A1",status:"accepted",version:2,quote,deadlineUtc:new Date(Date.now()+600000).toISOString(),orderId:id,orderStatus:paid?"paid":"kitchen_accepted",collectionReference:`QR-${id.replaceAll("-","")}`,
   receipt:paid?{receiptNumber:"R-ORIGINAL",paidAtUtc:new Date().toISOString(),currency:"THB",tender:"cash",lines:[{name:"Soup",unitPrice:107,quantity:1,total:107}],subtotalAmount:100,serviceChargeAmount:10,taxAmount:7.7,totalAmount:117.7}:null}],hasMore:false}});
  return route.fulfill({status:404,json:{}});
 });
 await page.goto("/order");await expect(page.getByText(`QR-${id.replaceAll("-","")}`,{exact:true})).toBeVisible();
 paid=true;await page.getByRole("button",{name:"Check request status",exact:true}).click();
 await expect(page.getByRole("region",{name:"Paid receipt",exact:true})).toBeVisible();await expect(page.getByText("Paid receipt R-ORIGINAL",{exact:true})).toBeVisible();
 await expect(page.getByText("Payment confirmed. Do not pay again.",{exact:false})).toBeVisible();
 expect(calls.every(c=>c.method==="GET"&&c.path.startsWith("/bff/guest/qr/"))).toBe(true);
});
