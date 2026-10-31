import {useMenuPanelState,type MenuManagementScope} from "./MenuManagementScope";
import {useEffect,useRef,useState} from "react";
import {Alert,AutoComplete,Button,Card,Descriptions,Input,InputNumber,Space,Table,Tag,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";
type Item={productId:string;name:string;unitPrice:number;currency:string;preparationStation:string;available:boolean;version:number};
type Menu={organizationId:string;branchId:string;items:Item[]};
const api=createApiClient({onUnauthorized:signInToCustomer});
const uuid=/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
function checked(item:Item){
 if(!item||!uuid.test(item.productId)||typeof item.name!=="string"||!item.name.trim()||typeof item.preparationStation!=="string"||typeof item.currency!=="string"
  ||!Number.isFinite(item.unitPrice)||item.unitPrice<0||typeof item.available!=="boolean"||!Number.isSafeInteger(item.version)||item.version<0)throw new Error("Invalid menu details");return item;
}
const money=(item:Pick<Item,"currency"|"unitPrice">)=>`${item.currency} ${item.unitPrice.toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:4})}`;
export function MenuDetailsPanel({organizationId,embedded}:{organizationId:string;embedded?:MenuManagementScope}){
 const [branch,setBranch]=useState(embedded?.branch??""),[items,setItems]=useState<Item[]>(embedded?.items??[]),[loaded,setLoaded]=useState(false),[busy,setBusy]=useState(false),[search,setSearch]=useState(""),[notice,setNotice]=useState<string>();
 const [selected,setSelected]=useState<Item>(),[name,setName]=useState(""),[price,setPrice]=useState<number|null>(null),[station,setStation]=useState(""),[reviewed,setReviewed]=useState(false);
 const pending=useRef<AbortController>(),scope=useRef("");scope.current=`${organizationId}|${branch}`;
 const alive=useRef(true);useEffect(()=>{alive.current=true;return()=>{alive.current=false;pending.current?.abort();pending.current=undefined;};},[]);
 const reset=()=>{setSelected(undefined);setReviewed(false);};
 const clear=(id:string)=>{pending.current?.abort();pending.current=undefined;scope.current=`${organizationId}|${id}`;setBranch(id);setItems([]);setLoaded(false);setBusy(false);setNotice(undefined);setSearch("");reset();};
 const run=async(action:(c:AbortController)=>Promise<void>,expected=scope.current)=>{
  if(!alive.current||expected!==scope.current)return;pending.current?.abort();const c=new AbortController();pending.current=c;panelStatus.begin();setBusy(true);setNotice(undefined);const timeout=window.setTimeout(()=>c.abort(),25000);
  try{await action(c);if(c.signal.aborted)throw new Error("Timed out");}catch{if(pending.current===c&&scope.current===expected){embedded?.onInvalidated();setItems([]);setLoaded(false);reset();setNotice("Menu changes were not saved or the result is uncertain. Reload menu items before editing or retrying.");}}
  finally{window.clearTimeout(timeout);if(pending.current===c&&scope.current===expected){pending.current=undefined;setBusy(false);panelStatus.end();}}
 };
 const load=()=>run(async c=>{
  reset();const menu=await api.request<Menu>(`/bff/customer/menu-details/branches/${branch}`,{signal:c.signal});
  if(menu.organizationId!==organizationId||menu.branchId!==branch||!Array.isArray(menu.items)||menu.items.length>200)throw new Error("Wrong menu scope");
  const values=menu.items.map(checked);if(new Set(values.map(x=>x.productId)).size!==values.length)throw new Error("Duplicate items");
  if(pending.current===c&&!c.signal.aborted){setItems(values);setLoaded(true);setNotice("Menu items loaded. Select an existing item to edit.");}
 });
 const edit=(item:Item)=>{setSelected(item);setName(item.name);setPrice(item.unitPrice);setStation(item.preparationStation);setReviewed(false);setNotice(undefined);};
 const valid=selected?.currency==="THB"&&name.trim().length>0&&name.trim().length<=120&&!/[\x00-\x1f\x7f]/.test(name+station)&&station.trim().length>0&&station.trim().length<=80&&price!==null&&price>=0&&price<=99999999&&Math.round(price*100)/100===price;
 const changed=!!selected&&(selected.name!==name.trim()||selected.unitPrice!==price||selected.preparationStation!==station.trim());
 const panelStatus=useMenuPanelState(embedded,changed,busy);
 useEffect(()=>{if(embedded){const item=embedded.items.find(i=>i.productId===embedded.product);if(item)edit(item);}},[embedded?.branch,embedded?.product]);
 const save=()=>{if(!selected||!valid||!changed||!reviewed)return;const original=selected;const draft={name:name.trim(),unitPrice:price!,preparationStation:station.trim(),expectedVersion:original.version};void run(async c=>{
  const csrf=await api.request<{requestToken:string}>("/bff/customer/menu-details/csrf",{signal:c.signal});if(!csrf.requestToken)throw new Error("Missing CSRF");
  const result=checked(await api.request<Item>(`/bff/customer/menu-details/branches/${branch}/products/${original.productId}`,{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:draft,signal:c.signal}));
  if(result.productId!==original.productId||result.version!==original.version+1||result.currency!==original.currency||result.name!==draft.name||result.unitPrice!==draft.unitPrice||result.preparationStation!==draft.preparationStation)throw new Error("Changed result");
  if(pending.current===c&&!c.signal.aborted){setItems(rows=>rows.map(row=>row.productId===original.productId?result:row));if(embedded){embedded.onDetailsSaved(result);edit(result);}else reset();setNotice("Menu item saved. Refresh POS and phone menus and review fresh totals before a new order. Accepted orders, receipts and payments remain unchanged.");}
 });};
 return <>{!embedded&&<><Typography.Title level={2}>Menu item editor</Typography.Title><Typography.Paragraph>Edit existing names, prices and preparation stations. Review before confirming. Currency is retained; availability, categories, photos and barcodes are managed separately. Accepted orders remain unchanged.</Typography.Paragraph>
  <Space wrap><OperationalBranchPicker capability="catalog" label="Menu editor branch" value={branch} onChange={clear}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load menu items</Button></Space></>}
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  {!embedded&&loaded&&<><Input aria-label="Search editable menu items" placeholder="Find menu items by name" value={search} onChange={e=>setSearch(e.target.value)} style={{marginTop:16}}/>
   <Table<Item> rowKey="productId" dataSource={items.filter(i=>i.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()))} pagination={{pageSize:10}} columns={[
    {title:"Menu item",dataIndex:"name"},{title:"Price",render:(_,item)=>money(item)},{title:"Preparation station",dataIndex:"preparationStation"},
    {title:"Availability",render:(_,item)=><Tag>{item.available?"Available":"Sold out"}</Tag>},
    {title:"Edit",render:(_,item)=><Button disabled={busy||item.currency!=="THB"} aria-label={`Edit menu item ${item.name}`} onClick={()=>edit(item)}>Edit</Button>}
   ]}/></>}
  {selected&&<Card title={`Edit ${selected.name}`} style={{marginTop:16}}><Space direction="vertical" style={{width:"100%"}}>
   <label>Menu item name<Input aria-label="Menu item name" maxLength={120} disabled={busy} value={name} onChange={e=>{setName(e.target.value);setReviewed(false);}}/></label>
   <label>Unit price ({selected.currency})<InputNumber aria-label="Menu unit price" min={0} max={99999999} precision={2} step={0.01} disabled={busy} value={price} onChange={v=>{setPrice(v);setReviewed(false);}}/></label>
   <label>Preparation station<AutoComplete aria-label="Menu preparation station" maxLength={80} disabled={busy} value={station} options={[...new Set(items.map(i=>i.preparationStation))].map(value=>({value}))} onChange={v=>{setStation(v);setReviewed(false);}} style={{width:"100%"}}/></label>
   <Space wrap><Button disabled={busy||!valid||!changed} onClick={()=>setReviewed(true)}>Review menu changes</Button><Button disabled={busy} onClick={()=>embedded&&selected?edit(selected):reset()}>Cancel editing</Button></Space>
   {reviewed&&<Card title="Review menu changes"><Descriptions column={1} items={[
    {key:"name",label:"Name",children:`${selected.name} → ${name.trim()}`},{key:"price",label:"Price",children:`${money(selected)} → ${money({currency:selected.currency,unitPrice:price!})}`},
    {key:"station",label:"Preparation station",children:`${selected.preparationStation} → ${station.trim()}`}
   ]}/><Typography.Paragraph>Applies to this branch for new order checks. Availability and accepted orders are retained.</Typography.Paragraph><Button type="primary" disabled={busy||!valid||!changed} onClick={save}>Confirm menu changes</Button></Card>}
  </Space></Card>}
 </>;
}
