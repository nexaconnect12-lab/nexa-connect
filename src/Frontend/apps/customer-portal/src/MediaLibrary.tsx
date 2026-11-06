import {useEffect,useRef,useState} from "react";
import {Alert,Button,Empty,Input,Modal,Popconfirm,Segmented,Space,Spin,Table,Tag,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {signInToCustomer} from "./navigation";
import {MenuPhoto} from "./MenuPhoto";

export type LibraryPhoto={id:string;originalFileName:string;contentType:string;sizeBytes:number;processingStatus:string;uploadedAtUtc:string;concurrencyVersion:number;ownerService:string;ownerType:string};
type Page={items:LibraryPhoto[];hasMore:boolean;page:number};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function validLibraryPage(value:Page,page:number,readyOnly:boolean){
 return value&&value.page===page&&typeof value.hasMore==="boolean"&&Array.isArray(value.items)&&value.items.length<=24&&new Set(value.items.map(p=>p?.id)).size===value.items.length
  &&value.items.every(p=>p&&/^[0-9a-f-]{36}$/i.test(p.id)&&(p.ownerService==="catalog"&&p.ownerType==="product"||p.ownerService==="media"&&p.ownerType==="photo_library")&&typeof p.originalFileName==="string"&&p.originalFileName.length>0&&p.originalFileName.length<=200
   &&["image/jpeg","image/png","image/webp"].includes(p.contentType)&&Number.isSafeInteger(p.sizeBytes)&&p.sizeBytes>0&&Number.isSafeInteger(p.concurrencyVersion)&&p.concurrencyVersion>0
   &&typeof p.processingStatus==="string"&&p.processingStatus.length<=40&&(!readyOnly||p.processingStatus==="ready")&&Number.isFinite(Date.parse(p.uploadedAtUtc)));
}
export function MediaLibrary({onChoose,selectedId,excludedIds=[],refreshKey=0}:{onChoose?:(photo:LibraryPhoto)=>void;selectedId?:string;excludedIds?:string[];refreshKey?:number}){
 const [view,setView]=useState("grid"),[query,setQuery]=useState(""),[page,setPage]=useState(1),[readyOnly,setReadyOnly]=useState(!!onChoose);
 const [items,setItems]=useState<LibraryPhoto[]>([]),[more,setMore]=useState(false),[busy,setBusy]=useState(false),[notice,setNotice]=useState<string>(),[revision,setRevision]=useState(0),[inspect,setInspect]=useState<LibraryPhoto>();
 const [actionBusy,setActionBusy]=useState(false);
 const action=useRef<AbortController>();
 useEffect(()=>()=>{const current=action.current;action.current=undefined;current?.abort();},[]);
 useEffect(()=>{
  const c=new AbortController();setItems([]);setMore(false);setNotice(undefined);setBusy(true);
  const deadline=window.setTimeout(()=>c.abort(),20000);
  const timer=window.setTimeout(()=>{void api.request<Page>(`/bff/customer/media-library?search=${encodeURIComponent(query)}&page=${page}&readyOnly=${readyOnly}`,{signal:c.signal,cache:"no-store"}).then(value=>{
   if(c.signal.aborted)return;if(!validLibraryPage(value,page,readyOnly))throw new Error();setItems(value.items);setMore(value.hasMore);
  }).catch(()=>{if(!c.signal.aborted)setNotice("Photo library unavailable or access denied. Refresh to retry.");}).finally(()=>{if(!c.signal.aborted){setBusy(false);window.clearTimeout(deadline);}});},250);
  const timedOut=()=>{setBusy(false);setNotice("Photo library timed out. Refresh to retry.");};c.signal.addEventListener("abort",timedOut,{once:true});
  return()=>{c.signal.removeEventListener("abort",timedOut);window.clearTimeout(timer);window.clearTimeout(deadline);c.abort();};
 },[query,page,readyOnly,revision,refreshKey]);
 const refresh=()=>{setPage(1);setRevision(v=>v+1);};
 const choose=(photo:LibraryPhoto)=>{if(onChoose){if(photo.processingStatus==="ready"&&!excludedIds.includes(photo.id))onChoose(photo);}else setInspect(photo);};
 const beginAction=()=>{const c=new AbortController();action.current=c;setActionBusy(true);const timeout=window.setTimeout(()=>{if(action.current!==c)return;action.current=undefined;c.abort();setActionBusy(false);setItems([]);setMore(false);setInspect(undefined);setNotice("The result is unconfirmed. Refresh the library before retrying.");},20000);return {c,timeout};};
 const endAction=(c:AbortController,timeout:number)=>{window.clearTimeout(timeout);if(action.current===c){action.current=undefined;setActionBusy(false);}};
 const download=async(photo:LibraryPhoto)=>{if(actionBusy)return;const {c,timeout}=beginAction();try{const lease=await api.request<{downloadUrl:string}>(`/bff/customer/media/${photo.id}/download`,{signal:c.signal});const target=new URL(lease.downloadUrl);if(!["http:","https:"].includes(target.protocol)||target.username||target.password)throw new Error();if(!c.signal.aborted)window.location.assign(target.href);}catch{if(!c.signal.aborted)setNotice("Download unavailable. Check processing status and access.");}finally{endAction(c,timeout);}};
 const remove=async(photo:LibraryPhoto)=>{if(actionBusy)return;const {c,timeout}=beginAction();try{await api.request(`/bff/customer/media/${photo.id}?expectedVersion=${photo.concurrencyVersion}`,{method:"DELETE",signal:c.signal});if(!c.signal.aborted){setInspect(undefined);refresh();}}catch{if(!c.signal.aborted)setNotice("Delete failed or changed. Refresh before retrying.");}finally{endAction(c,timeout);}};
 const actions=(photo:LibraryPhoto)=><Space wrap>{onChoose?<Button disabled={busy||photo.processingStatus!=="ready"||excludedIds.includes(photo.id)} onClick={()=>choose(photo)}>Select photo</Button>:<><Button disabled={actionBusy||photo.processingStatus!=="ready"} onClick={()=>void download(photo)}>Download</Button><Popconfirm title="Delete this photo?" description="Menu images and screensavers using it may show a placeholder until replaced." onConfirm={()=>remove(photo)} okText="Delete" cancelText="Cancel"><Button danger disabled={actionBusy}>Delete</Button></Popconfirm></>}</Space>;
 return <section aria-label="Photo library">
  <Space wrap style={{marginBottom:16}}><Input aria-label="Search photo library" placeholder="Search photos by filename" maxLength={100} value={query} onChange={e=>{setPage(1);setQuery(e.target.value);}}/>
   <Segmented aria-label="Photo library view" value={view} options={[{label:"Thumbnails",value:"grid"},{label:"Details",value:"details"}]} onChange={value=>setView(String(value))}/>
   {!onChoose&&<Segmented aria-label="Photo processing filter" value={readyOnly?"ready":"all"} options={[{label:"All photos",value:"all"},{label:"Ready",value:"ready"}]} onChange={value=>{setPage(1);setReadyOnly(value==="ready");}}/>}
   <Button onClick={refresh} disabled={actionBusy}>Refresh library</Button>
  </Space>
  {notice&&<Alert role="status" type="warning" message={notice} style={{marginBottom:16}}/>}
  {busy?<Spin tip="Loading photos"><div style={{minHeight:120}}/></Spin>:items.length===0?<Empty description={query?"No matching photos":"No photos in this view"}/>:view==="grid"?
   <div aria-label="Photo thumbnail grid" style={{display:"grid",gridTemplateColumns:"repeat(auto-fill,minmax(150px,1fr))",gap:16}}>{items.map(photo=><div key={photo.id} style={{minWidth:0}}>
    <button type="button" aria-label={`${onChoose?"Select":"Preview"} photo ${photo.originalFileName}`} aria-pressed={selectedId===photo.id} disabled={!!onChoose&&(photo.processingStatus!=="ready"||excludedIds.includes(photo.id))}
      onClick={()=>choose(photo)} style={{width:"100%",border:selectedId===photo.id?"3px solid #1677ff":"1px solid #d9d9d9",borderRadius:12,padding:8,background:"#fff",cursor:"pointer"}}>
     {photo.processingStatus==="ready"?<MenuPhoto src={`/bff/customer/media-library/assets/${photo.id}/thumbnail`} name={photo.originalFileName} tile refreshKey={revision+refreshKey}/>:<div style={{height:160,display:"grid",placeItems:"center"}}>Processing preview unavailable</div>}
     <span style={{display:"block",overflow:"hidden",textOverflow:"ellipsis",whiteSpace:"nowrap"}}>{photo.originalFileName}</span>
     <Tag>{excludedIds.includes(photo.id)?"Already selected":photo.processingStatus}</Tag>
    </button>
   </div>)}</div>:
   <Table<LibraryPhoto> rowKey="id" dataSource={items} pagination={false} scroll={{x:650}} columns={[
    {title:"Photo",render:(_,photo)=><Button onClick={()=>choose(photo)} disabled={!!onChoose&&excludedIds.includes(photo.id)}>{photo.originalFileName}</Button>},
    {title:"Type",dataIndex:"contentType"},{title:"Size",render:(_,photo)=>`${(photo.sizeBytes/1024).toFixed(1)} KB`},
    {title:"Uploaded",render:(_,photo)=>new Date(photo.uploadedAtUtc).toLocaleString()},{title:"Status",dataIndex:"processingStatus"},{title:"Actions",render:(_,photo)=>actions(photo)}]}/>}
  <Space style={{marginTop:16}}><Button disabled={busy||page===1||actionBusy} onClick={()=>setPage(v=>v-1)}>Previous photos</Button><span>Page {page}</span><Button disabled={busy||!more||page>=10000||actionBusy} onClick={()=>setPage(v=>v+1)}>Next photos</Button></Space>
  {page>=10000&&more&&<Typography.Paragraph>Refine your search to see more photos.</Typography.Paragraph>}
  <Modal title="Photo details" open={!!inspect} footer={null} onCancel={()=>setInspect(undefined)} destroyOnClose>
   {inspect&&<Space direction="vertical"><MenuPhoto src={`/bff/customer/media-library/assets/${inspect.id}/thumbnail`} name={inspect.originalFileName} large/>
    <Typography.Text>{inspect.originalFileName}</Typography.Text><Typography.Text>{inspect.contentType} · {(inspect.sizeBytes/1024).toFixed(1)} KB · {inspect.processingStatus}</Typography.Text><Typography.Text>{new Date(inspect.uploadedAtUtc).toLocaleString()}</Typography.Text>{actions(inspect)}</Space>}
  </Modal>
 </section>;
}
