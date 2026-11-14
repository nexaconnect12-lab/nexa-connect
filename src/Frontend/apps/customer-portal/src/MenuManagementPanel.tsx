import {useCallback,useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Form,Input,Space,Table,Tabs,Tag,Typography} from "antd";
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
 const [branchLabel,setBranchLabel]=useState(""),[loaded,setLoaded]=useState(false);
 const [category,setCategory]=useState(""),[availability,setAvailability]=useState(""),[page,setPage]=useState(1);
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
 const clearFilters=()=>{setSearch("");setCategory("");setAvailability("");setPage(1);};
 const changeBranch=(id:string,label:string)=>{if(id===branch||!mayLeave())return;resetPanel();pending.current?.abort();pending.current=undefined;setBranch(id);setBranchLabel(label);setLoaded(false);clearFilters();setItems([]);setProduct("");setLayout(undefined);setLoading(false);setInvalid(false);setNotice(undefined);};
 const choose=(id:string,section:Section=tab)=>{if((id===product&&section===tab)||!mayLeave())return;resetPanel();setProduct(id);setTab(section);setInvalid(false);setNotice(undefined);};
 const changeTab=(value:string)=>{if(value===tab||!mayLeave())return;resetPanel();setTab(value as Section);};
 const load=async()=>{
  if(!branch||!mayLeave())return;resetPanel();setProduct("");setItems([]);setLayout(undefined);setLoaded(false);setCategory("");setPage(1);setInvalid(false);setNotice(undefined);setLoading(true);
  const c=new AbortController();pending.current=c;const scope=`${organizationId}|${branch}`;const timeout=window.setTimeout(()=>c.abort(),25000);
  try{
   const [menu,organization]=await Promise.all([api.request<{organizationId:string;branchId:string;items:ManagedMenuItem[]}>(`/bff/customer/menu-details/branches/${branch}`,{signal:c.signal}),api.request<BranchMenuOrganization>(`/bff/customer/menu-organization/branches/${branch}`,{signal:c.signal}).catch(()=>undefined)]);
   if(menu.organizationId!==organizationId||menu.branchId!==branch||!Array.isArray(menu.items)||menu.items.length>200||new Set(menu.items.map(i=>i.productId)).size!==menu.items.length||menu.items.some(i=>!i||!/^([a-f0-9]{8}-){1}[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(i.productId)||typeof i.name!=="string"||!i.name.trim()||!Number.isFinite(i.unitPrice)||i.unitPrice<0||typeof i.currency!=="string"||typeof i.preparationStation!=="string"||typeof i.available!=="boolean"||!Number.isSafeInteger(i.version)||i.version<0))throw new Error("Invalid menu scope");
   if(pending.current!==c||scope!==`${organizationId}|${branch}`)return;if(c.signal.aborted)throw new Error("Timed out");
   setItems(menu.items);
   if(organization){checkedOrganization(organization);if(organization.organizationId!==organizationId||organization.branchId!==branch||!Number.isSafeInteger(organization.version)||organization.version<0)throw new Error("Invalid category scope");setLayout(organization);}
   else setNotice("Menu loaded. Category organization is unavailable; use Categories & order to reload it.");
   setLoaded(true);
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
 const categoryNames=new Map(layout?.categories.map(value=>[value.id,value.name]));
 const categoryId=(item:ManagedMenuItem)=>layout?categoryFor(item.productId,layout):null;
 const activeCategory=categoryNames.has(category)||category==="uncategorized"?category:"";
 const matching=ordered.filter(item=>
  item.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())
  &&(!activeCategory||(categoryId(item)??"uncategorized")===activeCategory)
  &&(!availability||item.available===(availability==="available")));
 const content=tab==="details"?<MenuDetailsPanel organizationId={organizationId} embedded={scope}/>:tab==="image"?<MenuPhotoPanel embedded={scope}/>:tab==="availability"?<MenuAvailabilityPanel organizationId={organizationId} embedded={scope}/>:<MenuOrganizationPanel organizationId={organizationId} embedded={scope}/>;
 return <Space direction="vertical" size="middle" style={{width:"100%"}}>
  <Card title="Branch menu">
   <Space wrap><OperationalBranchPicker capability="catalog" label="Menu management branch" value={branch} onChange={changeBranch} disabled={blocked}/><Button type="primary" loading={loading} disabled={!branch||blocked} onClick={()=>void load()}>Load menu</Button></Space>
   {loaded&&<Typography.Paragraph style={{marginTop:12,marginBottom:0}}>Loaded branch: {branchLabel||branch}</Typography.Paragraph>}
  </Card>
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  {loading&&<Typography.Text role="status">Loading menu…</Typography.Text>}
  {loaded&&<Card title="Menu items">
  <Form layout="vertical">
   <Space wrap align="start">
    <Form.Item label="Item name" htmlFor="managed-menu-search"><Input id="managed-menu-search" aria-label="Search managed menu items" allowClear value={search} onChange={event=>{setSearch(event.target.value);setPage(1);}}/></Form.Item>
    <Form.Item label="Category" htmlFor="managed-menu-category" extra={!layout?"Category data unavailable.":undefined}>
     <select id="managed-menu-category" style={{padding:7}} value={activeCategory} disabled={!layout} onChange={event=>{setCategory(event.target.value);setPage(1);}}>
      <option value="">All categories</option>
      <option value="uncategorized">Uncategorized</option>
      {layout?.categories.map(value=><option key={value.id} value={value.id}>{value.name}</option>)}
     </select>
    </Form.Item>
    <Form.Item label="Availability" htmlFor="managed-menu-availability">
     <select id="managed-menu-availability" style={{padding:7}} value={availability} onChange={event=>{setAvailability(event.target.value);setPage(1);}}>
      <option value="">All availability</option>
      <option value="available">Available</option>
      <option value="sold-out">Sold out</option>
     </select>
    </Form.Item>
    <Form.Item label=" "><Button onClick={clearFilters}>Clear filters</Button></Form.Item>
   </Space>
  </Form>
  <Table<ManagedMenuItem> rowKey="productId" scroll={{x:650}} dataSource={matching} pagination={{pageSize:10,current:Math.min(page,Math.max(1,Math.ceil(matching.length/10))),showSizeChanger:false,onChange:setPage}} locale={{emptyText:items.length?"No menu items match these filters.":"No menu items in this branch."}} columns={[
   {title:"Image",render:(_,item)=><MenuPhoto src={`/bff/customer/menu-photos/branches/${branch}/products/${item.productId}/image`} name={item.name} refreshKey={imageVersion}/>},
   {title:"Menu item",render:(_,item)=><Button type={item.productId===product?"primary":"default"} disabled={blocked||invalid} onClick={()=>choose(item.productId)} aria-label={`Manage ${item.name}`}>{item.name}</Button>},
   {title:"Price",render:(_,item)=>`${item.currency} ${item.unitPrice.toFixed(2)}`},
   {title:"Category",render:(_,item)=>layout?(categoryNames.get(categoryId(item)??"")??"Uncategorized"):"Unavailable"},
   {title:"Availability",render:(_,item)=><Tag color={item.available?"green":"volcano"}>{item.available?"Available":"Sold out"}</Tag>},
   {title:"Action",render:(_,item)=><Button disabled={blocked||invalid} aria-label={`Edit availability for ${item.name}`} onClick={()=>choose(item.productId,"availability")}>Edit availability</Button>}
  ]}/>
  </Card>}
  {selected&&<Card title={`Managing ${selected.name}`}><Tabs activeKey={tab} onChange={changeTab} items={[
   {key:"details",label:"Details",disabled:blocked||invalid},{key:"image",label:"Image",disabled:blocked||invalid},
   {key:"availability",label:"Availability",disabled:blocked||invalid},{key:"categories",label:"Categories & order",disabled:blocked||invalid}
  ]}/>{panelState.dirty&&<Tag color="gold">Unsaved changes</Tag>}<div key={key}>{content}</div></Card>}
 </Space>;
}
