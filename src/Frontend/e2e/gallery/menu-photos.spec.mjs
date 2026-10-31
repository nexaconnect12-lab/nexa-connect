import {test,expect} from '@playwright/test';
const org='11111111-1111-1111-1111-111111111111',branch='22222222-2222-2222-2222-222222222222',product='33333333-3333-3333-3333-333333333333',asset='44444444-4444-4444-4444-444444444444';
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=','base64');
async function setup(page,{conflict=false,failedPreview=false}={}){
 const state={writes:[]};
 await page.route('**/bff/customer/**',async route=>{
  const request=route.request(),path=new URL(request.url()).pathname;let json={};
  if(path.endsWith('/me'))json={subjectId:'operator'};
  else if(path.endsWith('/access'))json={organizations:[{organizationId:org,organizationName:'Test restaurant',applicationCode:'nexa_connect'}]};
  else if(path.endsWith('/tenant'))json={organizationId:org,applicationCode:'nexa_connect'};
  else if(path.endsWith('/operational-branches'))json=[{branchId:branch,name:'Main',code:'main',currency:'THB',timeZone:'Asia/Singapore'}];
  else if(path.endsWith('/menu-items'))json=[{productId:product,name:'Rice bowl',unitPrice:107,currency:'THB'}];
  else if(path==='/bff/customer/media')json=[{id:asset,originalFileName:'Rice.png',processingStatus:'ready',ownerService:'catalog',ownerType:'product',concurrencyVersion:3},{id:org,originalFileName:'Bank QR.png',processingStatus:'ready',ownerService:'restaurant',ownerType:'promptpay_qr'}];
  else if(path.endsWith('/csrf'))json={requestToken:'synthetic-csrf'};
  else if(path.endsWith('/image'))return route.fulfill({status:404});
  else if(path.includes('/preview/'))return route.fulfill(failedPreview?{status:503}:{contentType:'image/png',body:png});
  else if(path.includes('/menu-photos/branches/')){
   if(request.method()==='PUT'){state.writes.push({body:request.postDataJSON(),headers:request.headers()});if(conflict)return route.fulfill({status:409,json:{}});}
   json={organizationId:org,branchId:branch,productId:product,assetId:state.writes.at(-1)?.body.assetId??null,version:state.writes.length?3:2};
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto('/#menu-images');await page.getByLabel('Menu image branch').selectOption(branch);await page.getByRole('button',{name:'Load menu and images'}).click();await page.getByRole('button',{name:'Rice bowl',exact:true}).click();
 return state;
}
async function choose(page){await page.getByRole('button',{name:'Use for selected item',exact:true}).click();}
test('product names and existing image list replace UUID typing; preview and approval are required',async({page})=>{
 const state=await setup(page);expect(await page.locator('input[placeholder*="UUID"]').count()).toBe(0);
 await expect(page.getByText('Bank QR.png')).toHaveCount(0);await choose(page);
 await expect(page.getByAltText('Selected menu image preview')).toBeVisible();
 await expect(page.getByRole('checkbox',{name:/I approve this image/})).toBeEnabled();await choose(page);
 await expect(page.getByRole('checkbox',{name:/I approve this image/})).toBeEnabled();
 const save=page.getByRole('button',{name:'Save menu image'});await expect(save).toBeDisabled();
 await page.getByRole('checkbox',{name:/I approve this image/}).check();await save.click();
 await expect(page.getByText('Menu image approved.',{exact:false})).toBeVisible();expect(state.writes).toHaveLength(1);
 expect(state.writes[0].body).toEqual({assetId:asset,expectedVersion:2,approved:true});expect(state.writes[0].headers['x-nexa-csrf']).toBe('synthetic-csrf');
});
test('conflict discards editable version and cannot silently retry',async({page})=>{
 const state=await setup(page,{conflict:true});await choose(page);await page.getByRole('checkbox',{name:/I approve this image/}).check();await page.getByRole('button',{name:'Save menu image'}).click();
 await expect(page.getByText(/result is uncertain/)).toBeVisible();await expect(page.getByRole('button',{name:'Save menu image'})).toBeDisabled();expect(state.writes).toHaveLength(1);
});
test('failed preview blocks public approval and branch change clears product edits',async({page})=>{
 const state=await setup(page,{failedPreview:true});await choose(page);await expect(page.getByRole('checkbox',{name:/I approve this image/})).toBeDisabled();
 await page.getByLabel('Menu image branch').selectOption('');await expect(page.getByRole('button',{name:'Save menu image'})).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
test('clearing an assignment still needs explicit approval',async({page})=>{
 const state=await setup(page);await page.getByRole('button',{name:'Remove image assignment'}).click();await page.getByRole('checkbox',{name:/I approve this image/}).check();await page.getByRole('button',{name:'Save menu image'}).click();
 await expect(page.getByText('Menu image removed.')).toBeVisible();expect(state.writes[0].body).toEqual({assetId:null,expectedVersion:2,approved:true});
});
