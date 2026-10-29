import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Input,Select,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";

type Binding={organizationId:string;branchId:string;productId:string;barcode:string|null;version:number};
type Item={productId:string;name:string};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function ProductBarcodePanel(){
  const [branch,setBranch]=useState(""),[product,setProduct]=useState<string>(),[items,setItems]=useState<Item[]>([]);
  const [binding,setBinding]=useState<Binding>(),[code,setCode]=useState(""),[busy,setBusy]=useState(false),[notice,setNotice]=useState<string>();
  const pending=useRef<AbortController>();
  useEffect(()=>()=>pending.current?.abort(),[]);
  const reset=()=>{pending.current?.abort();setBinding(undefined);setCode("");setNotice(undefined);};
  const loadMenu=async()=>{
    if(!branch||busy)return;reset();setProduct(undefined);setItems([]);const c=new AbortController();pending.current=c;setBusy(true);
    try{const result=await api.request<Item[]>(`/bff/customer/catalog/branches/${branch}/menu-items`,{signal:c.signal,cache:"no-store"});if(!c.signal.aborted)setItems(result);}
    catch{if(!c.signal.aborted)setNotice("Menu unavailable or access denied. Check the branch and permissions.");}
    finally{if(!c.signal.aborted)setBusy(false);}
  };
  const load=async()=>{
    if(!branch||!product||busy)return;reset();const c=new AbortController();pending.current=c;setBusy(true);
    try{const result=await api.request<Binding>(`/bff/customer/barcodes/branches/${branch}/products/${product}`,{signal:c.signal,cache:"no-store"});if(!c.signal.aborted){setBinding(result);setCode(result.barcode??"");}}
    catch{if(!c.signal.aborted)setNotice("Barcode unavailable or access denied. Reload the menu and barcode.");}
    finally{if(!c.signal.aborted)setBusy(false);}
  };
  const save=async()=>{
    if(!binding||!branch||!product||busy)return;
    if(code!==""&&!/^[A-Za-z0-9._-]{1,64}$/.test(code)){setNotice("Use up to 64 letters, digits, dots, underscores or hyphens, without spaces.");return;}
    const c=new AbortController();pending.current=c;setBusy(true);setNotice(undefined);
    try{
      const csrf=await api.request<{requestToken:string}>("/bff/customer/barcodes/csrf",{signal:c.signal});
      const result=await api.request<Binding>(`/bff/customer/barcodes/branches/${branch}/products/${product}`,{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{barcode:code||null,expectedVersion:binding.version},signal:c.signal});
      if(!c.signal.aborted){setBinding(result);setCode(result.barcode??"");setNotice("Barcode saved. POS scans use the current branch menu.");}
    }catch{if(!c.signal.aborted){setBinding(undefined);setNotice("Save was rejected or its outcome is unconfirmed. Check permissions/duplicate codes and reload before saving again. No automatic retry was sent.");}}
    finally{if(!c.signal.aborted)setBusy(false);}
  };
  return <Card title="Product barcodes" style={{marginTop:24}}>
    <Typography.Paragraph>Assign one barcode to a branch product. A code can identify only one product within your organization. The same product can use that code in several branches. Codes preserve case and leading zeroes. Clear the field to remove this branch's binding.</Typography.Paragraph>
    <Space wrap><OperationalBranchPicker capability="catalog" label="Barcode branch" value={branch} disabled={busy} onChange={value=>{reset();setBranch(value);setProduct(undefined);setItems([]);}}/><Button onClick={()=>void loadMenu()} disabled={!branch||busy}>Load products</Button></Space>
    <Space wrap style={{marginTop:12}}><Select aria-label="Barcode product" style={{minWidth:240}} value={product} disabled={busy} options={items.map(i=>({value:i.productId,label:i.name}))} onChange={value=>{reset();setProduct(value);}}/><Button disabled={!product||busy} onClick={()=>void load()}>Load barcode</Button></Space>
    {binding&&<Space wrap style={{marginTop:12}}><Input aria-label="Product barcode" value={code} maxLength={64} disabled={busy} onChange={e=>setCode(e.target.value)} placeholder="Barcode (empty removes binding)"/><Button disabled={busy} onClick={()=>void save()}>Save barcode</Button></Space>}
    {notice&&<Alert role="status" style={{marginTop:12}} message={notice}/>} 
  </Card>;
}
