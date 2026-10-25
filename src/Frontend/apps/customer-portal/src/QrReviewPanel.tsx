import {useEffect,useRef,useState} from "react";
import {Alert,Button,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {OperationalBranchPicker} from "./OperationalBranchPicker";
import {signInToCustomer} from "./navigation";
import {qrStatus} from "./qrStatus";
import {useVisibleRefresh} from "./useVisibleRefresh";
import type {RequestView} from "./QrGuestRequests";
const api=createApiClient({onUnauthorized:signInToCustomer});
export function QrReviewPanel(){
 const [branch,setBranch]=useState("");return <><Typography.Title level={2}>QR requests</Typography.Title><OperationalBranchPicker capability="qr-requests" value={branch} onChange={setBranch} label="QR review branch"/>{branch&&<Queue key={branch} branch={branch}/>}</>;
}
function Queue({branch}:{branch:string}){
 const loading=useRef(false),deciding=useRef(false),revision=useRef(0),alive=useRef(true);
 const controllers=useRef(new Set<AbortController>());
 const call=async<T,>(path:string,options:Parameters<typeof api.request>[1]={})=>{const controller=new AbortController();controllers.current.add(controller);const abort=()=>controller.abort();options.signal?.addEventListener("abort",abort,{once:true});if(options.signal?.aborted)controller.abort();const timeout=window.setTimeout(abort,20000);try{return await api.request<T>(path,{...options,signal:controller.signal});}finally{window.clearTimeout(timeout);controllers.current.delete(controller);options.signal?.removeEventListener("abort",abort);}};
 useEffect(()=>{alive.current=true;return()=>{alive.current=false;revision.current++;controllers.current.forEach(c=>c.abort());};},[]);
 const [state,setState]=useState("pending"),[items,setItems]=useState<RequestView[]>([]),[more,setMore]=useState(false),[busy,setBusy]=useState(false),[notice,setNotice]=useState<string>(),[acceptance,setAcceptance]=useState(false);
 useEffect(()=>{const controller=new AbortController();void call<{acceptanceAvailable?:boolean}>("/bff/customer/qr-requests/csrf",{signal:controller.signal,cache:"no-store"}).then(v=>{if(!controller.signal.aborted)setAcceptance(v.acceptanceAvailable===true);}).catch(()=>{});return()=>controller.abort();},[]);
 const accept=async(item:RequestView)=>{if(deciding.current||loading.current)return;deciding.current=true;setBusy(true);try{const csrf=await call<{requestToken:string}>("/bff/customer/qr-requests/csrf",{cache:"no-store"});if(!alive.current)return;await call(`/bff/customer/qr-requests/branches/${branch}/${item.id}/accept`,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:item.version}});if(alive.current)await load();}catch{if(alive.current)setNotice("Acceptance outcome is unknown or changed. Reload the original request before retrying.");}finally{deciding.current=false;if(alive.current)setBusy(false);}};
 const load=async(signal?:AbortSignal)=>{if(!alive.current||loading.current)return;loading.current=true;const generation=revision.current;setBusy(true);setNotice(undefined);try{const data=await call<{items:RequestView[];hasMore:boolean}>(`/bff/customer/qr-requests/branches/${branch}?state=${state}`,{signal,cache:"no-store"});if(alive.current&&!signal?.aborted&&generation===revision.current){setItems(data.items);setMore(data.hasMore);}}catch{if(alive.current&&!signal?.aborted&&generation===revision.current){setItems([]);setNotice("Requests unavailable or access denied. Reload before retrying.");}}finally{if(generation===revision.current)loading.current=false;if(alive.current&&!signal?.aborted&&generation===revision.current)setBusy(false);}};
 useEffect(()=>{const controller=new AbortController();setItems([]);void load(controller.signal);return()=>{revision.current++;loading.current=false;controller.abort();};},[branch,state]);
 useVisibleRefresh(async()=>{if(!deciding.current&&!loading.current)await load();});
 const reject=async(item:RequestView,reason:string)=>{if(deciding.current||loading.current)return;deciding.current=true;setBusy(true);try{const csrf=await call<{requestToken:string}>("/bff/customer/qr-requests/csrf",{cache:"no-store"});if(!alive.current)return;await call(`/bff/customer/qr-requests/branches/${branch}/${item.id}/reject`,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},body:{expectedVersion:item.version,reason}});if(alive.current)await load();}catch{if(alive.current)setNotice("Rejection outcome changed or is unavailable. Reload the queue before retrying.");}finally{deciding.current=false;if(alive.current)setBusy(false);}};
 return <section aria-label="QR review queue" style={{marginTop:16}}>
  <Alert message={acceptance?"Acceptance starts Inventory and Kitchen work on the original unpaid Order. Changed prices require guest review.":"Requests are unpaid proposals. Acceptance is disabled."}/>
  <p>Refreshes every 15 seconds while this page is visible and focused. Decisions always require your action.</p>
  <label>Request state <select aria-label="Request state" value={state} disabled={busy} onChange={e=>setState(e.target.value)} style={{minHeight:44}}>{["pending","price_review","accepted","rejected","expired"].map(s=><option key={s} value={s}>{s==="accepted"?"Accepted":qrStatus({status:s})}</option>)}</select></label>
  <Button disabled={busy} onClick={()=>void load()} style={{minHeight:44}}>Reload requests</Button>
  {notice&&<Alert role="status" type="warning" message={notice}/>}{!busy&&items.length===0&&<p>No {state} requests.</p>}
  {more&&<p>Showing 10 requests. Pending requests are oldest first; reload after decisions to review more.</p>}
  {items.map(item=><article key={item.id} style={{borderBottom:"1px solid #ccc",padding:16,overflowWrap:"anywhere"}}>
   <Typography.Title level={4}>Table {item.tableCode} · {qrStatus(item)} · {item.id.slice(0,8)}</Typography.Title>
   <ul>{item.quote.lines.map(l=><li key={l.productId}>{l.name} × {l.quantity} — {item.quote.currency} {l.lineTotal.toFixed(2)}</li>)}</ul>
   <p>Subtotal {item.quote.subtotalAmount.toFixed(2)} · Service {item.quote.serviceChargeAmount.toFixed(2)} · Tax {item.quote.taxAmount.toFixed(2)} · Total {item.quote.currency} {item.quote.totalAmount.toFixed(2)}</p>
   <p>Deadline {new Date(item.deadlineUtc).toLocaleString()}</p>
   {item.orderId&&<p>Original Order {item.orderId} · {qrStatus(item)} · {item.orderStatus==="paid"?"Payment confirmed":"Collect payment at cashier"}</p>}
   {item.collectionStatus&&<p>Collection: {qrStatus(item)}</p>}
   {item.collectionReference&&item.orderStatus!=="paid"&&<p>Collection reference: {item.collectionReference}</p>}
   {item.status==="price_review"&&<Alert message="Waiting for the original guest to review current prices. Reload after confirmation."/>}
   {item.status==="price_review"&&acceptance&&<Button disabled={busy} style={{minHeight:44}} onClick={()=>void accept(item)}>Refresh guest price review</Button>}
   {item.status==="pending"&&acceptance&&<Button type="primary" disabled={busy} style={{minHeight:44}} onClick={()=>void accept(item)}>Accept original request</Button>}
   {(item.status==="pending"||item.status==="price_review")&&["unavailable","closing","staff_review"].map(reason=><Button key={reason} disabled={busy} style={{minHeight:44,margin:4}} onClick={()=>void reject(item,reason)}>Reject: {reason.replaceAll("_"," ")}</Button>)}
  </article>)}
 </section>;
}
