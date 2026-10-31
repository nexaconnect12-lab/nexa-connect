import {test,expect} from '@playwright/test';
const rice='11111111-1111-1111-1111-111111111111',tea='22222222-2222-2222-2222-222222222222',food='33333333-3333-3333-3333-333333333333',drink='44444444-4444-4444-4444-444444444444';
async function setup(page,{failed=false}={}){
 await page.route('**/bff/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  if(path.endsWith('/context'))return route.fulfill({json:{visitorId:'guest',entryId:'entry',restaurantName:'Restaurant',branchName:'Main',tableCode:'A1',currency:'THB',expiresAtUtc:new Date(Date.now()+3600000).toISOString(),menuAvailable:true,orderingAvailable:false}});
  if(path.endsWith('/menu'))return route.fulfill({json:{items:[{productId:tea,name:'Tea',unitPrice:5,currency:'THB',available:true},{productId:rice,name:'Rice',unitPrice:107,currency:'THB',available:true}]}});
  if(path.endsWith('/menu-organization'))return route.fulfill(failed?{status:503,json:{}}:{json:{categories:[{id:food,name:'Food'},{id:drink,name:'Drinks'}],items:[{productId:tea,categoryId:drink},{productId:rice,categoryId:food}]}});
  return route.fulfill({status:404,json:{}});
 });await page.goto('/order');await expect(page.getByLabel('Menu category',{exact:true})).toBeVisible();
}
test('category and search filters retain the cart and published display order',async({page})=>{
 await setup(page);const add=page.getByRole('button',{name:/^Add (Rice|Tea)$/});await expect(add.first()).toHaveAccessibleName('Add Rice');await page.getByRole('button',{name:'Add Rice',exact:true}).click();
 await page.getByLabel('Menu category',{exact:true}).selectOption(drink);await expect(page.getByRole('button',{name:'Add Rice',exact:true})).toHaveCount(0);await expect(page.getByRole('spinbutton',{name:'Quantity for Rice'})).toHaveValue('1');
 await page.getByLabel('Search QR menu').fill('absent');await expect(page.getByText(/No items match/)).toBeVisible();await expect(page.getByRole('spinbutton',{name:'Quantity for Rice'})).toHaveValue('1');await page.getByLabel('Search QR menu').fill('Tea');await expect(page.getByRole('button',{name:'Add Tea',exact:true})).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
test('category outage falls back to complete text menu without losing ordering actions',async({page})=>{
 await setup(page,{failed:true});await expect(page.getByText(/Categories unavailable/)).toBeVisible();await expect(page.getByRole('button',{name:'Add Rice',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Add Tea',exact:true}).click();await expect(page.getByRole('spinbutton',{name:'Quantity for Tea'})).toHaveValue('1');
});
test('Uncategorized contains products when no organization exists',async({page})=>{
 await setup(page,{failed:true});await page.getByLabel('Menu category',{exact:true}).selectOption('');await expect(page.getByRole('button',{name:'Add Rice',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Add Tea',exact:true})).toBeVisible();
});
