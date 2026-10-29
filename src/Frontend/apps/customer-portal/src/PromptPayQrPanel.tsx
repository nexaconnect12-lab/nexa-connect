import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Checkbox,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";

type Selection={organizationId:string;restaurantId:string;branchId:string;version:number;assetId:string|null};
type Bundle={selection:Selection;image:{assetId:string;png:string;sha256:string}|null};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function PromptPayQrPanel(){
 const [branch,setBranch]=useState(""),[selection,setSelection]=useState<Selection>(),[draft,setDraft]=useState<string|null>(null);
 const [file,setFile]=useState<File>(),[preview,setPreview]=useState<string>(),[approved,setApproved]=useState(false);
 const [busy,setBusy]=useState(false),[notice,setNotice]=useState<string>();
 const pending=useRef<AbortController>();const previewGeneration=useRef(0);const input=useRef<HTMLInputElement>(null);
 const disposePreview=()=>{previewGeneration.current++;};
 useEffect(()=>()=>{pending.current?.abort();disposePreview();},[]);
 const clear=(value:string)=>{pending.current?.abort();disposePreview();setBranch(value);setSelection(undefined);setDraft(null);setFile(undefined);setPreview(undefined);setApproved(false);setNotice(undefined);setBusy(false);if(input.current)input.current.value="";};
 const load=async()=>{
  const controller=new AbortController();pending.current?.abort();pending.current=controller;setBusy(true);setNotice(undefined);setSelection(undefined);setApproved(false);disposePreview();setPreview(undefined);setFile(undefined);if(input.current)input.current.value="";
  try{
   const value=await api.request<Selection>(`/bff/customer/promptpay-qr/branches/${branch}`,{signal:controller.signal,cache:"no-store"});
   if(value.branchId!==branch)throw new Error();
   let image:Bundle|undefined;
   if(value.assetId){image=await api.request<Bundle>(`/bff/customer/promptpay-qr/branches/${branch}/image`,{signal:controller.signal,cache:"no-store"});if(image.selection.branchId!==branch||image.selection.version!==value.version||image.selection.assetId!==value.assetId)throw new Error();}
   if(!controller.signal.aborted){setSelection(value);setDraft(value.assetId);setPreview(image?.image?`data:image/png;base64,${image.image.png}`:undefined);if(value.assetId&&!image?.image)setNotice("The selected image is unavailable. Replace or remove it; POS will not use it for a new payment.");}
  }catch{if(!controller.signal.aborted)setNotice("PromptPay setup unavailable or access denied. Check the selected branch and permissions, then reload.");}
  finally{if(!controller.signal.aborted)setBusy(false);}
 };
 const choose=async(value:File|undefined)=>{disposePreview();const generation=previewGeneration.current;setFile(undefined);setPreview(undefined);setApproved(false);
  if(!value)return;if(value.type!=="image/png"||!value.name.toLowerCase().endsWith(".png")||value.size<24||value.size>2097152){setNotice("Choose a PNG no larger than 2 MiB. SVG, JPEG and WebP are not accepted for payment QR.");if(input.current)input.current.value="";return;}
  try{const bytes=new Uint8Array(await value.arrayBuffer());const view=new DataView(bytes.buffer);
   if(![137,80,78,71,13,10,26,10].every((b,i)=>bytes[i]===b)||view.getUint32(16)<64||view.getUint32(16)>2048||view.getUint32(20)<64||view.getUint32(20)>2048)throw new Error();
   let binary="";for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));
   if(generation===previewGeneration.current){setFile(value);setPreview(`data:image/png;base64,${btoa(binary)}`);setNotice("Preview only. Upload the PNG before approving it for this branch.");}
  }catch{if(generation===previewGeneration.current){setNotice("Choose a valid PNG, 64–2048 pixels per side. Preserve its full border.");if(input.current)input.current.value="";}}
 };
 const upload=async()=>{
  if(!file||!selection||busy)return;const controller=new AbortController();pending.current=controller;setBusy(true);setNotice(undefined);setApproved(false);
  try{
   const content=await file.arrayBuffer();const view=new DataView(content);
   if(![137,80,78,71,13,10,26,10].every((b,i)=>view.getUint8(i)===b)||view.getUint32(16)<64||view.getUint32(16)>2048||view.getUint32(20)<64||view.getUint32(20)>2048)throw new Error();
   const digest=new Uint8Array(await crypto.subtle.digest("SHA-256",content));const hash=Array.from(digest).map(v=>v.toString(16).padStart(2,"0")).join("");
   const csrf=await api.request<{requestToken:string}>("/bff/customer/promptpay-qr/csrf",{signal:controller.signal});
   const result=await api.request<{assetId:string;version:number;uploadUrl:string}>(`/bff/customer/promptpay-qr/branches/${branch}/uploads`,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{fileName:file.name,contentType:"image/png",sizeBytes:file.size,checksumSha256:hash},signal:controller.signal});
   const put=await fetch(result.uploadUrl,{method:"PUT",credentials:"omit",redirect:"error",headers:{"Content-Type":"image/png","x-amz-checksum-sha256":btoa(String.fromCharCode(...digest))},body:file,signal:controller.signal});
   if(!put.ok)throw new Error();
   await api.request(`/bff/customer/promptpay-qr/branches/${branch}/uploads/${result.assetId}/complete`,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:result.version},signal:controller.signal});
   if(!controller.signal.aborted){setDraft(result.assetId);setFile(undefined);if(input.current)input.current.value="";setNotice("Upload completed. Verify the merchant/payee, amount instructions and full QR border in the preview, then approve for this branch.");}
  }catch{if(!controller.signal.aborted){setSelection(undefined);setNotice("Upload not confirmed or PNG invalid. Use 64–2048 pixels per side; reload before another upload. No branch approval was sent.");}}
  finally{if(!controller.signal.aborted)setBusy(false);}
 };
 const save=async()=>{
  if(!selection||!approved||busy||file)return;const controller=new AbortController();pending.current=controller;setBusy(true);setNotice(undefined);
  try{
   const csrf=await api.request<{requestToken:string}>("/bff/customer/promptpay-qr/csrf",{signal:controller.signal});
   const value=await api.request<Selection>(`/bff/customer/promptpay-qr/branches/${branch}`,{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:selection.version,assetId:draft,approved:true},signal:controller.signal});
   if(!controller.signal.aborted){setSelection(value);setNotice(draft?"Branch QR approved. In POS select PromptPay and Refresh branch QR. Uploading does not confirm any payment.":"Branch QR removed. POS will block new PromptPay confirmations after refreshing.");}
  }catch{if(!controller.signal.aborted){setSelection(undefined);setNotice("Approval outcome is unconfirmed, access denied or the branch changed. Reload before saving again; no automatic retry was sent.");}}
  finally{if(!controller.signal.aborted){setBusy(false);setApproved(false);}}
 };
 return <Card title="PromptPay QR — manually verified" style={{marginTop:16}}>
  <Space direction="vertical" style={{width:"100%"}}>
   <Typography.Paragraph>Authorized configuration managers approve one PNG per active THB branch. This does not verify a bank payment. Cashiers must verify receipt of the exact amount and enter its reference.</Typography.Paragraph>
   <OperationalBranchPicker capability="promptpay-qr" label="PromptPay branch" value={branch} onChange={clear} disabled={busy}/>
   <Button disabled={!branch||busy} onClick={()=>void load()}>Load branch QR settings</Button>
   {notice&&<Alert message={notice}/>}
   {selection&&<>
    <input ref={input} type="file" accept="image/png,.png" aria-label="PromptPay PNG file" disabled={busy} onChange={e=>void choose(e.target.files?.[0])}/>
    {preview&&<img src={preview} alt="PromptPay QR preview — verify branch payee before approval" style={{width:256,height:256,objectFit:"contain",background:"white",imageRendering:"pixelated"}}/>}
    <Typography.Text>PNG only, at most 2 MiB, 64–2048 pixels per side. Preserve the white border; do not crop or stretch.</Typography.Text>
    <Button disabled={busy||!file} onClick={()=>void upload()}>Upload PromptPay PNG</Button>
    <Button danger disabled={busy||!draft} onClick={()=>{disposePreview();setDraft(null);setPreview(undefined);setFile(undefined);setApproved(false);if(input.current)input.current.value="";}}>Remove branch QR selection</Button>
    <Checkbox disabled={busy||!!file} checked={approved} onChange={e=>setApproved(e.target.checked)}>I approve this QR selection and merchant/payee for this branch.</Checkbox>
    <Button type="primary" disabled={busy||!!file||!approved} onClick={()=>void save()}>Approve branch PromptPay QR</Button>
   </>}
  </Space>
 </Card>;
}
