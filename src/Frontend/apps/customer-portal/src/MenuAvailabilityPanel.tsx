import {useMenuPanelState,type MenuManagementScope} from "./MenuManagementScope";
import {useEffect,useRef,useState} from "react";
import {Alert,Button,Input,Popconfirm,Space,Table,Tag,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";

type Item={productId:string;name:string;available:boolean;version:number};
type Menu={organizationId:string;branchId:string;items:Item[]};
const api=createApiClient({onUnauthorized:signInToCustomer});
const uuid=/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
function checkedItem(item:Item){
 if(!item||!uuid.test(item.productId)||typeof item.name!=="string"||!item.name.trim()||typeof item.available!=="boolean"||!Number.isSafeInteger(item.version)||item.version<0)throw new Error("Invalid menu item");
 return item;
}
export function MenuAvailabilityPanel({organizationId,embedded}:{organizationId:string;embedded?:MenuManagementScope}){
 const [branch,setBranch]=useState(embedded?.branch??""),[items,setItems]=useState<Item[]>([]),[loaded,setLoaded]=useState(false),[busy,setBusy]=useState(false),[search,setSearch]=useState(""),[notice,setNotice]=useState<string>();
 const pending=useRef<AbortController>();const scope=useRef("");scope.current=`${organizationId}|${branch}`;
 useEffect(()=>()=>{pending.current?.abort();pending.current=undefined;},[]);
 const clear=(id:string)=>{pending.current?.abort();pending.current=undefined;scope.current=`${organizationId}|${id}`;setBranch(id);setItems([]);setLoaded(false);setBusy(false);setNotice(undefined);setSearch("");};
 const run=async(action:(c:AbortController)=>Promise<void>,expected=scope.current)=>{
  if(expected!==scope.current)return;
  pending.current?.abort();const c=new AbortController();pending.current=c;panelStatus.begin();setBusy(true);setNotice(undefined);
  const timeout=window.setTimeout(()=>c.abort(),25000);
  try{await action(c);if(c.signal.aborted)throw new Error("Timed out");}catch{if(pending.current===c&&scope.current===expected){embedded?.onInvalidated();setItems([]);setLoaded(false);setNotice("Availability was not saved or the result is uncertain. Reload availability before changing or retrying an item.");}}
  finally{window.clearTimeout(timeout);if(pending.current===c&&scope.current===expected){pending.current=undefined;setBusy(false);panelStatus.end();}}
 };
 const load=()=>run(async c=>{
  const menu=await api.request<Menu>(`/bff/customer/menu-availability/branches/${branch}`,{signal:c.signal});
  if(menu.organizationId!==organizationId||menu.branchId!==branch||!Array.isArray(menu.items)||menu.items.length>200)throw new Error("Wrong menu scope");
  const values=menu.items.map(checkedItem);if(new Set(values.map(x=>x.productId)).size!==values.length)throw new Error("Duplicate menu items");
  if(pending.current===c&&!c.signal.aborted){setItems(values);setLoaded(true);setNotice("Availability loaded. Changes affect new order checks; accepted orders and payments are retained.");}
 });
 const save=(item:Item,expected:string)=>run(async c=>{
  const csrf=await api.request<{requestToken:string}>("/bff/customer/menu-availability/csrf",{signal:c.signal});
  if(!csrf.requestToken)throw new Error("Missing CSRF token");
  const result=checkedItem(await api.request<Item>(`/bff/customer/menu-availability/branches/${branch}/products/${item.productId}`,{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{available:!item.available,expectedVersion:item.version},signal:c.signal}));
  if(result.productId!==item.productId||result.available===item.available||result.version!==item.version+1)throw new Error("Changed availability response");
  if(pending.current===c&&!c.signal.aborted){setItems(rows=>rows.map(row=>row.productId===item.productId?result:row));embedded?.onAvailabilitySaved(result.productId,result.available);setNotice("Availability saved. Refresh POS and QR menus. Existing staff-review requests remain for review; accepted orders and payments are unchanged.");}
 },expected);
 const panelStatus=useMenuPanelState(embedded,false,busy);
 useEffect(()=>{if(embedded)void load();},[embedded?.branch,embedded?.product]);
 return <>{!embedded&&<><Typography.Title level={2}>Menu availability</Typography.Title>
  <Typography.Paragraph>Mark individual items available or sold out for this branch. Refresh POS and phone menus after saving. This does not change stock quantities, prices, accepted orders or payments.</Typography.Paragraph>
  <Space wrap><OperationalBranchPicker capability="catalog" label="Availability branch" value={branch} onChange={clear}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load availability</Button></Space></>}
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  {loaded&&<>{!embedded&&<Input aria-label="Search availability menu items" placeholder="Find menu items by name" value={search} onChange={e=>setSearch(e.target.value)} style={{marginTop:16}}/>}
   <Table<Item> rowKey="productId" dataSource={items.filter(x=>(!embedded||x.productId===embedded.product)&&x.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()))} pagination={{pageSize:10}} columns={[
    {title:"Menu item",dataIndex:"name"},
    {title:"Availability",render:(_,item)=><Tag color={item.available?"green":"default"}>{item.available?"Available":"Sold out"}</Tag>},
    {title:"Action",render:(_,item)=><Popconfirm title={`${item.available?"Mark sold out":"Make available"}: ${item.name}?`} description="Applies to this branch. Existing accepted orders remain unchanged." okText="Confirm availability" onConfirm={()=>save(item,`${organizationId}|${branch}`)} disabled={busy}>
     <Button disabled={busy} aria-label={`${item.available?"Mark sold out":"Make available"} ${item.name}`}>{item.available?"Mark sold out":"Make available"}</Button>
    </Popconfirm>}
   ]}/></>}
 </>;
}
