export type MenuCategory={id:string;name:string};
export type MenuPlacement={productId:string;categoryId:string|null};
export type MenuOrganization={categories:MenuCategory[];items:MenuPlacement[]};
export type BranchMenuOrganization=MenuOrganization&{organizationId:string;branchId:string;version:number};
const guid=(s:unknown):s is string=>typeof s==="string"&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s)&&!/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(s);
export function checkedOrganization(value:unknown):MenuOrganization{
 const v=value as MenuOrganization;
 if(!v||!Array.isArray(v.categories)||!Array.isArray(v.items)||v.categories.length>50||v.items.length>200
  ||v.categories.some(c=>!c||!guid(c.id)||typeof c.name!=="string"||!c.name.trim()||c.name.length>80||/[\x00-\x1f\x7f]/.test(c.name))
  ||new Set(v.categories.map(c=>c.id)).size!==v.categories.length||new Set(v.categories.map(c=>c.name.trim().toLocaleLowerCase())).size!==v.categories.length
  ||v.items.some(i=>!i||!guid(i.productId)||i.categoryId!==null&&(!guid(i.categoryId)||!v.categories.some(c=>c.id===i.categoryId)))
  ||new Set(v.items.map(i=>i.productId)).size!==v.items.length)throw new Error("Invalid menu organization");
 return {categories:v.categories,items:v.items};
}
export function categoryFor(product:string,layout:MenuOrganization){return layout.items.find(i=>i.productId===product)?.categoryId??null;}
export function orderedMenu<T extends{productId:string}>(items:readonly T[],layout:MenuOrganization):T[]{
 const rank=(id:string)=>{const cat=categoryFor(id,layout);const index=layout.categories.findIndex(c=>c.id===cat);return index<0?layout.categories.length:index;};
 const position=(id:string)=>{const i=layout.items.findIndex(p=>p.productId===id);return i<0?layout.items.length+items.findIndex(p=>p.productId===id):i;};
 return [...items].sort((a,b)=>rank(a.productId)-rank(b.productId)||position(a.productId)-position(b.productId));
}
