import {test,expect} from "@playwright/test";
const org="11111111-1111-1111-1111-111111111111",other="22222222-2222-2222-2222-222222222222",branch="33333333-3333-3333-3333-333333333333",a="44444444-4444-4444-4444-444444444444",b="55555555-5555-5555-5555-555555555555";
async function setup(page,{denied=false,conflict=false}={}){
 const state={posts:[],tenant:{organizationId:org,applicationCode:"nexa_connect"}};
 await page.route("**/bff/customer/**",async route=>{
  const path=new URL(route.request().url()).pathname;let json;
  if(path.endsWith("/me"))json={subjectId:"operator"};
  else if(path.endsWith("/access"))json={organizations:[{organizationId:org,organizationName:"One",applicationCode:"nexa_connect"},{organizationId:other,organizationName:"Two",applicationCode:"nexa_connect"}]};
  else if(path.endsWith("/tenant")){if(route.request().method()==="POST")state.tenant=route.request().postDataJSON();json=state.tenant;}
  else if(path.endsWith("/operational-branches"))json=[{branchId:branch,name:"Branch",code:"one",currency:"THB",timeZone:"Asia/Singapore"}];
  else if(path==="/bff/customer/media")json=[{id:a,originalFileName:"Meal A.png",processingStatus:"ready"},{id:b,originalFileName:"Meal B.png",processingStatus:"ready"},{id:other,originalFileName:"Unapproved.png",processingStatus:"quarantined"}];
  else if(path.endsWith("/csrf"))json={requestToken:"synthetic-csrf"};
  else if(path.includes("/screensaver-gallery/branches/")){
   if(denied)return route.fulfill({status:403,json:{}});
   if(route.request().method()==="PUT"){
    state.posts.push({body:route.request().postDataJSON(),headers:route.request().headers()});
    if(conflict)return route.fulfill({status:409,json:{title:"Changed"}});
    json={organizationId:org,branchId:branch,version:8,...state.posts.at(-1).body};
   }else json={organizationId:org,branchId:branch,version:7,staff:[],customer:[],secondsPerSlide:10};
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto("/#media");await page.getByLabel("Gallery branch").selectOption(branch);await page.getByRole("button",{name:"Load gallery"}).click();return state;
}
async function add(page,role,name){const picker=page.getByRole("combobox",{name:`Add ${role} image`,exact:true});await picker.click();const list=await picker.getAttribute("aria-controls");await page.locator(`[id="${list}"]`).getByRole("option",{name,exact:true}).click();}
test("approval requires explicit consent and sends bounded role lists, CSRF and displayed version",async({page},info)=>{
 const state=await setup(page);await add(page,"staff","Meal A.png");await add(page,"customer","Meal B.png");
 await page.getByLabel("staff slide 1 title").fill("Meal A");await page.getByLabel("customer slide 1 caption").fill("Freshly prepared");
 await expect(page.getByRole("button",{name:"Approve gallery"})).toBeDisabled();
 await page.getByRole("checkbox").check();await expect(page.getByRole("button",{name:"Approve gallery"})).toBeEnabled();
 await expect(page.locator(".ant-select-dropdown:visible")).toHaveCount(0);
 await page.screenshot({path:info.outputPath("gallery-approval.png"),fullPage:true});
 await page.getByRole("button",{name:"Approve gallery"}).click();await expect(page.getByText("Gallery approved.",{exact:false})).toBeVisible();
 expect(state.posts).toHaveLength(1);expect(state.posts[0].body.expectedVersion).toBe(7);expect(state.posts[0].body.staff[0].assetId).toBe(a);expect(state.posts[0].body.customer[0].assetId).toBe(b);expect(state.posts[0].headers["x-nexa-csrf"]).toBe("synthetic-csrf");
});
test("only ready assets are offered and edits invalidate approval",async({page})=>{
 await setup(page);await page.getByRole("combobox",{name:"Add staff image",exact:true}).click();await expect(page.getByRole("option",{name:"Unapproved.png"})).toHaveCount(0);
 await page.getByRole("option",{name:"Meal A.png",exact:true}).click();await page.getByRole("checkbox").check();
 await page.getByLabel("staff slide 1 caption").fill("Changed");await expect(page.getByRole("checkbox")).not.toBeChecked();
});
test("removing selections publishes empty lists without implicit media selection",async({page})=>{
 const state=await setup(page);await add(page,"staff","Meal A.png");await page.getByRole("button",{name:"Remove",exact:true}).click();
 await page.getByRole("checkbox").check();await page.getByRole("button",{name:"Approve gallery"}).click();await expect(page.getByText("Gallery approved.",{exact:false})).toBeVisible();expect(state.posts[0].body.staff).toEqual([]);expect(state.posts[0].body.customer).toEqual([]);
});
test("conflicting save clears editable proof and never automatically retries",async({page})=>{
 const state=await setup(page,{conflict:true});await page.getByRole("checkbox").check();await page.getByRole("button",{name:"Approve gallery"}).click();
 await expect(page.getByText("Save outcome is unconfirmed",{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Approve gallery"})).toHaveCount(0);expect(state.posts).toHaveLength(1);
});
test("denied scope does not expose approval controls",async({page})=>{
 const state=await setup(page,{denied:true});await expect(page.getByText("Gallery unavailable or access denied.",{exact:false})).toBeVisible();await expect(page.getByRole("button",{name:"Approve gallery"})).toHaveCount(0);expect(state.posts).toHaveLength(0);
});
test("tenant changes discard unsaved approval state",async({page})=>{
 const state=await setup(page);await add(page,"staff","Meal A.png");await page.getByRole("checkbox").check();
 await page.getByTitle("One",{exact:false}).click();await page.getByTitle("Two",{exact:false}).click();
 await expect(page.getByLabel("Gallery branch")).toHaveValue("");await expect(page.getByRole("button",{name:"Approve gallery"})).toHaveCount(0);expect(state.posts).toHaveLength(0);
});
test("manager ordering is preserved in the approved staff playlist",async({page})=>{
 const state=await setup(page);await add(page,"staff","Meal A.png");await add(page,"staff","Meal B.png");
 await page.getByRole("button",{name:"Move up",exact:true}).nth(1).click();await page.getByRole("checkbox").check();
 await page.getByRole("button",{name:"Approve gallery"}).click();await expect(page.getByText("Gallery approved.",{exact:false})).toBeVisible();expect(state.posts[0].body.staff.map(x=>x.assetId)).toEqual([b,a]);
});
