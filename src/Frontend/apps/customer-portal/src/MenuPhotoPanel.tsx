import {useMenuPanelState,type MenuManagementScope} from "./MenuManagementScope";
import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Checkbox,Input,Space,Table,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";
import {MenuPhoto} from "./MenuPhoto";
import {MediaPhotoPicker} from "./MediaPhotoPicker";
type Item={productId:string;name:string;unitPrice:number;currency:string};
type Selection={organizationId:string;branchId:string;productId:string;assetId:string|null;version:number};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function MenuPhotoPanel({embedded}:{embedded?:MenuManagementScope}={}){
 const [branch,setBranch]=useState(embedded?.branch??""),[items,setItems]=useState<Item[]>(embedded?.items??[]),[product,setProduct]=useState("");
 const [selection,setSelection]=useState<Selection>(),[asset,setAsset]=useState<string|null>();
 const [busy,setBusy]=useState(false),[approved,setApproved]=useState(false),[previewReady,setPreviewReady]=useState(false),[notice,setNotice]=useState<string>(),[imageVersion,setImageVersion]=useState(0);
 const [pickerOpen,setPickerOpen]=useState(false),[search,setSearch]=useState("");
 const timer=useRef<number>(),pending=useRef<AbortController>();
 useEffect(()=>()=>{pending.current?.abort();window.clearTimeout(timer.current);},[]);
 const panelStatus=useMenuPanelState(embedded,approved||(asset!==undefined&&!!selection&&asset!==selection.assetId),busy||pickerOpen);
 const begin=()=>{window.clearTimeout(timer.current);pending.current?.abort();const c=new AbortController();pending.current=c;panelStatus.begin();setBusy(true);setNotice(undefined);if(embedded)timer.current=window.setTimeout(()=>{if(pending.current!==c)return;c.abort();embedded.onInvalidated();setSelection(undefined);setApproved(false);setBusy(false);panelStatus.end();setNotice("Request timed out. Reload menu before editing or retrying.");},25000);return c;};
 const end=(c:AbortController)=>{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}};
 const base=(id=product)=>`/bff/customer/menu-photos/branches/${branch}/products/${id}`;
 const selectAsset=(value:string|null)=>{if(value!==asset)setPreviewReady(false);setAsset(value);setApproved(false);};
 const clear=(id:string)=>{pending.current?.abort();setBranch(id);setSearch("");setItems([]);setProduct("");setSelection(undefined);setAsset(undefined);setApproved(false);setPreviewReady(false);setPickerOpen(false);setNotice(undefined);setBusy(false);};
 const load=async()=>{const c=begin();setItems([]);setProduct("");setSelection(undefined);setAsset(undefined);setApproved(false);setPickerOpen(false);
  try{const menu=await api.request<Item[]>(`/bff/customer/catalog/branches/${branch}/menu-items`,{signal:c.signal});if(!c.signal.aborted)setItems(menu);}
  catch{if(!c.signal.aborted)setNotice("Menu unavailable. Check the selected workspace, branch and permissions.");}finally{end(c);}};
 const applySelection=(value:Selection,id:string)=>{if((embedded&&value.organizationId!==embedded.organizationId)||value.productId!==id||value.branchId!==branch||!Number.isSafeInteger(value.version)||value.version<0)throw new Error();setSelection(value);setAsset(value.assetId);};
 const choose=async(id:string)=>{const c=begin();setProduct(id);setSelection(undefined);setAsset(undefined);setApproved(false);setPreviewReady(false);setPickerOpen(false);
  try{const value=await api.request<Selection>(base(id),{signal:c.signal});if(!c.signal.aborted)applySelection(value,id);}
  catch{if(!c.signal.aborted)setNotice("Photo settings unavailable. Reload before editing.");}finally{end(c);}};
 const save=async()=>{if(!selection||asset===undefined||!approved||busy||(asset!==null&&!previewReady))return;const c=begin();
  try{const csrf=await api.request<{requestToken:string}>("/bff/customer/menu-photos/csrf",{signal:c.signal});
   const value=await api.request<Selection>(base(),{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{assetId:asset,expectedVersion:selection.version,approved:true},signal:c.signal});
   if(!c.signal.aborted){applySelection(value,product);setApproved(false);setImageVersion(v=>v+1);embedded?.onImageSaved();setNotice(asset===null?"Menu image removed.":"Menu image approved. Refresh the guest/POS menu to see the photo.");}}
  catch{if(!c.signal.aborted){embedded?.onInvalidated();setSelection(undefined);setApproved(false);setNotice("Photo was not saved or the result is uncertain. Select the product again to reload before saving; do not retry blindly.");}}finally{end(c);}};
 useEffect(()=>{if(!embedded)return;const c=begin();setProduct(embedded.product);setPreviewReady(false);setApproved(false);setSelection(undefined);setAsset(undefined);setPickerOpen(false);
  void api.request<Selection>(base(embedded.product),{signal:c.signal}).then(value=>{if(!c.signal.aborted)applySelection(value,embedded.product);})
   .catch(()=>{if(!c.signal.aborted){embedded.onInvalidated();setNotice("Photo settings unavailable. Reload menu before editing.");}}).finally(()=>end(c));
 },[embedded?.branch,embedded?.product]);
 return <>{!embedded&&<><Typography.Paragraph>Select a menu item by name, then choose a photo from the shared library.</Typography.Paragraph>
  <Space wrap><OperationalBranchPicker capability="catalog" label="Menu image branch" value={branch} onChange={clear}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load menu and images</Button></Space></>}
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  {!embedded&&<><Input aria-label="Search menu items" placeholder="Search menu items by name" value={search} onChange={e=>setSearch(e.target.value)} style={{marginTop:16}}/>
   <Table<Item> rowKey="productId" dataSource={items.filter(i=>i.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()))} loading={busy&&!product} pagination={{pageSize:5}} style={{marginTop:16}} columns={[
    {title:"Image",render:(_,item)=><MenuPhoto src={`${base(item.productId)}/image`} name={item.name} refreshKey={imageVersion}/>},
    {title:"Menu item",render:(_,item)=><Button type={product===item.productId?"primary":"default"} disabled={busy} onClick={()=>void choose(item.productId)}>{item.name}</Button>},
    {title:"Price",render:(_,item)=>`${item.currency} ${item.unitPrice.toFixed(2)}`} ]}/></>}
  {product&&<Card title={`Image for ${items.find(i=>i.productId===product)?.name??"selected item"}`}><Space direction="vertical" style={{width:"100%"}}>
   <Button disabled={busy||!selection} onClick={()=>setPickerOpen(true)}>Choose from photo library</Button>
   {asset&&<MenuPhoto src={`${base()}/preview/${asset}`} name="Selected menu image preview" refreshKey={imageVersion} onReady={setPreviewReady}/>}
   <Button disabled={busy||!selection} onClick={()=>selectAsset(null)}>Remove image assignment</Button>
   <Checkbox checked={approved} disabled={busy||!selection||asset===undefined||(asset!==null&&!previewReady)} onChange={e=>setApproved(e.target.checked)}>I approve this image selection for public menu display at this branch.</Checkbox>
   <Button type="primary" disabled={busy||!selection||!approved||asset===undefined||(asset!==null&&!previewReady)} onClick={()=>void save()}>Save menu image</Button>
  </Space></Card>}
  <MediaPhotoPicker open={pickerOpen&&!!selection} selectedId={asset??undefined} onCancel={()=>setPickerOpen(false)} onChoose={photo=>{selectAsset(photo.id);setPickerOpen(false);}}/>
 </>;
}
