import {useEffect,useState} from "react";
import {Alert,Button,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";
import type {RequestView} from "./QrGuestRequests";
const api=createApiClient({onUnauthorized:signInToCustomer});
export function QrReviewPanel(){
 const [branch,setBranch]=useState("");return <><Typography.Title level={2}>QR requests</Typography.Title><OperationalBranchPicker capability="qr-requests" value={branch} onChange={setBranch} label="QR review branch"/>{branch&&<Queue key={branch} branch={branch}/>}</>;
}
function Queue({branch}:{branch:string}){
 const [state,setState]=useState("pending"),[items,setItems]=useState<RequestView[]>([]),[more,setMore]=useState(false),[busy,setBusy]=useState(false),[notice,setNotice]=useState<string>(),[acceptance,setAcceptance]=useState(false);
 useEffect(()=>{const controller=new AbortController();void api.request<{acceptanceAvailable?:boolean}>("/bff/customer/qr-requests/csrf",{signal:controller.signal,cache:"no-store"}).then(v=>{if(!controller.signal.aborted)setAcceptance(v.acceptanceAvailable===true);}).catch(()=>{});return()=>controller.abort();},[]);
 const accept=async(item:RequestView)=>{setBusy(true);try{const csrf=await api.request<{requestToken:string}>("/bff/customer/qr-requests/csrf",{cache:"no-store"});await api.request(`/bff/customer/qr-requests/branches/${branch}/${item.id}/accept`,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:item.version}});await load();}catch{setNotice("Acceptance outcome is unknown or changed. Reload the original request before retrying.");}finally{setBusy(false);}};
 const load=async(signal?:AbortSignal)=>{setBusy(true);setNotice(undefined);try{const data=await api.request<{items:RequestView[];hasMore:boolean}>(`/bff/customer/qr-requests/branches/${branch}?state=${state}`,{signal,cache:"no-store"});if(!signal?.aborted){setItems(data.items);setMore(data.hasMore);}}catch{if(!signal?.aborted){setItems([]);setNotice("Requests unavailable or access denied. Reload before retrying.");}}finally{if(!signal?.aborted)setBusy(false);}};
 useEffect(()=>{const controller=new AbortController();setItems([]);void load(controller.signal);return()=>controller.abort();},[branch,state]);
 const reject=async(item:RequestView,reason:string)=>{setBusy(true);try{const csrf=await api.request<{requestToken:string}>("/bff/customer/qr-requests/csrf",{cache:"no-store"});await api.request(`/bff/customer/qr-requests/branches/${branch}/${item.id}/reject`,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:item.version,reason}});await load();}catch{setNotice("Rejection outcome changed or is unavailable. Reload the queue before retrying.");}finally{setBusy(false);}};
 return <section aria-label="QR review queue" style={{marginTop:16}}>
  <Alert message={acceptance?"Acceptance starts Inventory and Kitchen work on the original unpaid Order. Changed prices require guest review.":"Requests are unpaid proposals. Acceptance is disabled."}/>
  <label>Request state <select aria-label="Request state" value={state} disabled={busy} onChange={e=>setState(e.target.value)} style={{minHeight:44}}>{["pending","price_review","accepted","rejected","expired"].map(s=><option key={s}>{s}</option>)}</select></label>
  <Button disabled={busy} onClick={()=>void load()} style={{minHeight:44}}>Reload requests</Button>
  {notice&&<Alert role="status" type="warning" message={notice}/>}{!busy&&items.length===0&&<p>No {state} requests.</p>}
  {more&&<p>Showing 10 requests. Pending requests are oldest first; reload after decisions to review more.</p>}
  {items.map(item=><article key={item.id} style={{borderBottom:"1px solid #ccc",padding:16,overflowWrap:"anywhere"}}>
   <Typography.Title level={4}>Table {item.tableCode} · {item.status} · {item.id.slice(0,8)}</Typography.Title>
   <ul>{item.quote.lines.map(l=><li key={l.productId}>{l.name} × {l.quantity} — {item.quote.currency} {l.lineTotal.toFixed(2)}</li>)}</ul>
   <p>Subtotal {item.quote.subtotalAmount.toFixed(2)} · Service {item.quote.serviceChargeAmount.toFixed(2)} · Tax {item.quote.taxAmount.toFixed(2)} · Total {item.quote.currency} {item.quote.totalAmount.toFixed(2)}</p>
   <p>Deadline {new Date(item.deadlineUtc).toLocaleString()}</p>
   {item.orderId&&<p>Original Order {item.orderId} · {item.orderStatus} · {item.orderStatus==="paid"?"Payment confirmed":"Collect payment at cashier"}</p>}
   {item.collectionStatus&&<p>Collection: {item.collectionStatus.replaceAll("_"," ")}</p>}
   {item.collectionReference&&item.orderStatus!=="paid"&&<p>Collection reference: {item.collectionReference}</p>}
   {item.status==="price_review"&&<Alert message="Waiting for the original guest to review current prices. Reload after confirmation."/>}
   {item.status==="price_review"&&acceptance&&<Button disabled={busy} style={{minHeight:44}} onClick={()=>void accept(item)}>Refresh guest price review</Button>}
   {item.status==="pending"&&acceptance&&<Button type="primary" disabled={busy} style={{minHeight:44}} onClick={()=>void accept(item)}>Accept original request</Button>}
   {(item.status==="pending"||item.status==="price_review")&&["unavailable","closing","staff_review"].map(reason=><Button key={reason} disabled={busy} style={{minHeight:44,margin:4}} onClick={()=>void reject(item,reason)}>Reject: {reason.replaceAll("_"," ")}</Button>)}
  </article>)}
 </section>;
}
