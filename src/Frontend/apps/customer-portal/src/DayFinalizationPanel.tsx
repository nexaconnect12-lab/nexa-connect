import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,List,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {Approval} from "./dayApproval";
import {Finalization,FinalizationCommand,readFinalization} from "./dayFinalization";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
type Pending={action:"prepare"|"cancel";command:FinalizationCommand|{branchId:string;businessDate:string;operationId:string}};
export function DayFinalizationPanel({organizationId,branchId,businessDate,reviewedApproval}:{organizationId:string;branchId:string;businessDate:string;reviewedApproval?:Approval}){
 const [value,setValue]=useState<Finalization>(),[pending,setPending]=useState<Pending>(),[busy,setBusy]=useState(false),[error,setError]=useState<string>(),[now,setNow]=useState(Date.now());
 const generation=useRef(0),active=useRef<AbortController>();
 useEffect(()=>{const timer=setInterval(()=>setNow(Date.now()),1000);return()=>{clearInterval(timer);generation.current++;active.current?.abort();};},[]);
 const reviewed=reviewedApproval?.status==="approved"&&reviewedApproval.decision&&reviewedApproval.validatedAtUtc&&now-Date.parse(reviewedApproval.validatedAtUtc)<=60_000;
 const resume=value?.pendingCommand&&["preparing","blocked"].includes(value.status)&&value.expiresAtUtc&&Date.parse(value.expiresAtUtc)>now+15_000;
 const canStart=value?.canPrepare&&["not_prepared","cancelled","expired"].includes(value.status)&&reviewed;
 const execute=async(action:"read"|"prepare"|"cancel")=>{
  let request=pending;
  if(action!=="read"&&!request){
   if(action==="prepare"){
    if(!resume&&!canStart)return;
    request={action,command:resume?value!.pendingCommand!:{branchId,businessDate,operationId:crypto.randomUUID(),expectedVersion:value!.version,approvalId:reviewedApproval!.decision!.approvalId,reviewedApprovalVersion:reviewedApproval!.version}};
   }else{if(!value?.pendingCommand)return;request={action,command:{branchId,businessDate,operationId:value.pendingCommand.operationId}};}
  }
  if(action!=="read")setPending(request);const run=++generation.current;active.current?.abort();const controller=new AbortController();active.current=controller;setBusy(true);setValue(undefined);setError(undefined);
  const timer=setTimeout(()=>{if(generation.current===run){generation.current++;controller.abort();setBusy(false);setError("Preparation response unavailable. Load saved progress or retry the same operation.");}},40_000);
  try{
   const path="/bff/customer/day-close-finalization-preparations",selected=action==="read"?"read":request!.action;
   const csrf=selected!=="read"?await api.request<{requestToken:string}>(path+"/csrf",{signal:controller.signal,cache:"no-store"}):undefined;
   if(generation.current!==run||controller.signal.aborted)return;
   const response=await api.request<unknown>(selected==="read"?`${path}?${new URLSearchParams({branchId,businessDate})}`:path+(selected==="cancel"?"/cancel":""),{method:selected==="read"?"GET":"POST",body:selected==="read"?undefined:request!.command,headers:csrf?{"X-Nexa-CSRF":csrf.requestToken}:undefined,signal:controller.signal,cache:"no-store"});
   if(generation.current!==run||controller.signal.aborted)return;
   const fresh=readFinalization(response,organizationId,branchId,businessDate);
   if(selected!=="read"&&fresh.pendingCommand?.operationId!==request!.command.operationId)throw new Error("Preparation operation mismatch");
   setValue(fresh);setPending(undefined);
  }catch{if(generation.current===run&&!controller.signal.aborted)setError("Preparation unavailable or changed. Load saved progress or retry the same operation.");}
  finally{clearTimeout(timer);if(generation.current===run)setBusy(false);}
 };
 const current=value?.status==="prepared"&&value.expiresAtUtc&&Date.parse(value.expiresAtUtc)>now+15_000&&value.validatedAtUtc&&now-Date.parse(value.validatedAtUtc)<=60_000;
 return <Space direction="vertical" style={{width:"100%"}}><Typography.Title level={3}>Settlement finalization preparation</Typography.Title>
  <Alert type="info" message="Temporary source protection" description="Load and review the current approval before preparing. Relevant financial writes pause for a short lease. Cancel to release protection; expiry releases it automatically. This prepares evidence and does not finalize settlement."/>
  <Space wrap><Button disabled={busy} onClick={()=>void execute("read")}>Load finalization preparation</Button>
   {(value?.canPrepare||pending)&&<><Button disabled={busy||!pending&&!resume&&!canStart} onClick={()=>void execute(pending?.action??"prepare")}>{pending?"Retry same preparation operation":resume?"Resume finalization preparation":"Prepare finalization"}</Button>
    {value?.pendingCommand&&value.status!=="cancelled"&&<Button disabled={busy||!!pending} onClick={()=>void execute("cancel")}>{value.status==="cancelling"?"Retry cancellation":"Cancel finalization preparation"}</Button>}</>}
  </Space>{error&&<Alert role="status" type="error" message={error}/>}
  {value&&<><Descriptions column={1} bordered><Descriptions.Item label="Preparation status">{value.status==="prepared"&&!current?"Verification expired; reload":value.status.replaceAll("_"," ")}</Descriptions.Item><Descriptions.Item label="Preparation version">{value.version}</Descriptions.Item><Descriptions.Item label="Bound approval">{value.approvalId??"None"}</Descriptions.Item><Descriptions.Item label="Source lease expires">{value.expiresAtUtc??"None"}</Descriptions.Item><Descriptions.Item label="Fresh preparation validation">{current?value.validatedAtUtc:"Not currently verified"}</Descriptions.Item></Descriptions>
   <List header="Source protection" dataSource={value.sources} renderItem={s=><List.Item>{s.source}: {s.cancelled?"cancelled":s.active&&Date.parse(s.expiresAtUtc)>now?"temporary fence active":"inactive"}; seal {s.sealId}.</List.Item>}/>
   {value.blockers.length>0&&<Alert type="warning" message={value.blockers.map(x=>x.replaceAll("_"," ")).join(", ")}/>}</>}
 </Space>;
}
