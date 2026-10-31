import {test,expect} from '@playwright/test';
const org='11111111-1111-1111-1111-111111111111',branch='22222222-2222-2222-2222-222222222222',product='33333333-3333-3333-3333-333333333333';
async function setup(page,{conflict=false}={}){
 const state={writes:[],item:{productId:product,name:'Soup',unitPrice:107,currency:'THB',preparationStation:'kitchen',available:false,version:0}};
 await page.route('**/bff/customer/**',async route=>{
  const req=route.request(),path=new URL(req.url()).pathname;let json={};
  if(path.endsWith('/me'))json={subjectId:'manager'};
  else if(path.endsWith('/access'))json={organizations:[{organizationId:org,organizationName:'Test restaurant',applicationCode:'nexa_connect'}]};
  else if(path.endsWith('/tenant'))json={organizationId:org,applicationCode:'nexa_connect'};
  else if(path.endsWith('/operational-branches'))json=[{branchId:branch,name:'Main',code:'main',currency:'THB',timeZone:'Asia/Singapore'}];
  else if(path.endsWith('/csrf'))json={requestToken:'synthetic-csrf'};
  else if(path.includes('/menu-details/branches/')){
   if(req.method()==='PUT'){
    state.writes.push({body:req.postDataJSON(),headers:req.headers()});if(conflict)return route.fulfill({status:409,json:{}});
    const body=req.postDataJSON();state.item={...state.item,name:body.name,unitPrice:body.unitPrice,preparationStation:body.preparationStation,version:state.item.version+1};json=state.item;
   }else json={organizationId:org,branchId:branch,items:[state.item]};
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto('/#menu-details');await page.getByLabel('Menu editor branch').selectOption(branch);await page.getByRole('button',{name:'Load menu items'}).click();
 await page.getByRole('button',{name:'Edit menu item Soup'}).click();return state;
}
async function change(page){await page.getByRole('textbox',{name:'Menu item name',exact:true}).fill('Soup updated');await page.getByRole('spinbutton',{name:'Menu unit price'}).fill('108.25');await page.getByRole('combobox',{name:'Menu preparation station'}).fill('bar');}
test('manager reviews before confirming metadata and price while preserving currency and sold-out status',async({page})=>{
 const state=await setup(page);await change(page);expect(state.writes).toHaveLength(0);
 await page.getByRole('button',{name:'Review menu changes',exact:true}).click();await expect(page.getByText('THB 107.00 → THB 108.25')).toBeVisible();expect(state.writes).toHaveLength(0);
 await page.getByRole('button',{name:'Confirm menu changes'}).click();await expect(page.getByText(/Menu item saved/)).toBeVisible();
 expect(state.writes[0].body).toEqual({name:'Soup updated',unitPrice:108.25,preparationStation:'bar',expectedVersion:0});expect(state.writes[0].headers['x-nexa-csrf']).toBe('synthetic-csrf');expect(state.item.available).toBe(false);expect(state.item.currency).toBe('THB');
});
test('changing a reviewed draft requires another explicit review',async({page})=>{
 const state=await setup(page);await change(page);await page.getByRole('button',{name:'Review menu changes',exact:true}).click();await page.getByRole('textbox',{name:'Menu item name',exact:true}).fill('Another soup');await expect(page.getByRole('button',{name:'Confirm menu changes'})).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
test('stale save invalidates edit and requires reload without silent retry',async({page})=>{
 const state=await setup(page,{conflict:true});await change(page);await page.getByRole('button',{name:'Review menu changes',exact:true}).click();await page.getByRole('button',{name:'Confirm menu changes'}).click();await expect(page.getByText(/result is uncertain/)).toBeVisible();await expect(page.getByRole('button',{name:'Confirm menu changes'})).toHaveCount(0);expect(state.writes).toHaveLength(1);
});
test('branch change discards an unsaved reviewed draft',async({page})=>{
 const state=await setup(page);await change(page);await page.getByRole('button',{name:'Review menu changes',exact:true}).click();await page.getByLabel('Menu editor branch').selectOption('');await expect(page.getByRole('button',{name:'Confirm menu changes'})).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
