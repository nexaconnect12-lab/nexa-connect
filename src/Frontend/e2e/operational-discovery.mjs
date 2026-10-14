// Only mock suites use this helper. Live suites exercise Restaurant authorization.
export async function mockOperationalDiscovery(page,ids){
 await page.route('**/bff/customer/operational-branches?*',route=>route.fulfill({json:ids.map((branchId,index)=>({branchId,name:`Branch ${index+1}`,code:`branch-${index+1}`,timeZone:'Asia/Singapore',currency:'THB'}))}));
 await page.route('**/bff/customer/catalog/branches/*/menu-items',route=>route.fulfill({json:[{preparationStation:'grill'},{preparationStation:'bar'}]}));
}
export async function selectTamperedBranch(page,label,id){
 const picker=page.getByLabel(label,{exact:true});
 // Backend security and delayed-response tests may intentionally bypass discovery.
 if(!await picker.locator(`option[value="${id}"]`).count())
  await picker.evaluate((element,value)=>element.add(new Option('Tampered denial fixture',value)),id);
 await picker.selectOption(id);
}
