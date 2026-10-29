import {test,expect} from "@playwright/test";
import {deflateSync} from "node:zlib";
import {createHash} from "node:crypto";
const org="11111111-1111-1111-1111-111111111111",branch="22222222-2222-2222-2222-222222222222",other="66666666-6666-6666-6666-666666666666",restaurant="33333333-3333-3333-3333-333333333333",asset="44444444-4444-4444-4444-444444444444";
function crc(bytes){let value=0xffffffff;for(const b of bytes){value^=b;for(let n=0;n<8;n++)value=(value>>>1)^((value&1)?0xedb88320:0);}return (value^0xffffffff)>>>0;}
function chunk(type,data){const name=Buffer.from(type),head=Buffer.alloc(4),tail=Buffer.alloc(4);head.writeUInt32BE(data.length);tail.writeUInt32BE(crc(Buffer.concat([name,data])));return Buffer.concat([head,name,data,tail]);}
function fixturePng(){const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(128,0);ihdr.writeUInt32BE(128,4);ihdr[8]=8;ihdr[9]=2;const raw=Buffer.alloc(128*(128*3+1),255);for(let y=0;y<128;y++){raw[y*385]=0;for(let x=32;x<96&&y>=32&&y<96;x++)raw.fill(0,y*385+1+x*3,y*385+1+x*3+3);}return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk("IHDR",ihdr),chunk("IDAT",deflateSync(raw)),chunk("IEND",Buffer.alloc(0))]);}
const png=fixturePng();
async function setup(page,failSave=false){
 const state={selection:{organizationId:org,restaurantId:restaurant,branchId:branch,version:3,assetId:null},writes:[],starts:[],completes:[],calls:[],uploadHeaders:[]};
 await page.route("**/_fixture-qr-upload",async route=>{state.uploadHeaders.push(await route.request().allHeaders());return route.fulfill({status:204});});
 await page.route("**/bff/**",async route=>{
  const path=new URL(route.request().url()).pathname;state.calls.push(path);let json;
  if(path.endsWith("/me"))json={subjectId:"operator"};
  else if(path.endsWith("/access"))json={subjectId:"operator",organizations:[{organizationId:org,organizationName:"One",applicationCode:"nexa_connect"}]};
  else if(path.endsWith("/tenant"))json={subjectId:"operator",organizationId:org,applicationCode:"nexa_connect"};
  else if(path.includes("operational-branches")||path==="/bff/customer/branches")json=[{branchId:branch,restaurantId:restaurant,name:"Branch",code:"B",currency:"THB",timeZone:"Asia/Singapore"},{branchId:other,restaurantId:restaurant,name:"Other",code:"O",currency:"THB",timeZone:"Asia/Singapore"}];
  else if(path.includes("/configuration/branches/"))json={branchId:branch,concurrencyVersion:1,dineInEnabled:true,takeawayEnabled:true,requireTableForDineIn:false,serviceChargePercent:0,taxPercent:0,taxInclusive:false};
  else if(path==="/bff/customer/promptpay-qr/csrf")json={requestToken:"synthetic-csrf"};
  else if(path.endsWith("/promptpay-qr/branches/"+branch+"/uploads")){state.starts.push({body:route.request().postDataJSON(),csrf:route.request().headers()["x-nexa-csrf"]});json={organizationId:org,branchId:branch,assetId:asset,version:1,uploadUrl:"http://127.0.0.1:5199/_fixture-qr-upload",expiresAtUtc:new Date(Date.now()+600000).toISOString()};}
  else if(path.endsWith("/uploads/"+asset+"/complete")){state.completes.push({body:route.request().postDataJSON(),csrf:route.request().headers()["x-nexa-csrf"]});json={organizationId:org,branchId:branch,assetId:asset,version:2,status:"ready"};}
  else if(path.endsWith("/promptpay-qr/branches/"+branch+"/image"))json={selection:state.selection,image:{assetId:asset,png:png.toString("base64"),sha256:createHash("sha256").update(png).digest("hex")}};
  else if(path.endsWith("/promptpay-qr/branches/"+branch)){
   if(route.request().method()==="PUT"){
    const body=route.request().postDataJSON();state.writes.push({body,csrf:route.request().headers()["x-nexa-csrf"]});state.selection={...state.selection,assetId:body.assetId,version:state.selection.version+1};
    if(failSave)return route.fulfill({status:503,json:{title:"Unconfirmed"}});
   }
   json=state.selection;
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto("/#configuration");await page.getByLabel("PromptPay branch",{exact:true}).selectOption(branch);await page.getByRole("button",{name:"Load branch QR settings",exact:true}).click();
 await expect(page.getByLabel("PromptPay PNG file",{exact:true})).toBeVisible();return state;
}
async function upload(page){await page.getByLabel("PromptPay PNG file",{exact:true}).setInputFiles({name:"fixture.png",mimeType:"image/png",buffer:png});await expect(page.getByAltText("PromptPay QR preview — verify branch payee before approval")).toBeVisible();await page.getByRole("button",{name:"Upload PromptPay PNG",exact:true}).click();await expect(page.getByText("Upload completed.",{exact:false})).toBeVisible();}
test("branch PNG upload needs explicit approval, scoped CSRF and version; removal is separate",async({page})=>{
 const state=await setup(page);await page.context().addCookies([{name:"staff-fixture-cookie",value:"private",url:page.url()}]);await expect(page.getByRole("button",{name:"Approve branch PromptPay QR",exact:true})).toBeDisabled();await upload(page);expect(state.uploadHeaders).toHaveLength(1);expect(state.uploadHeaders[0].cookie).toBeUndefined();expect(state.uploadHeaders[0].authorization).toBeUndefined();
 expect(state.starts).toHaveLength(1);expect(state.starts[0].csrf).toBe("synthetic-csrf");expect(Object.keys(state.starts[0].body).sort()).toEqual(["checksumSha256","contentType","fileName","sizeBytes"]);expect(state.starts[0].body.contentType).toBe("image/png");
 expect(state.completes).toEqual([{body:{expectedVersion:1},csrf:"synthetic-csrf"}]);expect(state.writes).toHaveLength(0);
 await page.getByRole("checkbox",{name:"I approve this QR selection and merchant/payee for this branch.",exact:true}).check();await page.getByRole("button",{name:"Approve branch PromptPay QR",exact:true}).click();
 await expect(page.getByText("Branch QR approved.",{exact:false})).toBeVisible();expect(state.writes).toEqual([{body:{expectedVersion:3,assetId:asset,approved:true},csrf:"synthetic-csrf"}]);
 await page.getByRole("button",{name:"Remove branch QR selection",exact:true}).click();expect(state.writes).toHaveLength(1);
 await page.getByRole("checkbox",{name:"I approve this QR selection and merchant/payee for this branch.",exact:true}).check();await page.getByRole("button",{name:"Approve branch PromptPay QR",exact:true}).click();
 await expect(page.getByText("Branch QR removed.",{exact:false})).toBeVisible();expect(state.writes[1].body).toEqual({expectedVersion:4,assetId:null,approved:true});
 expect(state.calls.some(p=>p.includes("workflows/place")||p.includes("manual-settlement"))).toBe(false);
});
test("SVG is rejected and branch changes discard unapproved PNG and preview",async({page})=>{
 const state=await setup(page);await page.getByLabel("PromptPay PNG file",{exact:true}).setInputFiles({name:"bad.svg",mimeType:"image/svg+xml",buffer:Buffer.from("<svg/>")});
 await expect(page.getByText("Choose a PNG no larger than 2 MiB.",{exact:false})).toBeVisible();expect(state.starts).toHaveLength(0);
 await page.getByLabel("PromptPay PNG file",{exact:true}).setInputFiles({name:"fixture.png",mimeType:"image/png",buffer:png});await expect(page.getByAltText("PromptPay QR preview — verify branch payee before approval")).toBeVisible();
 await page.getByLabel("PromptPay branch",{exact:true}).selectOption(other);await expect(page.getByAltText("PromptPay QR preview — verify branch payee before approval")).toHaveCount(0);await expect(page.getByRole("button",{name:"Approve branch PromptPay QR",exact:true})).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
test("unconfirmed approval reloads original committed selection without automatic retry",async({page})=>{
 const state=await setup(page,true);await upload(page);await page.getByRole("checkbox",{name:"I approve this QR selection and merchant/payee for this branch.",exact:true}).check();await page.getByRole("button",{name:"Approve branch PromptPay QR",exact:true}).click();
 await expect(page.getByText("Approval outcome is unconfirmed",{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Approve branch PromptPay QR",exact:true})).toHaveCount(0);expect(state.writes).toHaveLength(1);
 await page.getByRole("button",{name:"Load branch QR settings",exact:true}).click();await expect(page.getByAltText("PromptPay QR preview — verify branch payee before approval")).toBeVisible();await expect(page.getByRole("checkbox",{name:"I approve this QR selection and merchant/payee for this branch.",exact:true})).not.toBeChecked();expect(state.writes).toHaveLength(1);
});
