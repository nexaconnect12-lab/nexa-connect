import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,List,Select,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {Approval,ApprovalCommand,readApproval} from "./dayApproval";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function DayCloseApprovalPanel({organizationId,branchId,businessDate,reviewedSealVersion,onApprovalChange}:{organizationId:string;branchId:string;businessDate:string;reviewedSealVersion?:number;onApprovalChange?:(value?:Approval)=>void}){
 const [value,setValue]=useState<Approval>(),[pending,setPending]=useState<ApprovalCommand>(),[busy,setBusy]=useState(false),[error,setError]=useState<string>();
 const [reason,setReason]=useState<"review_complete"|"review_after_changes">("review_complete");const generation=useRef(0),active=useRef<AbortController>();
 useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
 const execute=async(approve:boolean)=>{
  if(approve&&!pending&&(!value?.canApprove||!value.sealVersion||value.sealVersion!==reviewedSealVersion||value.decision?.sealVersion===value.sealVersion))return;
  const command=approve?(pending??{branchId,businessDate,operationId:crypto.randomUUID(),expectedApprovalVersion:value!.version,reviewedSealVersion:value!.sealVersion!,reasonCode:reason}):undefined;
  if(command)setPending(command);const run=++generation.current;active.current?.abort();const controller=new AbortController();active.current=controller;
  setBusy(true);setValue(undefined);onApprovalChange?.(undefined);setError(undefined);
  const timer=setTimeout(()=>{if(generation.current===run){generation.current++;controller.abort();setBusy(false);setError("Approval response unavailable. Load the saved decision or retry the same operation.");}},40_000);
  try{
   const path="/bff/customer/day-close-approvals";const csrf=approve?await api.request<{requestToken:string}>(path+"/csrf",{signal:controller.signal,cache:"no-store"}):undefined;
   if(controller.signal.aborted||generation.current!==run)return;
   const response=await api.request<unknown>(approve?path:`${path}?${new URLSearchParams({branchId,businessDate})}`,{method:approve?"POST":"GET",body:command,headers:csrf?{"X-Nexa-CSRF":csrf.requestToken}:undefined,signal:controller.signal,cache:"no-store"});
   if(controller.signal.aborted||generation.current!==run)return;const fresh=readApproval(response,organizationId,branchId,businessDate);
   if(command&&fresh.operationDecision?.operationId!==command.operationId)throw new Error("Approval operation mismatch");
   setValue(fresh);onApprovalChange?.(fresh);setPending(undefined);
  }catch{if(generation.current===run&&!controller.signal.aborted)setError("Approval unavailable or changed. Load the saved decision before a new attempt, or retry the same operation.");}
  finally{clearTimeout(timer);if(generation.current===run)setBusy(false);}
 };
 return <Space direction="vertical" style={{width:"100%"}}>
  <Typography.Title level={3}>Day-close manager approval</Typography.Title>
  <Alert type="info" message="Approval of reviewed evidence" description="Load and review the saved seal before approving. This decision preserves the reviewed snapshot. Relevant changes supersede approval when checked; unavailable evidence makes validity unverified. Settlement finalization is a separate workflow."/>
  <Space wrap><Button disabled={busy} onClick={()=>void execute(false)}>Load approval</Button>
   {(value?.canApprove||pending)&&<><Select aria-label="Approval reason" value={pending?.reasonCode??reason} disabled={busy||!!pending} onChange={setReason} options={[{value:"review_complete",label:"Review complete"},{value:"review_after_changes",label:"Review after changes"}]}/>
    <Button disabled={busy||!pending&&(!value?.sealVersion||value.sealVersion!==reviewedSealVersion||value.decision?.sealVersion===value.sealVersion)} loading={busy} onClick={()=>void execute(true)}>{pending?"Retry same approval":"Approve reviewed seal"}</Button></>}
  </Space>
  {error&&<Alert role="status" type="error" message={error}/>}
  {value&&<><Descriptions bordered column={1}><Descriptions.Item label="Approval status">{value.status.replaceAll("_"," ")}</Descriptions.Item><Descriptions.Item label="Approval version">{value.version}</Descriptions.Item><Descriptions.Item label="Fresh approval validation">{value.validatedAtUtc??"Not currently verified"}</Descriptions.Item><Descriptions.Item label="Reviewed seal version">{value.sealVersion??"No ready seal available"}</Descriptions.Item></Descriptions>
   {value.sealSnapshot!=null&&<Typography.Paragraph>Approval candidate: seal version {value.sealVersion}, {value.sealSnapshot.currency} gross {value.sealSnapshot.grossSales}, refunds {value.sealSnapshot.completedRefunds}, net {value.sealSnapshot.netSales}, cash variance {value.sealSnapshot.cashVariance}. Load this ready version in the sealed evidence panel before approving.</Typography.Paragraph>}
   {value.decision&&<Typography.Paragraph>Decision {value.decision.approvalId}: {value.decision.approverSubject}, {value.decision.reasonCode.replaceAll("_"," ")}, recorded {value.decision.approvedAtUtc}. Bound seal version {value.decision.sealVersion}.</Typography.Paragraph>}
   {value.historyTruncated&&<Alert type="info" message="Showing the latest 20 decisions; earlier immutable history remains retained."/>}
   <List header="Immutable approval decisions" dataSource={value.history} renderItem={d=><List.Item>Seal version {d.sealVersion}: {d.reasonCode.replaceAll("_"," ")} by {d.approverSubject} at {d.approvedAtUtc}; decision {d.approvalId}.</List.Item>}/>
  </>}
 </Space>;
}
