import {useMenuPanelState,type MenuManagementScope} from "./MenuManagementScope";
import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Input,Select,Space,Table,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {MenuPhoto} from "./MenuPhoto";
import {signInToCustomer} from "./navigation";
import {BranchMenuOrganization,MenuCategory,MenuPlacement,checkedOrganization,orderedMenu,categoryFor} from "./menuOrganization";
type Product={productId:string;name:string;unitPrice:number;currency:string;available:boolean};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function MenuOrganizationPanel({organizationId,embedded}:{organizationId:string;embedded?:MenuManagementScope}){
 const [branch,setBranch]=useState(embedded?.branch??""),[products,setProducts]=useState<Product[]>([]),[categories,setCategories]=useState<MenuCategory[]>([]),[placements,setPlacements]=useState<MenuPlacement[]>([]);
 const [version,setVersion]=useState(0),[loaded,setLoaded]=useState(false),[busy,setBusy]=useState(false),[name,setName]=useState(""),[search,setSearch]=useState(""),[notice,setNotice]=useState<string>();
 const [baseline,setBaseline]=useState("");
 const timer=useRef<number>();
 const pending=useRef<AbortController>();useEffect(()=>()=>{pending.current?.abort();window.clearTimeout(timer.current);},[]);
 const begin=()=>{window.clearTimeout(timer.current);pending.current?.abort();const c=new AbortController();pending.current=c;panelStatus.begin();setBusy(true);setNotice(undefined);if(embedded)timer.current=window.setTimeout(()=>{if(pending.current!==c)return;c.abort();embedded.onInvalidated();setLoaded(false);setBusy(false);window.clearTimeout(timer.current);panelStatus.end();setNotice("Request timed out. Reload menu before editing or retrying.");},25000);return c;};
 const clear=(id:string)=>{pending.current?.abort();setBranch(id);setProducts([]);setCategories([]);setPlacements([]);setLoaded(false);setBusy(false);setName("");setSearch("");setNotice(undefined);};
 const check=(value:BranchMenuOrganization)=>{checkedOrganization(value);if(value.organizationId!==organizationId||value.branchId!==branch||!Number.isSafeInteger(value.version)||value.version<0)throw new Error();return value;};
 const load=async()=>{const c=begin();setLoaded(false);setProducts([]);setCategories([]);setPlacements([]);
  try{const [menu,raw]=await Promise.all([embedded?Promise.resolve(embedded.items):api.request<Product[]>(`/bff/customer/catalog/branches/${branch}/menu-items`,{signal:c.signal,cache:"no-store"}),api.request<BranchMenuOrganization>(`/bff/customer/menu-organization/branches/${branch}`,{signal:c.signal,cache:"no-store"})]);
   if(!c.signal.aborted){const value=check(raw);if(menu.length>200)throw new Error();const ids=new Set(menu.map(p=>p.productId));
    const next=[...value.items.filter(i=>ids.has(i.productId)),...menu.filter(p=>!value.items.some(i=>i.productId===p.productId)).map(p=>({productId:p.productId,categoryId:null}))];setProducts(menu);setCategories(value.categories);setPlacements(next);setBaseline(JSON.stringify({categories:value.categories,items:next}));setVersion(value.version);setLoaded(true);}}
  catch{if(!c.signal.aborted){embedded?.onInvalidated();setNotice("Menu categories unavailable. Check the branch and permissions, then reload.");}}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const add=()=>{const trimmed=name.trim();if(!trimmed||trimmed.length>80||/[\x00-\x1f\x7f]/.test(trimmed)||categories.some(c=>c.name.trim().toLocaleLowerCase()===trimmed.toLocaleLowerCase())||categories.length>=50){setNotice("Use a unique category name of 1–80 characters; at most 50 categories.");return;}
  setCategories(rows=>[...rows,{id:crypto.randomUUID(),name:trimmed}]);setName("");setNotice("Unsaved menu organization changes.");};
 const moveCategory=(id:string,step:number)=>setCategories(rows=>{const i=rows.findIndex(c=>c.id===id),j=i+step;if(i<0||j<0||j>=rows.length)return rows;const next=[...rows];[next[i],next[j]]=[next[j]!,next[i]!];return next;});
 const remove=(id:string)=>{setCategories(rows=>rows.filter(c=>c.id!==id));setPlacements(rows=>rows.map(p=>p.categoryId===id?{...p,categoryId:null}:p));setNotice("Category removed in this draft. Its items return to Uncategorized when you save.");};
 const moveItem=(id:string,step:number)=>setPlacements(rows=>{const index=rows.findIndex(p=>p.productId===id);if(index<0)return rows;const group=rows.map((p,i)=>p.categoryId===rows[index]!.categoryId?i:-1).filter(i=>i>=0);const target=group[group.indexOf(index)+step];if(target===undefined)return rows;const next=[...rows];[next[index],next[target]]=[next[target]!,next[index]!];return next;});
 const save=async()=>{if(!loaded||busy)return;const c=begin();try{checkedOrganization({categories,items:placements});const csrf=await api.request<{requestToken:string}>("/bff/customer/menu-organization/csrf",{signal:c.signal});
   const raw=await api.request<BranchMenuOrganization>(`/bff/customer/menu-organization/branches/${branch}`,{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:version,categories:categories.map(c=>({...c,name:c.name.trim()})),items:placements},signal:c.signal});
   if(!c.signal.aborted){const value=check(raw);setVersion(value.version);setCategories(value.categories);setPlacements(value.items);setBaseline(JSON.stringify({categories:value.categories,items:value.items}));embedded?.onOrganizationSaved(value);setNotice("Menu categories saved. Refresh the POS and QR menus to use the new organization.");}}
  catch{if(!c.signal.aborted){embedded?.onInvalidated();setLoaded(false);setCategories([]);setPlacements([]);setNotice("Menu categories were not saved or the result is uncertain. Reload before editing or retrying.");}}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const layout={categories,items:placements};const ordered=orderedMenu(products,layout);const validNames=categories.every(c=>c.name.trim().length>0&&c.name.length<=80)&&new Set(categories.map(c=>c.name.trim().toLocaleLowerCase())).size===categories.length;
 const panelStatus=useMenuPanelState(embedded,loaded&&(JSON.stringify(layout)!==baseline||!!name),busy);
 useEffect(()=>{if(embedded)void load();},[embedded?.branch,embedded?.product]);
 return <>{!embedded&&<><Typography.Title level={2}>Menu categories</Typography.Title><Typography.Paragraph>Arrange categories and menu items for this branch. Prices, availability and existing orders are unchanged. Items without a category appear under Uncategorized.</Typography.Paragraph>
  <Space wrap><OperationalBranchPicker capability="catalog" label="Menu organization branch" value={branch} onChange={clear}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load menu organization</Button></Space></>}
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  {loaded&&<><Card title="Category display order" style={{marginTop:16}}><Space wrap><Input aria-label="New category name" placeholder="New category name" maxLength={80} disabled={busy} value={name} onChange={e=>setName(e.target.value)}/><Button disabled={busy||!name.trim()||categories.length>=50} onClick={add}>Add category</Button></Space>
   {categories.map((c,index)=><Space key={c.id} wrap style={{display:"flex",marginTop:12}}><Input aria-label={`Category ${index+1} name`} maxLength={80} value={c.name} disabled={busy} onChange={e=>setCategories(rows=>rows.map(row=>row.id===c.id?{...row,name:e.target.value}:row))}/>
    <Button aria-label={`Move category ${c.name} up`} disabled={busy||index===0} onClick={()=>moveCategory(c.id,-1)}>Up</Button><Button aria-label={`Move category ${c.name} down`} disabled={busy||index===categories.length-1} onClick={()=>moveCategory(c.id,1)}>Down</Button><Button danger disabled={busy} onClick={()=>remove(c.id)}>Remove category</Button></Space>)}
   <Typography.Paragraph>Removing a category keeps its menu items. Save explicitly to apply this draft.</Typography.Paragraph></Card>
   {!embedded&&<Input aria-label="Search category menu items" placeholder="Find menu items by name" value={search} onChange={e=>setSearch(e.target.value)} style={{marginTop:16}}/>}
   <Table<Product> rowKey="productId" dataSource={ordered.filter(p=>(!embedded||p.productId===embedded.product)&&p.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()))} pagination={{pageSize:10}} columns={[
    {title:"Image",render:(_,p)=><MenuPhoto src={`/bff/customer/menu-photos/branches/${branch}/products/${p.productId}/image`} name={p.name}/>},
    {title:"Menu item",render:(_,p)=><span>{p.name}{!p.available&&" — unavailable"}</span>},
    {title:"Category",render:(_,p)=><Select aria-label={`Category for ${p.name}`} showSearch optionFilterProp="label" virtual={false} style={{minWidth:180}} value={categoryFor(p.productId,layout)??""} disabled={busy} options={[{value:"",label:"Uncategorized"},...categories.map(c=>({value:c.id,label:c.name||"Unnamed category"}))]} onChange={value=>setPlacements(rows=>rows.map(row=>row.productId===p.productId?{...row,categoryId:value||null}:row))}/>},
    {title:"Item order",render:(_,p)=>{const group=placements.filter(i=>i.categoryId===categoryFor(p.productId,layout));const i=group.findIndex(x=>x.productId===p.productId);return <Space><Button aria-label={`Move ${p.name} up`} disabled={busy||i<=0} onClick={()=>moveItem(p.productId,-1)}>Up</Button><Button aria-label={`Move ${p.name} down`} disabled={busy||i===group.length-1} onClick={()=>moveItem(p.productId,1)}>Down</Button></Space>;}}
   ]}/><Button type="primary" disabled={busy||!validNames} onClick={()=>void save()}>Save menu organization</Button></>}
 </>;
}
