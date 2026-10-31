import {test,expect} from '@playwright/test';
const org='11111111-1111-1111-1111-111111111111',branch='22222222-2222-2222-2222-222222222222',product='33333333-3333-3333-3333-333333333333',other='55555555-5555-5555-5555-555555555555',asset='44444444-4444-4444-4444-444444444444';
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=','base64');
async function setup(page,{conflict=false,delay=false,failedPreview=false}={}){
 const state={writes:[],release:()=>{},items:[{productId:product,name:'Soup',unitPrice:107,currency:'THB',preparationStation:'kitchen',available:true,version:3},{productId:other,name:'Rice',unitPrice:20,currency:'THB',preparationStation:'kitchen',available:true,version:2}],layout:{organizationId:org,branchId:branch,categories:[],items:[],version:0}};
 await page.route('**/bff/customer/**',async route=>{
  const req=route.request(),path=new URL(req.url()).pathname;let json={};
  if(path.endsWith('/me'))json={subjectId:'manager'};
  else if(path.endsWith('/access'))json={organizations:[{organizationId:org,organizationName:'Test restaurant',applicationCode:'nexa_connect'},{organizationId:other,organizationName:'Other restaurant',applicationCode:'nexa_connect'}]};
  else if(path.endsWith('/tenant'))json={organizationId:org,applicationCode:'nexa_connect'};
  else if(path.endsWith('/operational-branches'))json=[{branchId:branch,name:'Main',code:'main',currency:'THB',timeZone:'Asia/Singapore'}];
  else if(path.endsWith('/csrf'))json={requestToken:'synthetic-csrf'};
  else if(path.endsWith('/image'))return route.fulfill({status:404});
  else if(path.includes('/preview/'))return route.fulfill(failedPreview?{status:503}:{contentType:'image/png',body:png});
  else if(path==='/bff/customer/media')json=[{id:asset,originalFileName:'Soup.png',processingStatus:'ready',ownerService:'catalog',ownerType:'product',concurrencyVersion:1},{id:org,originalFileName:'Bank QR.png',processingStatus:'ready',ownerService:'restaurant',ownerType:'promptpay_qr'}];
  else if(path.includes('/menu-details/')){
   if(req.method()==='PUT'){
    state.writes.push({path,body:req.postDataJSON()});if(delay)await new Promise(resolve=>state.release=resolve);if(conflict)return route.fulfill({status:409,json:{}});
    const i=state.items.findIndex(i=>path.endsWith(i.productId)),body=req.postDataJSON();state.items[i]={...state.items[i],name:body.name,unitPrice:body.unitPrice,preparationStation:body.preparationStation,version:state.items[i].version+1};json=state.items[i];
   }else json={organizationId:org,branchId:branch,items:state.items};
  }else if(path.includes('/menu-availability/')){
   if(req.method()==='PUT'){state.writes.push({path,body:req.postDataJSON()});state.items[0].available=false;json={...state.items[0],version:10};}
   else json={organizationId:org,branchId:branch,items:state.items.map(i=>({...i,version:9}))};
  }else if(path.includes('/menu-organization/')){
   if(req.method()==='PUT'){state.writes.push({path,body:req.postDataJSON()});state.layout={...state.layout,...req.postDataJSON(),version:state.layout.version+1};}json=state.layout;
  }else if(path.includes('/menu-photos/')){
   if(req.method()==='PUT')state.writes.push({path,body:req.postDataJSON()});json={organizationId:org,branchId:branch,productId:path.endsWith(other)?other:product,assetId:null,version:0};
  }else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto('/#menu-management');await page.getByLabel('Menu management branch').selectOption(branch);await page.getByRole('button',{name:'Load menu',exact:true}).click();await page.getByRole('button',{name:'Manage Soup',exact:true}).click();await expect(page.getByRole('textbox',{name:'Menu item name',exact:true})).toHaveValue('Soup');return state;
}
const nameInput=page=>page.getByRole('textbox',{name:'Menu item name',exact:true});
async function saveDetails(page){await page.getByRole('button',{name:'Review menu changes',exact:true}).click();await page.getByRole('button',{name:'Confirm menu changes'}).click();}
test('one branch and item list serves all editors; search and selection remain shared',async({page})=>{
 await setup(page);await page.getByLabel('Search managed menu items').fill('Soup');await page.getByRole('tab',{name:'Image',exact:true}).click();await expect(page.getByText('Image for Soup',{exact:true})).toBeVisible();await expect(page.getByLabel('Menu image branch')).toHaveCount(0);await page.getByRole('tab',{name:'Availability',exact:true}).click();await expect(page.getByRole('button',{name:'Mark sold out Soup'})).toBeVisible();await expect(page.getByLabel('Search managed menu items')).toHaveValue('Soup');await page.getByRole('tab',{name:'Categories & order'}).click();await expect(page.getByRole('combobox',{name:'Category for Soup'})).toBeVisible();await expect(page.getByRole('combobox',{name:'Category for Rice'})).toHaveCount(0);
});
test('unsaved details guard item, section and branch switching; discard sends no save',async({page})=>{
 const state=await setup(page);await nameInput(page).fill('Draft soup');page.on('dialog',dialog=>dialog.dismiss());await page.getByRole('button',{name:'Manage Rice'}).click();await page.getByRole('tab',{name:'Image',exact:true}).click();await page.getByLabel('Menu management branch').selectOption('');await expect(nameInput(page)).toHaveValue('Draft soup');await expect(page.getByLabel('Menu management branch')).toHaveValue(branch);page.removeAllListeners('dialog');page.once('dialog',dialog=>dialog.accept());await page.getByRole('button',{name:'Manage Rice'}).click();await expect(nameInput(page)).toHaveValue('Rice');expect(state.writes).toHaveLength(0);
});
test('metadata save refreshes shared item and requires only its own explicit review',async({page})=>{
 const state=await setup(page);await nameInput(page).fill('Soup revised');await saveDetails(page);await expect(page.getByRole('button',{name:'Manage Soup revised'})).toBeVisible();expect(state.writes).toHaveLength(1);expect(state.writes[0].body.expectedVersion).toBe(3);await expect(page.getByText('Unsaved changes',{exact:true})).toHaveCount(0);
});
test('availability save never replaces the metadata concurrency version',async({page})=>{
 const state=await setup(page);await page.getByRole('tab',{name:'Availability',exact:true}).click();await page.getByRole('button',{name:'Mark sold out Soup'}).click();await page.getByRole('button',{name:'Confirm availability'}).click();await expect(page.getByText(/Availability saved/)).toBeVisible();await page.getByRole('tab',{name:'Details',exact:true}).click();await nameInput(page).fill('Soup revised');await saveDetails(page);await expect(page.getByRole('button',{name:'Manage Soup revised'})).toBeVisible();expect(state.writes.map(w=>w.body.expectedVersion)).toEqual([9,3]);
});
test('image preview and explicit approval cannot transfer to another item',async({page})=>{
 const state=await setup(page);await page.getByRole('tab',{name:'Image',exact:true}).click();await expect(page.getByText('Bank QR.png')).toHaveCount(0);await page.getByRole('button',{name:'Use for selected item',exact:true}).click();await page.getByRole('checkbox',{name:/I approve/}).check();page.once('dialog',dialog=>dialog.accept());await page.getByRole('button',{name:'Manage Rice'}).click();await expect(page.getByText('Image for Rice',{exact:true})).toBeVisible();await expect(page.getByRole('checkbox',{name:/I approve/})).not.toBeChecked();await expect(page.getByRole('button',{name:'Save menu image'})).toBeDisabled();expect(state.writes).toHaveLength(0);
});
test('failed image preview still blocks approval inside unified workspace',async({page})=>{
 await setup(page,{failedPreview:true});await page.getByRole('tab',{name:'Image',exact:true}).click();await page.getByRole('button',{name:'Use for selected item',exact:true}).click();await expect(page.getByRole('checkbox',{name:/I approve/})).toBeDisabled();
});
test('category drafts guard navigation and save only organization',async({page})=>{
 const state=await setup(page);await page.getByRole('tab',{name:'Categories & order'}).click();await page.getByLabel('New category name').fill('Meals');await page.getByRole('button',{name:'Add category',exact:true}).click();page.once('dialog',dialog=>dialog.dismiss());await page.getByRole('tab',{name:'Details',exact:true}).click();await expect(page.getByLabel('Category 1 name')).toHaveValue('Meals');await page.getByRole('button',{name:'Save menu organization'}).click();await expect(page.getByText(/Menu categories saved/)).toBeVisible();expect(state.writes).toHaveLength(1);expect(state.writes[0].path).toContain('/menu-organization/');await page.getByRole('tab',{name:'Details',exact:true}).click();await expect(nameInput(page)).toHaveValue('Soup');
});
test('conflicted save invalidates the workspace until explicit reload without retry',async({page})=>{
 const state=await setup(page,{conflict:true});await nameInput(page).fill('Soup revised');await saveDetails(page);await expect(page.getByText(/This menu view is no longer current/)).toBeVisible();await expect(page.getByRole('button',{name:'Manage Rice'})).toBeDisabled();await expect(page.getByRole('tab',{name:'Image',exact:true})).toBeDisabled();expect(state.writes).toHaveLength(1);await page.getByRole('button',{name:'Load menu',exact:true}).click();await expect(page.getByRole('button',{name:'Manage Rice'})).toBeEnabled();
});
test('busy save blocks item, branch, section and portal navigation',async({page})=>{
 const state=await setup(page,{delay:true});await nameInput(page).fill('Soup revised');await saveDetails(page);await expect.poll(()=>state.writes.length).toBe(1);await expect(page.getByRole('button',{name:'Manage Rice'})).toBeDisabled();await expect(page.getByLabel('Menu management branch')).toBeDisabled();await expect(page.getByRole('tab',{name:'Image',exact:true})).toBeDisabled();await page.getByRole('menuitem',{name:'Organization profile'}).click();await expect(nameInput(page)).toHaveValue('Soup revised');state.release();await expect(page.getByRole('button',{name:'Manage Soup revised'})).toBeVisible();
});
test('unsaved draft guards portal navigation, sign out and workspace selection',async({page})=>{
 await setup(page);await nameInput(page).fill('Draft soup');page.on('dialog',dialog=>dialog.dismiss());await page.getByRole('menuitem',{name:'Organization profile'}).click();await page.getByRole('link',{name:'Sign out',exact:true}).click();await page.getByTitle('Test restaurant · nexa_connect',{exact:true}).click();await page.getByTitle('Other restaurant · nexa_connect',{exact:true}).click();await expect(nameInput(page)).toHaveValue('Draft soup');await expect(page).toHaveURL(/#menu-management$/);
});

test('direct hash navigation cannot silently discard a draft',async({page})=>{
 await setup(page);await nameInput(page).fill('Draft soup');page.once('dialog',dialog=>dialog.dismiss());await page.evaluate(()=>location.hash='profile');await expect(page).toHaveURL(/#menu-management$/);await expect(nameInput(page)).toHaveValue('Draft soup');
});
test('stalled image read times out and unlocks explicit reload',async({page})=>{
 await setup(page);await page.clock.install();await page.route('**/menu-photos/branches/*/products/*',async route=>{await page.waitForTimeout(30000).catch(()=>{});await route.abort().catch(()=>{});});
 await page.getByRole('tab',{name:'Image',exact:true}).click();await page.clock.fastForward(26000);await expect(page.getByText('Request timed out. Reload menu before editing or retrying.')).toBeVisible();await expect(page.getByRole('button',{name:'Load menu',exact:true})).toBeEnabled();
});
