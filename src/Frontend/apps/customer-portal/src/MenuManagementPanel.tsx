import {useCallback,useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Input,Space,Table,Tabs,Tag,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {MenuDetailsPanel} from "./MenuDetailsPanel";
import {MenuPhotoPanel} from "./MenuPhotoPanel";
import {MenuAvailabilityPanel} from "./MenuAvailabilityPanel";
import {MenuOrganizationPanel} from "./MenuOrganizationPanel";
import {MenuPhoto} from "./MenuPhoto";
import {signInToCustomer} from "./navigation";
import {checkedOrganization,orderedMenu,categoryFor,type BranchMenuOrganization} from "./menuOrganization";
import type {ManagedMenuItem,MenuManagementScope,MenuPanelState} from "./MenuManagementScope";
const api=createApiClient({onUnauthorized:signInToCustomer});
type Section="details"|"image"|"availability"|"categories";
export function MenuManagementPanel({organizationId,registerLeaveGuard}:{organizationId:string;registerLeaveGuard:(guard:(()=>boolean)|undefined)=>void}){
 const [branch,setBranch]=useState(""),[items,setItems]=useState<ManagedMenuItem[]>([]),[product,setProduct]=useState(""),[tab,setTab]=useState<Section>("details"),[search,setSearch]=useState("");
 const [layout,setLayout]=useState<BranchMenuOrganization>(),[loading,setLoading]=useState(false),[invalid,setInvalid]=useState(false),[notice,setNotice]=useState<string>(),[imageVersion,setImageVersion]=useState(0);
 const [panelState,setPanelState]=useState<MenuPanelState>({dirty:false,busy:false});const state=useRef(panelState),pending=useRef<AbortController>(),generation=useRef(0);
 const key=`${organizationId}|${branch}|${product}|${tab}|${generation.current}`;const currentKey=useRef(key);currentKey.current=key;
 const mayLeave=useCallback(()=>{
  if(state.current.busy||pending.current)return false;
  return !state.current.dirty||window.confirm("Discard unsaved menu changes?");
 },[]);
 useEffect(()=>{registerLeaveGuard(mayLeave);const unload=(event:BeforeUnloadEvent)=>{if(state.current.dirty||state.current.busy||pending.current){event.preventDefault();event.returnValue="";}};window.addEventListener("beforeunload",unload);
  return()=>{registerLeaveGuard(undefined);pending.current?.abort();pending.current=undefined;window.removeEventListener("beforeunload",unload);};},[registerLeaveGuard,mayLeave]);
 const resetPanel=()=>{generation.current++;currentKey.current="";state.current={dirty:false,busy:false};setPanelState(state.current);};
 const changeBranch=(id:string)=>{if(id===branch||!mayLeave())return;resetPanel();pending.current?.abort();pending.current=undefined;setBranch(id);setItems([]);setProduct("");setLayout(undefined);setLoading(false);setInvalid(false);setNotice(undefined);};
 const choose=(id:string)=>{if(id===product||!mayLeave())return;resetPanel();setProduct(id);setInvalid(false);setNotice(undefined);};
 const changeTab=(value:string)=>{if(value===tab||!mayLeave())return;resetPanel();setTab(value as Section);};
 const load=async()=>{
  if(!branch||!mayLeave())return;resetPanel();setProduct("");setItems([]);setLayout(undefined);setInvalid(false);setNotice(undefined);setLoading(true);
  const c=new AbortController();pending.current=c;const scope=`${organizationId}|${branch}`;const timeout=window.setTimeout(()=>c.abort(),25000);
  try{
   const [menu,organization]=await Promise.all([api.request<{organizationId:string;branchId:string;items:ManagedMenuItem[]}>(`/bff/customer/menu-details/branches/${branch}`,{signal:c.signal}),api.request<BranchMenuOrganization>(`/bff/customer/menu-organization/branches/${branch}`,{signal:c.signal}).catch(()=>undefined)]);
   if(menu.organizationId!==organizationId||menu.branchId!==branch||!Array.isArray(menu.items)||menu.items.length>200||new Set(menu.items.map(i=>i.productId)).size!==menu.items.length||menu.items.some(i=>!i||!/^([a-f0-9]{8}-){1}[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(i.productId)||typeof i.name!=="string"||!i.name.trim()||!Number.isFinite(i.unitPrice)||i.unitPrice<0||typeof i.currency!=="string"||typeof i.preparationStation!=="string"||typeof i.available!=="boolean"||!Number.isSafeInteger(i.version)||i.version<0))throw new Error("Invalid menu scope");
   if(pending.current!==c||scope!==`${organizationId}|${branch}`)return;if(c.signal.aborted)throw new Error("Timed out");
   setItems(menu.items);
   if(organization){checkedOrganization(organization);if(organization.organizationId!==organizationId||organization.branchId!==branch||!Number.isSafeInteger(organization.version)||organization.version<0)throw new Error("Invalid category scope");setLayout(organization);}
   else setNotice("Menu loaded. Category organization is unavailable; use Categories & order to reload it.");
  }catch{if(pending.current===c){setItems([]);setLayout(undefined);setNotice("Menu unavailable or access denied. Reload before editing.");}}
  finally{window.clearTimeout(timeout);if(pending.current===c){pending.current=undefined;setLoading(false);}}
 };
 const report=useCallback((value:MenuPanelState)=>{if(currentKey.current===key){state.current=value;setPanelState(value);}},[key]);
 const scope:MenuManagementScope={organizationId,branch,product,items,onState:report,
  onDetailsSaved:item=>{if(currentKey.current===key)setItems(rows=>rows.map(row=>row.productId===item.productId?item:row));},
  onAvailabilitySaved:(id,available)=>{if(currentKey.current===key)setItems(rows=>rows.map(row=>row.productId===id?{...row,available}:row));},
  onOrganizationSaved:value=>{if(currentKey.current===key)setLayout(value);},
  onImageSaved:()=>{if(currentKey.current===key)setImageVersion(v=>v+1);},
  onInvalidated:()=>{if(currentKey.current===key){setInvalid(true);setNotice("This menu view is no longer current. Reload menu before another change.");}}
 };
 const selected=items.find(i=>i.productId===product),blocked=loading||panelState.busy;
 const ordered=orderedMenu(items,layout??{categories:[],items:[]});
 const content=tab==="details"?<MenuDetailsPanel organizationId={organizationId} embedded={scope}/>:tab==="image"?<MenuPhotoPanel embedded={scope}/>:tab==="availability"?<MenuAvailabilityPanel organizationId={organizationId} embedded={scope}/>:<MenuOrganizationPanel organizationId={organizationId} embedded={scope}/>;
 return <><Typography.Title level={2}>Menu management</Typography.Title><Typography.Paragraph>Select a branch and menu item once. Details, images, availability and organization have separate explicit saves; accepted orders and payments remain unchanged.</Typography.Paragraph>
  <Space wrap><OperationalBranchPicker capability="catalog" label="Menu management branch" value={branch} onChange={changeBranch} disabled={blocked}/><Button disabled={!branch||blocked} onClick={()=>void load()}>Load menu</Button></Space>
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  <Input aria-label="Search managed menu items" placeholder="Find menu items by name" value={search} onChange={e=>setSearch(e.target.value)} style={{marginTop:16}}/>
  <Table<ManagedMenuItem> rowKey="productId" dataSource={ordered.filter(i=>i.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()))} pagination={{pageSize:10}} columns={[
   {title:"Image",render:(_,item)=><MenuPhoto src={`/bff/customer/menu-photos/branches/${branch}/products/${item.productId}/image`} name={item.name} refreshKey={imageVersion}/>},
   {title:"Menu item",render:(_,item)=><Button type={item.productId===product?"primary":"default"} disabled={blocked||invalid} onClick={()=>choose(item.productId)} aria-label={`Manage ${item.name}`}>{item.name}</Button>},
   {title:"Price",render:(_,item)=>`${item.currency} ${item.unitPrice.toFixed(2)}`},
   {title:"Category",render:(_,item)=>layout?.categories.find(c=>c.id===categoryFor(item.productId,layout))?.name??"Uncategorized"},
   {title:"Availability",render:(_,item)=><Tag>{item.available?"Available":"Sold out"}</Tag>}
  ]}/>
  {selected&&<Card title={`Managing ${selected.name}`}><Tabs activeKey={tab} onChange={changeTab} items={[
   {key:"details",label:"Details",disabled:blocked||invalid},{key:"image",label:"Image",disabled:blocked||invalid},
   {key:"availability",label:"Availability",disabled:blocked||invalid},{key:"categories",label:"Categories & order",disabled:blocked||invalid}
  ]}/>{panelState.dirty&&<Tag color="gold">Unsaved changes</Tag>}<div key={key}>{content}</div></Card>}
 </>;
}
