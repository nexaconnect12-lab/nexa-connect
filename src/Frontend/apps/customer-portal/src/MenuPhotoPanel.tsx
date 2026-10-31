import {useMenuPanelState,type MenuManagementScope} from "./MenuManagementScope";
import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Checkbox,Input,Select,Space,Table,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";
import {MenuPhoto} from "./MenuPhoto";
type Item={productId:string;name:string;unitPrice:number;currency:string};
type Asset={id:string;originalFileName:string;processingStatus:string;ownerService:string;ownerType:string;concurrencyVersion:number};
type Selection={organizationId:string;branchId:string;productId:string;assetId:string|null;version:number};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function MenuPhotoPanel({embedded}:{embedded?:MenuManagementScope}={}){
 const [branch,setBranch]=useState(embedded?.branch??""),[items,setItems]=useState<Item[]>(embedded?.items??[]),[assets,setAssets]=useState<Asset[]>([]),[product,setProduct]=useState("");
 const [selection,setSelection]=useState<Selection>(),[asset,setAsset]=useState<string|null>(),[file,setFile]=useState<File>();
 const [busy,setBusy]=useState(false),[approved,setApproved]=useState(false),[previewReady,setPreviewReady]=useState(false),[notice,setNotice]=useState<string>(),[imageVersion,setImageVersion]=useState(0);
 const timer=useRef<number>();
 const pending=useRef<AbortController>();
 const fileInput=useRef<HTMLInputElement>(null);
 const [search,setSearch]=useState(""),[imageSearch,setImageSearch]=useState("");
 useEffect(()=>()=>{pending.current?.abort();window.clearTimeout(timer.current);},[]);
 const begin=()=>{window.clearTimeout(timer.current);pending.current?.abort();const c=new AbortController();pending.current=c;panelStatus.begin();setBusy(true);setNotice(undefined);if(embedded)timer.current=window.setTimeout(()=>{if(pending.current!==c)return;c.abort();embedded.onInvalidated();setSelection(undefined);setApproved(false);setBusy(false);window.clearTimeout(timer.current);panelStatus.end();setNotice("Request timed out. Reload menu before editing or retrying.");},25000);return c;};
 const base=(id=product)=>`/bff/customer/menu-photos/branches/${branch}/products/${id}`;
 const ready=assets.filter(a=>a.processingStatus==="ready"&&a.ownerService==="catalog"&&a.ownerType==="product");
 const selectAsset=(value:string|null)=>{if(value!==asset)setPreviewReady(false);setAsset(value);setApproved(false);};
 const clear=(id:string)=>{pending.current?.abort();setBranch(id);setSearch("");setImageSearch("");setItems([]);setAssets([]);setProduct("");setSelection(undefined);setAsset(undefined);setFile(undefined);setApproved(false);setPreviewReady(false);setNotice(undefined);setBusy(false);};
 const load=async()=>{const c=begin();setItems([]);setProduct("");setSelection(undefined);setAsset(undefined);setApproved(false);setFile(undefined);
  try{const [menu,media]=await Promise.all([api.request<Item[]>(`/bff/customer/catalog/branches/${branch}/menu-items`,{signal:c.signal}),api.request<Asset[]>("/bff/customer/media",{signal:c.signal})]);
   if(!c.signal.aborted){setItems(menu);setAssets(media.filter(a=>a.ownerService==="catalog"&&a.ownerType==="product"));}}
  catch{if(!c.signal.aborted)setNotice("Menu or images unavailable. Check the selected workspace, branch and permissions.");}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const choose=async(id:string)=>{const c=begin();setProduct(id);setSelection(undefined);setAsset(undefined);setFile(undefined);setApproved(false);setPreviewReady(false);
  try{const value=await api.request<Selection>(base(id),{signal:c.signal});if(!c.signal.aborted){if(value.productId!==id||value.branchId!==branch||!Number.isSafeInteger(value.version)||value.version<0)throw new Error();setSelection(value);setAsset(value.assetId&&ready.some(a=>a.id===value.assetId)?value.assetId:undefined);}}
  catch{if(!c.signal.aborted)setNotice("Photo settings unavailable. Reload before editing.");}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const refreshImages=async()=>{const c=begin();try{const rows=await api.request<Asset[]>("/bff/customer/media",{signal:c.signal});if(!c.signal.aborted)setAssets(rows.filter(a=>a.ownerService==="catalog"&&a.ownerType==="product"));}
  catch{if(!c.signal.aborted)setNotice("Images could not be refreshed.");}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const upload=async()=>{if(!file||!product||busy)return;const c=begin();
  try{if(!["image/jpeg","image/png","image/webp"].includes(file.type)||file.size<1||file.size>10_485_760)throw new Error();
   const digest=new Uint8Array(await crypto.subtle.digest("SHA-256",await file.arrayBuffer()));
   const hash=Array.from(digest).map(v=>v.toString(16).padStart(2,"0")).join("");
   const session=await api.request<{asset:Asset;uploadUrl:string}>("/bff/customer/media/uploads",{method:"POST",body:{ownerService:"catalog",ownerType:"product",ownerId:product,fileName:file.name,contentType:file.type,sizeBytes:file.size,checksumSha256:hash},signal:c.signal});
   const response=await fetch(session.uploadUrl,{method:"PUT",credentials:"omit",redirect:"error",headers:{"Content-Type":file.type,"x-amz-checksum-sha256":btoa(String.fromCharCode(...digest))},body:file,signal:c.signal});if(!response.ok)throw new Error();
   await api.request(`/bff/customer/media/${session.asset.id}/complete`,{method:"POST",body:{expectedVersion:session.asset.concurrencyVersion},signal:c.signal});
   if(!c.signal.aborted){setFile(undefined);if(fileInput.current)fileInput.current.value="";setNotice("Uploaded for the selected product. Wait for processing, refresh images, then choose and approve the image.");const refreshed=await api.request<Asset[]>("/bff/customer/media",{signal:c.signal});if(!c.signal.aborted)setAssets(refreshed.filter(a=>a.ownerService==="catalog"&&a.ownerType==="product"));}}
  catch{if(!c.signal.aborted)setNotice("Upload failed or is still awaiting safety checks. Use JPEG, PNG or WebP up to 10 MiB; refresh before retrying.");}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const save=async()=>{if(!selection||asset===undefined||!approved||busy||(asset!==null&&!previewReady))return;const c=begin();
  try{const csrf=await api.request<{requestToken:string}>("/bff/customer/menu-photos/csrf",{signal:c.signal});
   const value=await api.request<Selection>(base(),{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{assetId:asset,expectedVersion:selection.version,approved:true},signal:c.signal});
   if(!c.signal.aborted){if((embedded&&value.organizationId!==embedded.organizationId)||value.productId!==product||value.branchId!==branch||!Number.isSafeInteger(value.version)||value.version<0)throw new Error();setSelection(value);setApproved(false);setImageVersion(v=>v+1);embedded?.onImageSaved();setNotice(asset===null?"Menu image removed.":"Menu image approved. Refresh the guest/POS menu to see the photo.");}}
  catch{if(!c.signal.aborted){embedded?.onInvalidated();setSelection(undefined);setApproved(false);setNotice("Photo was not saved or the result is uncertain. Select the product again to reload before saving; do not retry blindly.");}}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const download=async(row:Asset,variant?:string)=>{const c=begin();try{const path=variant?`/bff/customer/media/${row.id}/variants/${variant}/download`:`/bff/customer/media/${row.id}/download`;const value=await api.request<{downloadUrl:string}>(path,{signal:c.signal});if(!c.signal.aborted)location.assign(value.downloadUrl);}catch{if(!c.signal.aborted)setNotice("Download unavailable; processing or access may have changed.");}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const remove=async(row:Asset)=>{const c=begin();try{await api.request(`/bff/customer/media/${row.id}?expectedVersion=${row.concurrencyVersion}`,{method:"DELETE",signal:c.signal});if(!c.signal.aborted){setAssets(rows=>rows.filter(a=>a.id!==row.id));if(asset===row.id)selectAsset(null);setImageVersion(v=>v+1);setNotice("Image deleted. Any assignment using it will show a placeholder until replaced or cleared.");}}
  catch{if(!c.signal.aborted)setNotice("Delete failed or changed. Refresh images before retrying.");}finally{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}}};
 const panelStatus=useMenuPanelState(embedded,!!file||approved||(asset!==undefined&&!!selection&&asset!==selection.assetId),busy);
 useEffect(()=>{if(!embedded)return;const c=begin();setProduct(embedded.product);setPreviewReady(false);
  void Promise.all([api.request<Asset[]>("/bff/customer/media",{signal:c.signal}),api.request<Selection>(base(embedded.product),{signal:c.signal})]).then(([rows,value])=>{
   if(c.signal.aborted)return;if(value.organizationId!==embedded.organizationId||value.productId!==embedded.product||value.branchId!==embedded.branch||!Number.isSafeInteger(value.version)||value.version<0)throw new Error();
   const filtered=rows.filter(a=>a.ownerService==="catalog"&&a.ownerType==="product");setAssets(filtered);setSelection(value);setAsset(value.assetId&&filtered.some(a=>a.id===value.assetId&&a.processingStatus==="ready")?value.assetId:undefined);
  }).catch(()=>{if(!c.signal.aborted){embedded.onInvalidated();setNotice("Photo settings unavailable. Reload menu before editing.");}}).finally(()=>{if(!c.signal.aborted){setBusy(false);window.clearTimeout(timer.current);panelStatus.end();}});
 },[embedded?.branch,embedded?.product]);
 return <>{!embedded&&<><Typography.Title level={2}>Menu images</Typography.Title><Typography.Paragraph>Select a menu item by name, then choose an existing image or upload a new one. No product UUID entry is needed.</Typography.Paragraph>
  <Space wrap><OperationalBranchPicker capability="catalog" label="Menu image branch" value={branch} onChange={clear}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load menu and images</Button></Space></>}<Space><Button disabled={!branch||busy} onClick={()=>void refreshImages()}>Refresh images</Button></Space>
  {notice&&<Alert role="status" message={notice} style={{marginTop:16}}/>}
  {!embedded&&<><Input aria-label="Search menu items" placeholder="Search menu items by name" value={search} onChange={e=>setSearch(e.target.value)} style={{marginTop:16}}/>
  <Table<Item> rowKey="productId" dataSource={items.filter(i=>i.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()))} loading={busy&&!product} pagination={{pageSize:5}} style={{marginTop:16}} columns={[
   {title:"Image",render:(_,item)=><MenuPhoto src={`${base(item.productId)}/image`} name={item.name} refreshKey={imageVersion}/>},
   {title:"Menu item",render:(_,item)=><Button type={product===item.productId?"primary":"default"} disabled={busy} onClick={()=>void choose(item.productId)}>{item.name}</Button>},
   {title:"Price",render:(_,item)=>`${item.currency} ${item.unitPrice.toFixed(2)}`} ]}/></>}
  {product&&<Card title={`Image for ${items.find(i=>i.productId===product)?.name??"selected item"}`}><Space direction="vertical" style={{width:"100%"}}>
   <Select aria-label="Choose menu image" showSearch optionFilterProp="label" allowClear placeholder="Choose a ready image" disabled={busy||!selection} style={{width:"100%"}} value={asset??undefined} options={ready.map(a=>({value:a.id,label:a.originalFileName}))} onChange={value=>selectAsset(value??null)}/>
   {asset&&<MenuPhoto src={`${base()}/preview/${asset}`} name="Selected menu image preview" refreshKey={imageVersion} onReady={setPreviewReady}/>}
   <Button disabled={busy||!selection} onClick={()=>selectAsset(null)}>Remove image assignment</Button>
   <label>Upload a new product image <input ref={fileInput} key={`${branch}:${product}`} type="file" accept="image/jpeg,image/png,image/webp" disabled={busy} onChange={e=>{setFile(e.target.files?.[0]);setApproved(false);}}/></label>
   <Button disabled={!file||busy} onClick={()=>void upload()}>Upload for selected menu item</Button>
   <Checkbox checked={approved} disabled={busy||!selection||asset===undefined||(asset!==null&&!previewReady)} onChange={e=>setApproved(e.target.checked)}>I approve this image selection for public menu display at this branch.</Checkbox>
   <Button type="primary" disabled={busy||!selection||!approved||asset===undefined||(asset!==null&&!previewReady)} onClick={()=>void save()}>Save menu image</Button>
  </Space></Card>}
  <Typography.Title level={3}>Uploaded product images</Typography.Title><Input aria-label="Search uploaded images" placeholder="Search images by filename" value={imageSearch} onChange={e=>setImageSearch(e.target.value)}/><Table<Asset> rowKey="id" dataSource={assets.filter(a=>a.ownerService==="catalog"&&a.ownerType==="product"&&a.originalFileName.toLocaleLowerCase().includes(imageSearch.toLocaleLowerCase()))} pagination={{pageSize:5}} columns={[
   {title:"File",dataIndex:"originalFileName"},{title:"Status",dataIndex:"processingStatus"},
   {title:"Actions",render:(_,row)=><Space wrap><Button disabled={!selection||busy||row.processingStatus!=="ready"} onClick={()=>selectAsset(row.id)}>Use for selected item</Button><Button disabled={row.processingStatus!=="ready"||busy} onClick={()=>void download(row)}>Original</Button><Button disabled={row.processingStatus!=="ready"||busy} onClick={()=>void download(row,"thumbnail")}>Thumbnail</Button><Button disabled={row.processingStatus!=="ready"||busy} onClick={()=>void download(row,"display")}>Display</Button><Button danger disabled={busy} onClick={()=>void remove(row)}>Delete</Button></Space>} ]}/>
 </>;
}
