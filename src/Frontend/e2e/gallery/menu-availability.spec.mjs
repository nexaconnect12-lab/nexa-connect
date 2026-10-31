import {test,expect} from '@playwright/test';
const org='11111111-1111-1111-1111-111111111111',branch='22222222-2222-2222-2222-222222222222',product='33333333-3333-3333-3333-333333333333';
async function setup(page,{conflict=false}={}){
 const state={writes:[],available:true,version:0};
 await page.route('**/bff/customer/**',async route=>{
  const req=route.request(),path=new URL(req.url()).pathname;let json={};
  if(path.endsWith('/me'))json={subjectId:'manager'};
  else if(path.endsWith('/access'))json={organizations:[{organizationId:org,organizationName:'Test restaurant',applicationCode:'nexa_connect'}]};
  else if(path.endsWith('/tenant'))json={organizationId:org,applicationCode:'nexa_connect'};
  else if(path.endsWith('/operational-branches'))json=[{branchId:branch,name:'Main',code:'main',currency:'THB',timeZone:'Asia/Singapore'}];
  else if(path.endsWith('/csrf'))json={requestToken:'synthetic-csrf'};
  else if(path.includes('/menu-availability/branches/')){
   if(req.method()==='PUT'){
    state.writes.push({body:req.postDataJSON(),headers:req.headers()});
    if(conflict)return route.fulfill({status:409,json:{}});
    state.available=req.postDataJSON().available;state.version++;
    json={productId:product,name:'Rice',available:state.available,version:state.version};
   }else json={organizationId:org,branchId:branch,items:[{productId:product,name:'Rice',available:state.available,version:state.version}]};
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto('/#menu-availability');await page.getByLabel('Availability branch').selectOption(branch);
 await page.getByRole('button',{name:'Load availability'}).click();await expect(page.getByRole('button',{name:'Mark sold out Rice'})).toBeVisible();return state;
}
test('manager marks sold out and restores availability by name with CSRF and original version',async({page})=>{
 const state=await setup(page);await page.getByLabel('Search availability menu items').fill('rice');
 await page.getByRole('button',{name:'Mark sold out Rice'}).click();expect(state.writes).toHaveLength(0);
 await page.getByRole('button',{name:'Confirm availability'}).click();await expect(page.getByText('Availability saved.',{exact:false})).toBeVisible();
 expect(state.writes[0].body).toEqual({available:false,expectedVersion:0});expect(state.writes[0].headers['x-nexa-csrf']).toBe('synthetic-csrf');
 await page.getByRole('button',{name:'Make available Rice'}).click();await page.getByRole('button',{name:'Confirm availability'}).click();
 await expect(page.getByRole('button',{name:'Mark sold out Rice'})).toBeEnabled();expect(state.writes[1].body).toEqual({available:true,expectedVersion:1});
});
test('stale save clears editable rows and requires reload without an automatic retry',async({page})=>{
 const state=await setup(page,{conflict:true});await page.getByRole('button',{name:'Mark sold out Rice'}).click();await page.getByRole('button',{name:'Confirm availability'}).click();
 await expect(page.getByText(/result is uncertain/)).toBeVisible();await expect(page.getByRole('button',{name:'Mark sold out Rice'})).toHaveCount(0);expect(state.writes).toHaveLength(1);
});
test('branch change clears loaded availability without issuing a change',async({page})=>{
 const state=await setup(page);await page.getByLabel('Availability branch').selectOption('');await expect(page.getByRole('button',{name:'Mark sold out Rice'})).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
