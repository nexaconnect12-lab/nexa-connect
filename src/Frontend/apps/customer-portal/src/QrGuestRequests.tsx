import {useEffect,useRef,useState} from "react";
import {Alert,Button,Typography} from "antd";
import {QrMenuCart,type Quote} from "./QrMenuCart";
export type RequestView={id:string;tableCode:string;status:"pending"|"price_review"|"accepted"|"rejected"|"expired";version:number;submittedAtUtc:string;deadlineUtc:string;reason?:string;quote:Quote;reviewQuote?:Quote;orderId?:string;orderStatus?:string};
type Submission={quoteId:string;fingerprint:string};
export function QrGuestRequests({currency,onExpired}:{currency:string;onExpired:()=>void}){
 const [requests,setRequests]=useState<RequestView[]>([]),[loaded,setLoaded]=useState(false),[busy,setBusy]=useState(false),[uncertain,setUncertain]=useState(false),[notice,setNotice]=useState<string>(),[cartKey,setCartKey]=useState(0);
 const original=useRef<Submission>(),alive=useRef(true),callback=useRef(onExpired),sending=useRef(false),recoveryRevision=useRef(0);callback.current=onExpired;
 const resolve=(items:RequestView[])=>{setRequests(items);setLoaded(true);if(original.current&&items.some(i=>i.id===original.current?.quoteId)){original.current=undefined;setUncertain(false);setCartKey(k=>k+1);}};
 const recover=async()=>{
  const revision=++recoveryRevision.current;
  try{const response=await fetch("/bff/guest/qr/requests",{credentials:"same-origin",cache:"no-store",redirect:"error"});if(response.status===401){callback.current();return;}if(!response.ok)throw new Error();
   const data=await response.json() as {items:RequestView[]};if(alive.current&&revision===recoveryRevision.current){resolve(data.items);setNotice(undefined);}}
  catch{if(alive.current&&revision===recoveryRevision.current)setNotice("Request status unavailable. Reconnect and check again before submitting.");}
 };
 useEffect(()=>{alive.current=true;void recover();const timer=window.setInterval(()=>void recover(),15000);return()=>{alive.current=false;window.clearInterval(timer);};},[]);
 const send=async(input:Submission)=>{
  if(sending.current)return;recoveryRevision.current++;sending.current=true;original.current=input;setUncertain(true);setBusy(true);setNotice(undefined);
  try{const response=await fetch("/bff/guest/qr/requests",{method:"POST",credentials:"same-origin",cache:"no-store",redirect:"error",headers:{"Content-Type":"application/json"},body:JSON.stringify(input)});
   if(response.status===401){callback.current();return;}
   if(response.status===400||response.status===409){original.current=undefined;if(alive.current){setUncertain(false);setCartKey(k=>k+1);setNotice("The quote expired or another request is pending. Check status and review totals again.");}await recover();return;}
   if(!response.ok)throw new Error();const value=await response.json() as RequestView;
   if(alive.current){original.current=undefined;setUncertain(false);setRequests(old=>[value,...old.filter(i=>i.id!==value.id)]);setLoaded(true);setCartKey(k=>k+1);}}
  catch{if(alive.current)setNotice("Submission outcome is unknown. Check request status or retry the original request. Do not rescan or place it again with staff.");}
  finally{sending.current=false;if(alive.current)setBusy(false);}
 };
 const confirm=async(item:RequestView)=>{if(!item.reviewQuote||sending.current)return;sending.current=true;setBusy(true);recoveryRevision.current++;
  try{const response=await fetch(`/bff/guest/qr/requests/${item.id}/confirm-price`,{method:"POST",credentials:"same-origin",cache:"no-store",redirect:"error",headers:{"Content-Type":"application/json"},body:JSON.stringify({expectedVersion:item.version,fingerprint:item.reviewQuote.fingerprint})});if(response.status===401){callback.current();return;}if(!response.ok)throw new Error();await recover();}
  catch{setNotice("Price confirmation changed or is unavailable. Check original request status before retrying.");}finally{sending.current=false;setBusy(false);}};
 const pending=requests.some(r=>r.status==="pending"||r.status==="price_review");
 return <section aria-label="Your requests">
  <Typography.Title level={3}>Your requests</Typography.Title>
  <p>Pending requests are unpaid and await staff acceptance. Accepted orders still require cashier payment.</p>
  <p>Ending this visit or rescanning does not cancel a request. A new scan cannot recover this visit's requests; ask staff for help.</p>
  {notice&&<Alert role="status" type="warning" message={notice}/>}
  {uncertain&&<Alert role="status" type="warning" message="Keep this visit open while the submission outcome is checked. Retry uses the original quote identity."/>}
  <Button style={{minHeight:44}} disabled={busy} onClick={()=>void recover()}>Check request status</Button>
  {uncertain&&<Button style={{minHeight:44}} disabled={busy||!original.current} onClick={()=>original.current&&void send(original.current)}>Retry original request</Button>}
  {!loaded&&<p role="status">Checking this visit's requests…</p>}
  {requests.map(r=><article key={r.id} style={{borderBottom:"1px solid #ddd",padding:12,overflowWrap:"anywhere"}}>
   <strong>Request {r.id.slice(0,8)} · Table {r.tableCode} · {r.status}</strong>
   <p>{new Intl.NumberFormat(undefined,{style:"currency",currency:r.quote.currency}).format(r.quote.totalAmount)} · deadline {new Date(r.deadlineUtc).toLocaleTimeString()}</p>
   {r.reason&&<p>Staff category: {r.reason.replaceAll("_"," ")}</p>}
   <ul>{r.quote.lines.map(l=><li key={l.productId}>{l.name} × {l.quantity}</li>)}</ul>
   {r.orderId&&<p>Original Order {r.orderId} · {r.orderStatus}. Ask staff about preparation and cashier collection.</p>}
   {r.status==="price_review"&&r.reviewQuote&&<section aria-label="Updated prices">
    <Alert type="warning" message="Prices changed. Review the new breakdown before confirming."/>
    <ul>{r.reviewQuote.lines.map(l=><li key={l.productId}>{l.name} × {l.quantity}: {l.lineTotal.toFixed(2)}</li>)}</ul>
    <p>{r.reviewQuote.currency} · Subtotal {r.reviewQuote.subtotalAmount.toFixed(2)} · Service {r.reviewQuote.serviceChargeAmount.toFixed(2)} · Tax {r.reviewQuote.taxAmount.toFixed(2)} · Total {r.reviewQuote.totalAmount.toFixed(2)}</p>
    <Button block disabled={busy||Date.parse(r.reviewQuote.expiresAtUtc)<=Date.now()} style={{minHeight:44}} onClick={()=>void confirm(r)}>Confirm updated prices for staff review</Button>
    {Date.parse(r.reviewQuote.expiresAtUtc)<=Date.now()&&<p>Price review expired. Ask staff to refresh this original request.</p>}
   </section>}
  </article>)}
  {loaded&&!pending&&!uncertain&&!busy&&<QrMenuCart key={cartKey} currency={currency} onExpired={onExpired} submit={quote=>{if(quote.quoteId)void send({quoteId:quote.quoteId,fingerprint:quote.fingerprint});}}/>}
  {pending&&<Alert role="status" message="A request is pending. Check its status before creating another."/>}
 </section>;
}
