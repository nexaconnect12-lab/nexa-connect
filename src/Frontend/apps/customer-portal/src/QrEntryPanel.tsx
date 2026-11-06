import {useEffect,useRef,useState} from "react";
import {Alert,Button,Card,Input,QRCode,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {signInToCustomer} from "./navigation";
import {OperationalBranchPicker} from "./OperationalBranchPicker";

type Entry={id:string;tableCode:string;version:number;active:boolean;expiresAtUtc:string};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function QrEntryPanel(){
  const [branch,setBranch]=useState(""),[entries,setEntries]=useState<Entry[]>([]),[code,setCode]=useState("");
  const [issued,setIssued]=useState<{entry:Entry;entryUrl:string}>(),[busy,setBusy]=useState(false),[notice,setNotice]=useState<string>();
  const pending=useRef<AbortController>();
  const qrPreview=useRef<HTMLDivElement>(null);
  useEffect(()=>()=>pending.current?.abort(),[]);
  const select=(value:string)=>{pending.current?.abort();setBranch(value);setEntries([]);setIssued(undefined);setNotice(undefined);setBusy(false);};
  const load=async()=>{
    const c=new AbortController();pending.current?.abort();pending.current=c;setBusy(true);setIssued(undefined);setNotice(undefined);
    try{const list=await api.request<Entry[]>(`/bff/customer/qr-entries/branches/${branch}`,{signal:c.signal});if(!c.signal.aborted)setEntries(list);}
    catch{if(!c.signal.aborted){setEntries([]);setNotice("Table QR entry is unavailable or you do not have configuration permission.");}}
    finally{if(!c.signal.aborted)setBusy(false);}
  };
  const mutate=async(entry?:Entry,revoke=false)=>{
    const c=new AbortController();pending.current=c;setBusy(true);setIssued(undefined);setNotice(undefined);
    try{
      const csrf=await api.request<{requestToken:string}>("/bff/customer/qr-entries/csrf",{signal:c.signal});
      const path=`/bff/customer/qr-entries/branches/${branch}`+(entry?`/${entry.id}/${revoke?"revoke":"rotate"}`:"");
      const result=await api.request<Entry|{entry:Entry;entryUrl:string}>(path,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:entry?{expectedVersion:entry.version}:{tableCode:code},signal:c.signal});
      if(c.signal.aborted)return;
      const changed="entry" in result?result.entry:result;
      setEntries(list=>[...list.filter(e=>e.id!==changed.id),changed].sort((a,b)=>a.tableCode.localeCompare(b.tableCode)));
      if("entry" in result)setIssued(result);
      setCode("");setNotice(revoke?"Table entry revoked. Existing visits can no longer use it.":"Save this QR privately for the table. It is shown only until you leave or reload this view.");
    }catch{if(!c.signal.aborted){setEntries([]);setNotice("The result is unconfirmed or the entry changed. Reload before another action. A lost QR can be replaced by rotating it.");}}
    finally{if(!c.signal.aborted)setBusy(false);}
  };
  const downloadForPos=()=>{
    const canvas=qrPreview.current?.querySelector("canvas");
    if(!issued||!canvas||!(Date.parse(issued.entry.expiresAtUtc)>Date.now()))return;
    let entryUrl:URL;
    try{entryUrl=new URL(issued.entryUrl);}catch{return;}
    if(entryUrl.protocol!=="https:"||entryUrl.pathname!=="/order"||entryUrl.search
      ||entryUrl.username||entryUrl.password||!/^#[A-Za-z0-9_-]{43}$/.test(entryUrl.hash))return;
    const exportCanvas=document.createElement("canvas");
    exportCanvas.width=exportCanvas.height=384;
    const ctx=exportCanvas.getContext("2d");
    if(!ctx)return;
    ctx.imageSmoothingEnabled=false;
    ctx.drawImage(canvas,0,0,384,384);
    const png=exportCanvas.toDataURL("image/png").split(",")[1];
    const url=URL.createObjectURL(new Blob([JSON.stringify({version:1,branchId:branch,
      tableCode:issued.entry.tableCode,expiresAtUtc:issued.entry.expiresAtUtc,png})],{type:"application/json"}));
    const anchor=document.createElement("a");
    anchor.href=url;anchor.download=issued.entry.tableCode+".nexa-table-qr.json";anchor.click();
    window.setTimeout(()=>URL.revokeObjectURL(url),1000);
  };
  return <Card title="Table QR entry" style={{marginTop:24}}>
    <Typography.Paragraph>Manage table codes for guest ordering. Rotation replaces the existing QR and ends all current visits for that table.</Typography.Paragraph>
    <Space wrap><OperationalBranchPicker capability="qr-entry" label="QR branch" value={branch} disabled={busy} onChange={select}/><Button disabled={!branch||busy} onClick={()=>void load()}>Load table QRs</Button></Space>
    {notice&&<Alert role="status" message={notice}/>}
    <Space wrap style={{marginTop:16}}><Input aria-label="Table code" value={code} maxLength={32} disabled={!branch||busy} onChange={e=>setCode(e.target.value)} placeholder="Table code, e.g. A1"/><Button disabled={!branch||busy||!code.trim()} onClick={()=>void mutate()}>Create table QR</Button></Space>
    {issued&&<Card title={`Table ${issued.entry.tableCode}`}><div ref={qrPreview}><QRCode type="canvas" includeMargin bgColor="#FFFFFF" value={issued.entryUrl} size={220}/></div><Typography.Paragraph>Expires {new Date(issued.entry.expiresAtUtc).toLocaleDateString()}</Typography.Paragraph><Space wrap><a href={issued.entryUrl} target="_blank" rel="noreferrer noopener">Open table entry</a><Button onClick={downloadForPos}>Download for POS</Button><Button onClick={()=>setIssued(undefined)}>Hide QR</Button></Space></Card>}
    {entries.map(entry=><Space key={entry.id} wrap style={{display:"flex",marginTop:12}}><Typography.Text>Table {entry.tableCode} · {entry.active?"Issued":"Revoked"} · version {entry.version}</Typography.Text><Button disabled={busy} onClick={()=>void mutate(entry)}>Rotate / reissue</Button><Button danger disabled={busy||!entry.active} onClick={()=>void mutate(entry,true)}>Revoke</Button></Space>)}
  </Card>;
}
