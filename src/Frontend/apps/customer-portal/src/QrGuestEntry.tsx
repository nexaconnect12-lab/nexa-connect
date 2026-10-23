import {useEffect,useState} from "react";
import {Alert,Button,Card,Space,Typography} from "antd";
import {QrMenuCart} from "./QrMenuCart";
import {QrGuestRequests} from "./QrGuestRequests";

export type QrVisit={visitorId:string;entryId:string;restaurantName:string;branchName:string;tableCode:string;currency:string;expiresAtUtc:string;menuAvailable?:boolean;requestsAvailable?:boolean;orderingAvailable:false};
export const isQrGuestEntry=()=>window.location.pathname==="/order"||window.location.pathname==="/order/";
// Entry capabilities never reach query strings, persistent JS storage or staff navigation.
let entryToken=isQrGuestEntry()?window.location.hash.slice(1):"";
if(isQrGuestEntry())window.history.replaceState(null,"",window.location.pathname);
let initial:Promise<QrVisit>|undefined;
async function context():Promise<QrVisit>{
  const response=await fetch("/bff/guest/qr/context",{credentials:"same-origin",cache:"no-store",redirect:"error"});
  if(!response.ok)throw new Error("entry_unavailable");return response.json() as Promise<QrVisit>;
}
function start():Promise<QrVisit>{
  if(!initial){
    const token=entryToken;entryToken="";
    initial=token?fetch("/bff/guest/qr/sessions",{method:"POST",credentials:"same-origin",cache:"no-store",redirect:"error",headers:{"Content-Type":"application/json"},body:JSON.stringify({entryToken:token})})
      .then(response=>{if(!response.ok)throw new Error("entry_unavailable");return response.json() as Promise<QrVisit>;}):context();
  }
  return initial;
}
export function QrGuestEntry(){
  const [visit,setVisit]=useState<QrVisit>(),[busy,setBusy]=useState(true),[notice,setNotice]=useState<string>();
  useEffect(()=>{let live=true;void start().then(v=>{if(live)setVisit(v);}).catch(()=>{if(live)setNotice("This table entry is unavailable. Scan the current QR code or ask staff for help.");}).finally(()=>{if(live)setBusy(false);});return()=>{live=false;};},[]);
  useEffect(()=>{if(!visit)return;const timer=window.setTimeout(()=>{setVisit(undefined);setNotice("Your visit has expired. Scan the table QR again.");},Math.max(0,Date.parse(visit.expiresAtUtc)-Date.now()));return()=>window.clearTimeout(timer);},[visit]);
  const restore=async()=>{setBusy(true);try{setVisit(await context());setNotice(undefined);}catch{setVisit(undefined);setNotice("No active visit was found. Scan the current table QR or ask staff.");}finally{setBusy(false);}};
  const end=async()=>{setBusy(true);try{const response=await fetch("/bff/guest/qr/sessions",{method:"DELETE",credentials:"same-origin",cache:"no-store",redirect:"error"});if(!response.ok)throw new Error();setVisit(undefined);setNotice("Your visit has ended.");}catch{setNotice("Your visit could not be ended. Try again when connected.");}finally{setBusy(false);}};
  return <main style={{maxWidth:520,margin:"0 auto",padding:20,minHeight:"100dvh"}}>
    <Card title="Welcome to your table" loading={busy}>
      <Space direction="vertical" size="large" style={{width:"100%"}}>
        {notice&&<Alert role="status" message={notice}/>}
        {visit&&<><Typography.Title level={2}>{visit.restaurantName}</Typography.Title><Typography.Text>{visit.branchName}</Typography.Text>
          <Typography.Title level={3}>Table {visit.tableCode}</Typography.Title>
          {visit.menuAvailable?(visit.requestsAvailable?<QrGuestRequests key={visit.visitorId} currency={visit.currency} onExpired={()=>{setVisit(undefined);setNotice("Your visit has expired or been revoked. Ask staff about any submitted request before rescanning.");}}/>:<QrMenuCart key={visit.visitorId} currency={visit.currency} onExpired={()=>{setVisit(undefined);setNotice("Your visit has expired or been revoked. Scan the current table QR again.");}}/>):
            <Alert type="info" message="Menu ordering is not available yet. Please ask staff to order and pay at the cashier."/>}
          <Button block style={{minHeight:44}} disabled={busy} onClick={()=>void end()}>End this visit</Button></>}
        {!visit&&!busy&&<Button block style={{minHeight:44}} onClick={()=>void restore()}>Restore this visit</Button>}
      </Space>
    </Card>
  </main>;
}
