import {useEffect,useState} from "react";
import {Alert,Space} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {signInToCustomer} from "./navigation";

type Branch={branchId:string;name:string;code:string;timeZone:string;currency:string};
const api=createApiClient({onUnauthorized:signInToCustomer});
export function OperationalBranchPicker({capability,value,onChange,disabled=false,label="Branch"}:{
 capability:string;value:string;onChange:(value:string)=>void;disabled?:boolean;label?:string}){
 const [branches,setBranches]=useState<Branch[]>();const [error,setError]=useState(false);
 useEffect(()=>{const controller=new AbortController();setBranches(undefined);setError(false);
  api.request<Branch[]>(`/bff/customer/operational-branches?capability=${encodeURIComponent(capability)}`,{signal:controller.signal,cache:"no-store"})
   .then(items=>{if(!controller.signal.aborted)setBranches(items);}).catch(()=>{if(!controller.signal.aborted)setError(true);});
  return()=>controller.abort();},[capability]);
 return <Space direction="vertical"><select aria-label={label} style={{minWidth:280,padding:8}} value={value}
  disabled={disabled||!branches} onChange={event=>onChange(event.target.value)}>
  <option value="">{!branches&&!error?"Loading branches…":"Select branch"}</option>
  {branches?.map(b=><option key={b.branchId} value={b.branchId}>{b.name} ({b.code}) · {b.currency} · {b.timeZone}</option>)}
  </select>{error&&<Alert type="error" message="Branch discovery unavailable. Reload this screen."/>}
  {branches?.length===0&&<Alert type="info" message="No active branches authorized for this workflow."/>}</Space>;
}
