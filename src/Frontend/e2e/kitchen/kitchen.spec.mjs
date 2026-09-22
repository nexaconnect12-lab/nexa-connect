import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",ticket="22222222-2222-2222-2222-222222222222";
async function setup(page,{readOnly=false,mode="success"}={}){
  const state={status:"Queued",version:1,posts:[],failReads:false,queries:[]};
  let tenant={subjectId:"operator",organizationId:"33333333-3333-3333-3333-333333333333",applicationCode:"nexa_connect"};
  const organizations=[{organizationId:tenant.organizationId,organizationCode:"one",organizationName:"Organization One",applicationCode:"nexa_connect"},{organizationId:"44444444-4444-4444-4444-444444444444",organizationCode:"two",organizationName:"Organization Two",applicationCode:"nexa_connect"}];
  const row=()=>({ticketId:ticket,orderId:"test-order",status:state.status,concurrencyVersion:state.version,queuedAtUtc:new Date().toISOString(),lines:[{productId:"meal",name:"Burger",quantity:2,preparationStation:"grill"}]});
  await page.route("**/bff/customer/**",async route=>{
    const url=new URL(route.request().url()),path=url.pathname,method=route.request().method();let json;
    if(path.endsWith("/me"))json={subjectId:"operator"};
    else if(path==="/bff/customer/access")json={subjectId:"operator",organizations};
    else if(path==="/bff/customer/tenant"){if(method==="POST")tenant={...tenant,...route.request().postDataJSON()};json=tenant;}
    else if(path.endsWith("/csrf"))json={requestToken:"kitchen-csrf"};
    else if(path.endsWith("/transitions")){
      const body=route.request().postDataJSON();state.posts.push({body,headers:route.request().headers()});
      state.version++;state.status=mode==="conflict"?"InProgress":body.targetStatus;
      if(mode==="lost")return route.abort("failed");
      if(mode==="failed-refresh"){state.failReads=true;return route.abort("failed");}
      if(mode==="conflict")return route.fulfill({status:409,json:{title:"Changed"}});
      json=row();
    }else if(path.includes("/kitchen/")){
      if(state.failReads||mode==="denied")return route.fulfill({status:503,json:{title:"Unavailable"}});
      if(path.endsWith(`/${ticket}`))json=row();
      else {state.queries.push(url.searchParams);json={items:state.status==="Completed"?[]:[row()],canTransition:!readOnly,nextCursor:url.searchParams.has("cursor")?null:"next-position"};}
    }else return route.fulfill({status:404});
    return route.fulfill({json});
  });
  await page.goto("/#kitchen");await page.getByLabel("Kitchen branch UUID").fill(branch);await page.getByRole("button",{name:"Refresh queue"}).click();
  return state;
}

test("operator completes lifecycle with CSRF and expected versions",async({page})=>{
  const state=await setup(page);
  for(const action of ["Start","Ready","Complete"]){await page.getByRole("button",{name:action,exact:true}).click();await expect(page.getByText(`${action} saved.`,{exact:false})).toBeVisible();}
  await expect(page.getByText("No active tickets on this page.")).toBeVisible();
  expect(state.posts.map(x=>x.body.expectedConcurrencyVersion)).toEqual([1,2,3]);
  expect(state.posts.every(x=>x.headers["x-nexa-csrf"]==="kitchen-csrf")).toBe(true);
});
test("read-only queue cannot mutate",async({page})=>{
  const state=await setup(page,{readOnly:true});await expect(page.getByText("Read-only kitchen access.")).toBeVisible();
  await expect(page.getByRole("button",{name:"Start",exact:true})).toBeDisabled();expect(state.posts).toHaveLength(0);
});
for(const mode of ["conflict","lost"]){
  test(`${mode} refreshes authoritative state without automatic replay`,async({page})=>{
    const state=await setup(page,{mode});await page.getByRole("button",{name:"Start",exact:true}).click();
    await expect(page.getByText("Current ticket status: InProgress.",{exact:false})).toBeVisible();
    await expect(page.getByRole("button",{name:"Ready",exact:true})).toBeEnabled();expect(state.posts).toHaveLength(1);
    await page.reload();await page.getByLabel("Kitchen branch UUID").fill(branch);await page.getByRole("button",{name:"Refresh queue"}).click();
    await expect(page.getByRole("button",{name:"Ready",exact:true})).toBeEnabled();expect(state.posts).toHaveLength(1);
  });
}
test("failed recovery locks actions until a successful explicit refresh",async({page})=>{
  const state=await setup(page,{mode:"failed-refresh"});await page.getByRole("button",{name:"Start",exact:true}).click();
  await expect(page.getByText("Refresh failed.",{exact:false})).toBeVisible();
  await expect(page.getByRole("button",{name:"Start",exact:true})).toBeDisabled();expect(state.posts).toHaveLength(1);
  state.failReads=false;await page.getByRole("button",{name:"Refresh queue"}).click();
  await expect(page.getByRole("button",{name:"Ready",exact:true})).toBeEnabled();expect(state.posts).toHaveLength(1);
});
test("station and page cursors are sent and tenant switch clears queue",async({page})=>{
  const state=await setup(page);await expect(page.getByRole("button",{name:"Start",exact:true})).toBeEnabled();
  await page.getByLabel("Kitchen station").fill("grill");await page.getByRole("button",{name:"Refresh queue"}).click();
  await page.getByRole("button",{name:"Next page"}).click();await expect(page.getByRole("button",{name:"Next page"})).toBeDisabled();
  expect(state.queries.at(-1).get("station")).toBe("grill");expect(state.queries.at(-1).get("cursor")).toBe("next-position");
  await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByLabel("Kitchen branch UUID")).toHaveValue("");await expect(page.getByText("2 × Burger")).toHaveCount(0);
});
