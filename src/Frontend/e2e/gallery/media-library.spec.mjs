import {test,expect} from '@playwright/test';
import {fulfillPhotoLibrary,libraryPhoto,photoPng} from './photo-library-fixture.mjs';
const org='11111111-1111-1111-1111-111111111111',other='22222222-2222-2222-2222-222222222222',branch='33333333-3333-3333-3333-333333333333',product='44444444-4444-4444-4444-444444444444';
async function setup(page,{menu=false,denied=false,empty=false}={}){
 const photos=empty?[]:Array.from({length:30},(_,i)=>libraryPhoto(`00000000-0000-0000-0000-${String(i+1).padStart(12,'0')}`,`Photo ${String(i+1).padStart(2,'0')}.png`));
 const state={writes:[],photos,tenant:{organizationId:org,applicationCode:'nexa_connect'},reads:[]};
 await page.route('**/bff/customer/**',async route=>{
  const req=route.request(),url=new URL(req.url()),path=url.pathname;let json={};
  if(path==='/bff/customer/media-library'){
   state.reads.push(url.search);if(denied)return route.fulfill({status:403,json:{}});
  }
  const activePhotos=state.tenant.organizationId===org?state.photos:[libraryPhoto('99999999-9999-9999-9999-999999999999','Other workspace.png')];
  if(await fulfillPhotoLibrary(route,activePhotos))return;
  if(path.endsWith('/me'))json={subjectId:'manager'};
  else if(path.endsWith('/access'))json={organizations:[{organizationId:org,organizationName:'One',applicationCode:'nexa_connect'},{organizationId:other,organizationName:'Two',applicationCode:'nexa_connect'}]};
  else if(path.endsWith('/tenant')){if(req.method()==='POST')state.tenant=req.postDataJSON();json=state.tenant;}
  else if(path.endsWith('/operational-branches'))json=[{branchId:branch,name:'Main',code:'main',currency:'THB',timeZone:'Asia/Singapore'}];
  else if(path.endsWith('/menu-items'))json=[{productId:product,name:'Rice',unitPrice:80,currency:'THB'}];
  else if(path.endsWith('/csrf'))json={requestToken:'synthetic-csrf'};
  else if(path.endsWith('/image'))return route.fulfill({status:404});
  else if(path.includes('/preview/'))return route.fulfill({contentType:'image/png',body:photoPng});
  else if(path.includes('/menu-photos/branches/')){if(req.method()==='PUT')state.writes.push({path,body:req.postDataJSON()});json={organizationId:org,branchId:branch,productId:product,assetId:state.writes.at(-1)?.body.assetId??null,version:2+state.writes.length};}
  else if(path.startsWith('/bff/customer/media/')&&req.method()==='DELETE'){state.writes.push({path,query:url.search});state.photos=state.photos.filter(p=>!path.endsWith(p.id));}
  else return route.fulfill({status:404,json:{}});
  return route.fulfill({json});
 });
 await page.goto(menu?'/#menu-images':'/#media');
 if(menu){await page.getByLabel('Menu image branch').selectOption(branch);await page.getByRole('button',{name:'Load menu and images'}).click();await page.getByRole('button',{name:'Rice',exact:true}).click();}
 return state;
}
test('library defaults to a responsive thumbnail grid and switches to details without refetching',async({page},info)=>{
 const state=await setup(page);const grid=page.getByLabel('Photo thumbnail grid');await expect(grid.getByRole('button')).toHaveCount(24);const reads=state.reads.length;
 await expect(page.getByRole('heading',{name:'Customer Portal',exact:true})).toHaveCount(0);await expect(page.getByText('Manage your restaurant workspace, daily operations and financial evidence.',{exact:true})).toHaveCount(0);await expect(page.getByRole('heading',{name:'Photo library',exact:true})).toHaveCount(0);await expect(page.getByLabel('Current section')).toHaveText('Media management');await expect(page.getByRole('navigation',{name:'Workspace navigation',exact:true})).toBeVisible();
 await page.getByLabel('Photo library view').getByText('Details',{exact:true}).click();await expect(page.getByRole('columnheader',{name:'Uploaded'})).toBeVisible();await expect(grid).toHaveCount(0);expect(state.reads).toHaveLength(reads);
 await page.getByLabel('Photo library view').getByText('Thumbnails',{exact:true}).click();await page.setViewportSize({width:390,height:844});await expect(grid.getByRole('button')).toHaveCount(24);
 await page.screenshot({path:info.outputPath('photo-library-mobile.png'),fullPage:true});expect(state.writes).toHaveLength(0);
});
test('server pages and literal search never show a truncated complete library',async({page})=>{
 const state=await setup(page);await expect(page.getByLabel('Photo thumbnail grid').getByRole('button')).toHaveCount(24);await page.getByRole('button',{name:'Next photos'}).click();
 await expect(page.getByText('Page 2',{exact:true})).toBeVisible();await expect(page.getByLabel('Photo thumbnail grid').getByRole('button')).toHaveCount(6);await expect(page.getByRole('button',{name:'Next photos'})).toBeDisabled();
 await page.getByLabel('Search photo library').fill('Photo 30');await expect(page.getByLabel('Photo thumbnail grid').getByRole('button')).toHaveCount(1);await expect(page.getByText('Page 1',{exact:true})).toBeVisible();expect(state.reads.some(q=>q.includes('page=2'))).toBe(true);
});
test('menu picker cancel discards tentative selection and confirm still requires preview and approval',async({page})=>{
 const state=await setup(page,{menu:true});await expect(page.locator('input[type="file"]')).toHaveCount(0);await page.getByRole('button',{name:'Choose from photo library',exact:true}).click();let dialog=page.getByRole('dialog',{name:'Choose a photo'});
 await expect(dialog.getByRole('button',{name:'Use selected photo'})).toBeDisabled();await dialog.getByRole('button',{name:'Select photo Photo 01.png',exact:true}).click();await dialog.getByRole('button',{name:'Cancel',exact:true}).click();
 await expect(page.getByAltText('Selected menu image preview')).toHaveCount(0);expect(state.writes).toHaveLength(0);
 await page.getByRole('button',{name:'Choose from photo library',exact:true}).click();dialog=page.getByRole('dialog',{name:'Choose a photo'});await expect(dialog.getByRole('button',{name:'Use selected photo'})).toBeDisabled();
 await dialog.getByRole('button',{name:'Select photo Photo 02.png',exact:true}).click();await dialog.getByRole('button',{name:'Use selected photo'}).click();
 await expect(page.getByAltText('Selected menu image preview')).toBeVisible();await expect(page.getByRole('button',{name:'Save menu image'})).toBeDisabled();expect(state.writes).toHaveLength(0);
 await page.getByRole('checkbox',{name:/I approve this image/}).check();await page.getByRole('button',{name:'Save menu image'}).click();await expect(page.getByText(/Menu image approved/)).toBeVisible();expect(state.writes).toHaveLength(1);
});
test('media management uploads a batch to the shared workspace and continues after a failed file',async({page})=>{
 const state=await setup(page,{empty:true});
 const uploads=new Map(),completed=[];
 await page.route('**/bff/customer/media/uploads',async route=>{
  const body=route.request().postDataJSON();
  expect(body).toMatchObject({ownerService:'media',ownerType:'photo_library',ownerId:org,contentType:'image/png'});
  expect(body.checksumSha256).toMatch(/^[0-9a-f]{64}$/);
  const id=`00000000-0000-0000-0000-${String(uploads.size+1).padStart(12,'0')}`;
  uploads.set(id,body.fileName);
  await route.fulfill({status:201,json:{asset:{id,concurrencyVersion:1},uploadUrl:new URL(`/fixture-upload/${id}`,page.url()).href}});
 });
 await page.route('**/fixture-upload/*',route=>{
  const id=new URL(route.request().url()).pathname.split('/').at(-1);
  return route.fulfill({status:uploads.get(id)==='failed.png'?503:200});
 });
 await page.route('**/bff/customer/media/*/complete',async route=>{
  const id=new URL(route.request().url()).pathname.split('/').at(-2);
  expect(route.request().postDataJSON()).toEqual({expectedVersion:1});
  completed.push(uploads.get(id));
  state.photos.push({...libraryPhoto(id,uploads.get(id)),ownerService:'media',ownerType:'photo_library'});
  await route.fulfill({status:200,json:{id}});
 });
 await page.getByLabel('Choose photos').setInputFiles(['first.png','failed.png','last.png'].map(name=>({name,mimeType:'image/png',buffer:photoPng})));
 await page.getByRole('button',{name:'Upload photos',exact:true}).click();
 await expect(page.getByText(/failed\.png.*Upload unconfirmed/)).toBeVisible();
 await expect(page.getByRole('button',{name:'Preview photo first.png'})).toBeVisible();
 await expect(page.getByRole('button',{name:'Preview photo last.png'})).toBeVisible();
 expect(completed).toEqual(['first.png','last.png']);
 expect(state.writes).toHaveLength(0);
});
test('delete requires confirmation and uses the observed version',async({page})=>{
 const state=await setup(page);await page.getByRole('button',{name:'Preview photo Photo 01.png',exact:true}).click();const dialog=page.getByRole('dialog',{name:'Photo details'});
 await dialog.getByRole('button',{name:'Delete',exact:true}).click();await page.getByRole('button',{name:'Cancel',exact:true}).click();expect(state.writes).toHaveLength(0);
 await dialog.getByRole('button',{name:'Delete',exact:true}).click();await page.getByRole('button',{name:'Delete',exact:true}).last().click();await expect(dialog).toHaveCount(0);expect(state.writes).toHaveLength(1);expect(state.writes[0].query).toBe('?expectedVersion=3');
});
test('workspace changes discard details, view state and old photographs',async({page})=>{
 await setup(page);await page.getByLabel('Photo library view').getByText('Details',{exact:true}).click();await page.getByTitle('One',{exact:false}).click();await page.getByTitle('Two',{exact:false}).click();
 await expect(page.getByLabel('Photo thumbnail grid')).toBeVisible();await expect(page.getByText('Other workspace.png',{exact:true})).toBeVisible();await expect(page.getByText('Photo 01.png',{exact:true})).toHaveCount(0);
});
test('denied library has no images or selection fallback',async({page})=>{
 const state=await setup(page,{denied:true});await expect(page.getByText(/Photo library unavailable or access denied/)).toBeVisible();await expect(page.getByLabel('Photo thumbnail grid')).toHaveCount(0);expect(state.writes).toHaveLength(0);
});
test('empty library is actionable and contains no fake demo media',async({page})=>{
 await setup(page,{empty:true});await expect(page.getByText('No photos in this view')).toBeVisible();await expect(page.getByRole('button',{name:'Next photos'})).toBeDisabled();
});
