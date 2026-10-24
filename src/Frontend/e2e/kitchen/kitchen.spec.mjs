import {mockOperationalDiscovery} from "../operational-discovery.mjs";
import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",ticket="22222222-2222-2222-2222-222222222222";
async function setup(page,{readOnly=false,mode="success",entry="/#kitchen",load=true}={}){
  const state={status:"Queued",version:1,posts:[],failReads:false,readDenied:false,queries:[]};
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
      if(state.readDenied||mode==="denied")return route.fulfill({status:403,json:{title:"Denied"}});
      if(state.failReads||mode==="denied")return route.fulfill({status:503,json:{title:"Unavailable"}});
      if(path.endsWith(`/${ticket}`))json=row();
      else {state.queries.push(url.searchParams);json={items:state.status==="Completed"?[]:[row()],canTransition:!readOnly,nextCursor:url.searchParams.has("cursor")?null:"next-position"};}
    }else return route.fulfill({status:404});
    return route.fulfill({json});
  });
  await mockOperationalDiscovery(page,[branch]);await page.goto(entry);
  if(load){await page.getByLabel("Kitchen branch").selectOption(branch);await page.getByRole("button",{name:"Refresh queue"}).click();}
  return state;
}

test("operator completes lifecycle with CSRF and expected versions",async({page})=>{
  const state=await setup(page);
  for(const action of ["Start","Ready","Complete"]){await page.getByRole("button",{name:action,exact:true}).click();await expect(page.getByText(`${action} saved.`,{exact:false})).toBeVisible();}
  await expect(page.getByText("No active tickets on this page.")).toBeVisible();
  expect(state.posts.map(x=>x.body.expectedConcurrencyVersion)).toEqual([1,2,3]);
  expect(state.posts.every(x=>x.headers["x-nexa-csrf"]==="kitchen-csrf")).toBe(true);
});

test("direct entry refreshes, completes the same lifecycle and exits through manager navigation",async({page},info)=>{
  const state=await setup(page,{entry:"/kitchen"});
  await expect(page.getByRole("heading",{name:"Kitchen display",exact:true})).toBeVisible();
  await expect(page.getByRole("menu")).toHaveCount(0);
  await expect(page.getByText("2 × Burger")).toBeVisible();
  await page.screenshot({path:info.outputPath("direct-kitchen.png"),fullPage:true});
  for(const action of ["Start","Ready","Complete"]){
    await page.getByRole("button",{name:action,exact:true}).click();
    await expect(page.getByText(`${action} saved.`,{exact:false})).toBeVisible();
  }
  expect(state.posts.map(x=>x.body.expectedConcurrencyVersion)).toEqual([1,2,3]);
  await page.reload();
  await expect(page.getByRole("heading",{name:"Kitchen display",exact:true})).toBeVisible();
  await expect(page.getByText("2 × Burger")).toHaveCount(0);
  await page.getByLabel("Kitchen branch").selectOption(branch);
  await page.getByRole("button",{name:"Refresh queue"}).click();
  await expect(page.getByText("No active tickets on this page.")).toBeVisible();
  await page.getByRole("link",{name:"Return to manager portal"}).click();
  await expect(page).toHaveURL(/\/#kitchen$/);
  await page.getByRole("link",{name:"Open Kitchen display"}).click();
  await expect(page).toHaveURL(/\/kitchen$/);
  expect(state.posts).toHaveLength(3);
});

test("unauthenticated direct link carries the destination through the login handoff",async({page})=>{
  await setup(page,{entry:"/",load:false});
  let authenticated=false;
  await page.route("**/bff/customer/me",route=>route.fulfill({status:authenticated?200:401,json:authenticated?{subjectId:"operator"}:{title:"Sign in"}}));
  await page.route("**/bff/customer/login?*",async route=>{
    const destination=new URL(route.request().url()).searchParams.get("returnUrl");
    expect(destination).toBe("/kitchen?display=wall#queue");
    await route.fulfill({contentType:"text/html",body:'<a href="/kitchen?display=wall#queue">Complete synthetic sign-in</a>'});
  });
  await page.goto("/kitchen?display=wall#queue");
  await expect(page.getByRole("link",{name:"Complete synthetic sign-in"})).toBeVisible();
  authenticated=true;
  await page.getByRole("link",{name:"Complete synthetic sign-in"}).click();
  await expect(page.getByRole("heading",{name:"Kitchen display",exact:true})).toBeVisible();
  await page.reload();
  await expect(page.getByRole("heading",{name:"Kitchen display",exact:true})).toBeVisible();
});

test("direct queue serializes duplicate clicks and filters stations",async({page})=>{
  const state=await setup(page,{entry:"/kitchen/"});
  await expect(page.getByRole("button",{name:"Start",exact:true})).toBeEnabled();
  await page.getByLabel("Kitchen station").selectOption("grill");
  await expect(page.getByText("2 × Burger")).toHaveCount(0);
  await page.getByRole("button",{name:"Refresh queue"}).click();
  const start=page.getByRole("button",{name:"Start",exact:true});
  await expect(start).toBeEnabled();
  await start.evaluate(element=>{element.click();element.click();});
  await expect(page.getByRole("button",{name:"Ready",exact:true})).toBeEnabled();
  expect(state.posts).toHaveLength(1);
  expect(state.queries.at(-1).get("station")).toBe("grill");
});

test("direct denied queue and revoked reads hide tickets and do not mutate",async({page})=>{
  const state=await setup(page,{entry:"/kitchen"});
  await expect(page.getByText("2 × Burger")).toBeVisible();
  state.readDenied=true;
  await page.getByRole("button",{name:"Refresh queue"}).click();
  await expect(page.getByText("Queue unavailable.",{exact:false})).toBeVisible();
  await expect(page.getByText("2 × Burger")).toHaveCount(0);
  await expect(page.getByRole("button",{name:"Start",exact:true})).toHaveCount(0);
  expect(state.posts).toHaveLength(0);
  await page.reload();
  await page.getByLabel("Kitchen branch").selectOption(branch);
  await page.getByRole("button",{name:"Refresh queue"}).click();
  await expect(page.getByText("Queue unavailable.",{exact:false})).toBeVisible();
  expect(state.posts).toHaveLength(0);
});

test("direct entry rejects a tenant outside current access",async({page})=>{
  const state=await setup(page,{entry:"/",load:false});
  await page.route("**/bff/customer/tenant",route=>route.fulfill({json:{organizationId:"unrelated",applicationCode:"nexa_connect"}}));
  await page.goto("/kitchen");
  await expect(page.getByRole("heading",{name:"Choose your workspace"})).toBeVisible();
  await expect(page.getByLabel("Kitchen branch")).toHaveCount(0);
  expect(state.queries).toHaveLength(0);
});

test("direct read-only access and unavailable fullscreen remain usable on a compact screen",async({page},info)=>{
  await page.addInitScript(()=>Object.defineProperty(Element.prototype,"requestFullscreen",{configurable:true,value:undefined}));
  await page.setViewportSize({width:390,height:844});
  const state=await setup(page,{entry:"/kitchen",readOnly:true});
  await expect(page.getByText("Read-only kitchen access.")).toBeVisible();
  await page.getByRole("button",{name:"Enter fullscreen"}).click();
  await expect(page.getByText("Fullscreen is unavailable.",{exact:false})).toBeVisible();
  await expect(page.getByRole("button",{name:"Start",exact:true})).toBeDisabled();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
  await page.screenshot({path:info.outputPath("compact-kitchen.png"),fullPage:true});
  expect(state.posts).toHaveLength(0);
});

test("fullscreen enters and exits without changing queue state",async({page})=>{
  const state=await setup(page,{entry:"/kitchen"});
  await page.getByRole("button",{name:"Enter fullscreen"}).click();
  await expect(page.getByRole("button",{name:"Exit fullscreen"})).toBeVisible();
  expect(await page.evaluate(()=>!!document.fullscreenElement)).toBe(true);
  await page.getByRole("button",{name:"Exit fullscreen"}).click();
  await expect(page.getByRole("button",{name:"Enter fullscreen"})).toBeVisible();
  await expect(page.getByText("2 × Burger")).toBeVisible();
  expect(state.posts).toHaveLength(0);
});

test("workspace switch clears tickets before the protected selection finishes",async({page})=>{
  const state=await setup(page,{entry:"/kitchen"});
  await expect(page.getByText("2 × Burger")).toBeVisible();
  let complete;
  const response=new Promise(resolve=>{complete=resolve;});
  await page.route("**/bff/customer/tenant",async route=>{
    await response;
    await route.fulfill({json:{subjectId:"operator",organizationId:"44444444-4444-4444-4444-444444444444",applicationCode:"nexa_connect"}});
  });
  await page.getByTitle("Organization One",{exact:false}).click();
  await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByText("2 × Burger")).toHaveCount(0);
  await expect(page.getByRole("heading",{name:"Choose your workspace"})).toBeVisible();
  complete();
  await expect(page.getByLabel("Kitchen branch")).toHaveValue("");
  expect(state.posts).toHaveLength(0);
});

test("denied branch discovery exposes no operational controls",async({page})=>{
  const state=await setup(page,{entry:"/",load:false});
  await page.route("**/bff/customer/operational-branches?*",route=>route.fulfill({json:[]}));
  await page.goto("/kitchen");
  await expect(page.getByText("No active branches authorized for this workflow.")).toBeVisible();
  await expect(page.getByRole("button",{name:"Refresh queue"})).toBeDisabled();
  expect(state.queries).toHaveLength(0);
  expect(state.posts).toHaveLength(0);
});

test("non-NexaConnect product cannot open the Kitchen queue",async({page})=>{
  const state=await setup(page,{entry:"/",load:false});
  const organizationId="33333333-3333-3333-3333-333333333333";
  await page.route("**/bff/customer/access",route=>route.fulfill({json:{organizations:[{organizationId,organizationName:"Other product",applicationCode:"other"}]}}));
  await page.route("**/bff/customer/tenant",route=>route.fulfill({json:{organizationId,applicationCode:"other"}}));
  await page.goto("/kitchen");
  await expect(page.getByText("Kitchen queue requires the NexaConnect product workspace.")).toBeVisible();
  await expect(page.getByLabel("Kitchen branch")).toHaveCount(0);
  expect(state.queries).toHaveLength(0);
});

test("fullscreen rejection keeps the normal queue usable",async({page})=>{
  await page.addInitScript(()=>Object.defineProperty(Element.prototype,"requestFullscreen",{configurable:true,value:()=>Promise.reject(new Error("Synthetic fullscreen rejection"))}));
  const state=await setup(page,{entry:"/kitchen"});
  await page.getByRole("button",{name:"Enter fullscreen"}).click();
  await expect(page.getByText("Fullscreen could not be changed.",{exact:false})).toBeVisible();
  await expect(page.getByRole("button",{name:"Start",exact:true})).toBeEnabled();
  expect(state.posts).toHaveLength(0);
});

test("sign-out unmounts the queue while the logout response is pending",async({page})=>{
  const state=await setup(page,{entry:"/kitchen"});
  await expect(page.getByText("2 × Burger")).toBeVisible();
  let observed;
  await page.exposeFunction("observeSyntheticSignOut",value=>{observed=value;});
  await page.evaluate(()=>{
    const observer=new MutationObserver(()=>{
      if(document.body.textContent.includes("Signing out")) {
        window.observeSyntheticSignOut({signingOut:true,ticketsVisible:document.body.textContent.includes("2 × Burger")});
        observer.disconnect();
      }
    });
    observer.observe(document.body,{childList:true,subtree:true});
  });
  let complete;
  const response=new Promise(resolve=>{complete=resolve;});
  await page.route("**/bff/customer/logout",async route=>{
    await response;
    await route.fulfill({contentType:"text/html",body:"Synthetic signed-out destination"});
  });
  await page.getByRole("link",{name:"Sign out",exact:true}).evaluate(element=>element.click());
  // Capture the old document before navigation; Playwright evaluations/locators
  // may wait for that intentionally pending navigation to finish.
  await expect.poll(()=>observed).toEqual({signingOut:true,ticketsVisible:false});
  complete();
  await expect(page.getByText("Synthetic signed-out destination")).toBeVisible();
  expect(state.posts).toHaveLength(0);
});

test("changing the branch clears ticket and station selection",async({page})=>{
  const state=await setup(page,{entry:"/kitchen"});
  await expect(page.getByText("2 × Burger")).toBeVisible();
  const other="55555555-5555-5555-5555-555555555555";
  await mockOperationalDiscovery(page,[branch,other]);
  await page.reload();
  await page.getByLabel("Kitchen branch").selectOption(branch);
  await page.getByLabel("Kitchen station").selectOption("grill");
  await page.getByRole("button",{name:"Refresh queue"}).click();
  await expect(page.getByText("2 × Burger")).toBeVisible();
  await page.getByLabel("Kitchen branch").selectOption(other);
  await expect(page.getByText("2 × Burger")).toHaveCount(0);
  await expect(page.getByLabel("Kitchen station")).toHaveValue("");
  expect(state.posts).toHaveLength(0);
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
    await page.reload();await page.getByLabel("Kitchen branch").selectOption(branch);await page.getByRole("button",{name:"Refresh queue"}).click();
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
  await page.getByLabel("Kitchen station").selectOption("grill");await page.getByRole("button",{name:"Refresh queue"}).click();
  await page.getByRole("button",{name:"Next page"}).click();await expect(page.getByRole("button",{name:"Next page"})).toBeDisabled();
  expect(state.queries.at(-1).get("station")).toBe("grill");expect(state.queries.at(-1).get("cursor")).toBe("next-position");
  await page.getByTitle("Organization One",{exact:false}).click();await page.getByTitle("Organization Two",{exact:false}).click();
  await expect(page.getByLabel("Kitchen branch")).toHaveValue("");await expect(page.getByText("2 × Burger")).toHaveCount(0);
});
