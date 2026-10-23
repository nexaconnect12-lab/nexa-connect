import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Checkbox,Input,InputNumber,Select,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";

type Slide={assetId:string;title:string;caption:string};
type Gallery={organizationId:string;branchId:string;version:number;staff:Slide[];customer:Slide[];secondsPerSlide:number};
type Asset={id:string;originalFileName:string;processingStatus:string};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function BranchGalleryPanel(){
  const [branch,setBranch]=useState(""),[gallery,setGallery]=useState<Gallery>(),[assets,setAssets]=useState<Asset[]>([]);
  const [busy,setBusy]=useState(false),[approved,setApproved]=useState(false),[notice,setNotice]=useState<string>();
  const pending=useRef<AbortController>();
  useEffect(()=>()=>pending.current?.abort(),[]);
  const clear=(value:string)=>{pending.current?.abort();setBranch(value);setGallery(undefined);setAssets([]);setNotice(undefined);setApproved(false);};
  const load=async()=>{
    const controller=new AbortController();pending.current?.abort();pending.current=controller;setBusy(true);setGallery(undefined);setNotice(undefined);setApproved(false);
    try{
      const [selection,media]=await Promise.all([api.request<Gallery>(`/bff/customer/screensaver-gallery/branches/${branch}`,{signal:controller.signal}),api.request<Asset[]>("/bff/customer/media",{signal:controller.signal})]);
      if(!controller.signal.aborted){setGallery(selection);setAssets(media.filter(a=>a.processingStatus==="ready"));}
    }catch{if(!controller.signal.aborted)setNotice("Gallery unavailable or access denied. Check your workspace and permissions.");}
    finally{if(!controller.signal.aborted)setBusy(false);}
  };
  const change=(role:"staff"|"customer",slides:Slide[])=>{setGallery(g=>g?{...g,[role]:slides}:g);setApproved(false);};
  const save=async()=>{
    if(!gallery||!approved||busy)return;const controller=new AbortController();pending.current=controller;setBusy(true);setNotice(undefined);
    try{
      const csrf=await api.request<{requestToken:string}>("/bff/customer/screensaver-gallery/csrf",{signal:controller.signal});
      const result=await api.request<Gallery>(`/bff/customer/screensaver-gallery/branches/${branch}`,{method:"PUT",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:gallery.version,staff:gallery.staff,customer:gallery.customer,secondsPerSlide:gallery.secondsPerSlide},signal:controller.signal});
      if(!controller.signal.aborted){setGallery(result);setNotice("Gallery approved. Terminals refresh while active; cached approval expires within five minutes.");}
    }catch{if(!controller.signal.aborted){setGallery(undefined);setNotice("Save outcome is unconfirmed or the selection changed. Reload before making another save; no automatic retry was sent.");}}
    finally{if(!controller.signal.aborted){setBusy(false);setApproved(false);}}
  };
  return <Card title="Approved screensaver gallery" style={{marginTop:24}}>
    <Typography.Paragraph>Select scanned, processed product images, separate ordered lists for staff and customers, and public-safe text. Saving approves this content for the selected branch. Empty lists remove media and use neutral branding.</Typography.Paragraph>
    <Space wrap><OperationalBranchPicker capability="catalog" label="Gallery branch" value={branch} disabled={busy} onChange={clear}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load gallery</Button></Space>
    {notice&&<Alert role="status" message={notice}/>}
    {gallery&&<Space direction="vertical" style={{width:"100%"}}>
      <label>Seconds per slide <InputNumber aria-label="Seconds per slide" min={5} max={60} value={gallery.secondsPerSlide} disabled={busy} onChange={v=>{setGallery({...gallery,secondsPerSlide:v??10});setApproved(false);}}/></label>
      {(["staff","customer"] as const).map(role=><Card key={role} title={`${role==="staff"?"Staff":"Customer"} screensaver`}>
        {gallery[role].map((slide,index)=><Space key={slide.assetId} wrap style={{marginBottom:12}}>
          <Typography.Text>{assets.find(a=>a.id===slide.assetId)?.originalFileName??"Unavailable asset (remove before saving)"}</Typography.Text>
          <Input aria-label={`${role} slide ${index+1} title`} placeholder="Public product name" maxLength={80} value={slide.title} disabled={busy} onChange={e=>change(role,gallery[role].map((s,i)=>i===index?{...s,title:e.target.value}:s))}/>
          <Input aria-label={`${role} slide ${index+1} caption`} placeholder="Public promotional text" maxLength={160} value={slide.caption} disabled={busy} onChange={e=>change(role,gallery[role].map((s,i)=>i===index?{...s,caption:e.target.value}:s))}/>
          <Button disabled={busy||index===0} onClick={()=>{const copy=[...gallery[role]];[copy[index-1],copy[index]]=[copy[index]!,copy[index-1]!];change(role,copy);}}>Move up</Button>
          <Button disabled={busy} onClick={()=>change(role,gallery[role].filter((_,i)=>i!==index))}>Remove</Button>
        </Space>)}
        <Select<string | null> virtual={false} aria-label={`Add ${role} image`} placeholder={`Add ${role} image`} style={{width:"100%"}} value={null} disabled={busy||gallery[role].length>=6}
          options={assets.filter(a=>!gallery[role].some(s=>s.assetId===a.id)).map(a=>({value:a.id,label:a.originalFileName}))}
          onChange={value=>{if(value)change(role,[...gallery[role],{assetId:value,title:"",caption:""}]);}}/>
      </Card>)}
      <Checkbox checked={approved} disabled={busy} onChange={e=>setApproved(e.target.checked)}>I approve these images and text for public display at this branch.</Checkbox>
      <Button type="primary" disabled={busy||!approved} onClick={()=>void save()}>Approve gallery</Button>
    </Space>}
  </Card>;
}
