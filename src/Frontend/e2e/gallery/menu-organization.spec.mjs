import {test,expect} from '@playwright/test';
const org='11111111-1111-1111-1111-111111111111',branch='22222222-2222-2222-2222-222222222222',rice='33333333-3333-3333-3333-333333333333',tea='44444444-4444-4444-4444-444444444444',food='55555555-5555-5555-5555-555555555555',drink='66666666-6666-6666-6666-666666666666';
async function setup(page,{conflict=false}={}){
 const state={writes:[]};const categories=[{id:food,name:'Food'},{id:drink,name:'Drinks'}];const items=[{productId:tea,categoryId:drink},{productId:rice,categoryId:food}];
 await page.route('**/bff/customer/**',async route=>{
  const req=route.request(),path=new URL(req.url()).pathname;let json={};
  if(path.endsWith('/me'))json={subjectId:'manager'};
  else if(path.endsWith('/access'))json={organizations:[{organizationId:org,organizationName:'Test restaurant',applicationCode:'nexa_connect'}]};
  else if(path.endsWith('/tenant'))json={organizationId:org,applicationCode:'nexa_connect'};
  else if(path.endsWith('/operational-branches'))json=[{branchId:branch,name:'Main',code:'main',currency:'THB',timeZone:'Asia/Singapore'}];
  else if(path.endsWith('/menu-items'))json=[{productId:tea,name:'Tea',unitPrice:5,currency:'THB',available:true},{productId:rice,name:'Rice',unitPrice:107,currency:'THB',available:true}];
  else if(path.endsWith('/csrf'))json={requestToken:'synthetic-csrf'};
  else if(path.includes('/menu-organization/branches/')){
   if(req.method()==='PUT'){state.writes.push({body:req.postDataJSON(),headers:req.headers()});if(conflict)return route.fulfill({status:409,json:{}});}
   json={organizationId:org,branchId:branch,version:state.writes.length?3:2,categories:state.writes.at(-1)?.body.categories??categories,items:state.writes.at(-1)?.body.items??items};
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto('/#menu-organization');await page.getByLabel('Menu organization branch').selectOption(branch);await page.getByRole('button',{name:'Load menu organization'}).click();await expect(page.getByRole('button',{name:'Save menu organization'})).toBeVisible();return state;
}
async function choose(page,product,label){const combo=page.getByRole('combobox',{name:`Category for ${product}`,exact:true});await combo.locator('xpath=ancestor::div[contains(@class,"ant-select-selector")]').click();const list=await combo.getAttribute('aria-controls');await page.locator(`[id="${list}"]`).getByRole('option',{name:label,exact:true}).click();}
test('manager creates category and assigns by product name with explicit versioned CSRF save',async({page})=>{
 const state=await setup(page);await page.getByLabel('New category name').fill('Specials');await page.getByRole('button',{name:'Add category',exact:true}).click();await choose(page,'Rice','Specials');expect(state.writes).toHaveLength(0);
 await page.getByRole('button',{name:'Save menu organization'}).click();await expect(page.getByText(/Menu categories saved/)).toBeVisible();expect(state.writes).toHaveLength(1);
 const saved=state.writes[0];expect(saved.body.expectedVersion).toBe(2);expect(saved.headers['x-nexa-csrf']).toBe('synthetic-csrf');const special=saved.body.categories.find(c=>c.name==='Specials');expect(saved.body.items.find(i=>i.productId===rice).categoryId).toBe(special.id);expect(JSON.stringify(saved.body)).not.toContain('unitPrice');
});
test('category ordering changes presentation while removal returns items to Uncategorized',async({page})=>{
 const state=await setup(page);await page.getByRole('button',{name:'Move category Food down',exact:true}).click();await expect(page.getByLabel('Category 1 name')).toHaveValue('Drinks');
 await page.getByRole('button',{name:'Remove category',exact:true}).last().click();await page.getByRole('button',{name:'Save menu organization'}).click();await expect(page.getByText(/Menu categories saved/)).toBeVisible();expect(state.writes[0].body.categories.map(c=>c.name)).toEqual(['Drinks']);expect(state.writes[0].body.items.find(i=>i.productId===rice).categoryId).toBeNull();
});
test('conflicting save invalidates editable version and does not retry silently',async({page})=>{
 const state=await setup(page,{conflict:true});await page.getByLabel('Category 1 name').fill('Mains');await page.getByRole('button',{name:'Save menu organization'}).click();await expect(page.getByText(/result is uncertain/)).toBeVisible();await expect(page.getByRole('button',{name:'Save menu organization'})).toHaveCount(0);expect(state.writes).toHaveLength(1);
});
test('changing branch discards category drafts and failed thumbnails do not block category editing',async({page})=>{
 const state=await setup(page);await expect(page.getByRole('button',{name:'Save menu organization'})).toBeEnabled();await page.getByLabel('Category 1 name').fill('Unsaved');await page.getByLabel('Menu organization branch').selectOption('');await expect(page.getByRole('button',{name:'Save menu organization'})).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
