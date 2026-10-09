import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,List,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {Approval} from "./dayApproval";
import {Finalization} from "./dayFinalization";
import {readSettlement,SettlementCommand,SettlementView} from "./daySettlement";

import {LateWorkReviewPanel} from "./LateWorkReviewPanel";
import {LateScope} from "./lateWork";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function DaySettlementPanel({organizationId,branchId,businessDate,preparation,reviewedApproval}:{organizationId:string;branchId:string;businessDate:string;preparation?:Finalization;reviewedApproval?:Approval}){
 const [value,setValue]=useState<SettlementView>(),[pending,setPending]=useState<SettlementCommand>(),[busy,setBusy]=useState(false),[error,setError]=useState<string>();
 const [reviewScope,setReviewScope]=useState<LateScope>();
 const generation=useRef(0),active=useRef<AbortController>();
 useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
 const execute=async(finalize:boolean)=>{
  let command=pending;
  const now=Date.now();
  if(finalize&&!command){
   if(!value?.canFinalize||value.settlement&&value.settlement.status!=="aborted"||preparation?.status!=="prepared"||!preparation.pendingCommand
    ||!preparation.validatedAtUtc||now-Date.parse(preparation.validatedAtUtc)>60_000||!preparation.expiresAtUtc||Date.parse(preparation.expiresAtUtc)<=now+15_000
    ||reviewedApproval?.status!=="approved"||reviewedApproval.decision?.approvalId!==preparation.approvalId||reviewedApproval.version!==preparation.reviewedApprovalVersion)return;
   command={branchId,businessDate,operationId:crypto.randomUUID(),expectedPreparationVersion:preparation.version,
    preparationOperationId:preparation.pendingCommand.operationId,approvalId:preparation.approvalId!,reviewedApprovalVersion:preparation.reviewedApprovalVersion!};
   setPending(command);
  }
  const run=++generation.current;active.current?.abort();const controller=new AbortController();active.current=controller;setBusy(true);setError(undefined);setValue(undefined);
  const timer=setTimeout(()=>controller.abort(),40_000);
  try{
   const path="/bff/customer/day-close-settlements";
   const csrf=finalize?await api.request<{requestToken:string}>(path+"/csrf",{signal:controller.signal,cache:"no-store"}):undefined;
   if(generation.current!==run)return;
   const raw=await api.request<unknown>(finalize?path:`${path}?${new URLSearchParams({branchId,businessDate})}`,{method:finalize?"POST":"GET",
    body:finalize?command:undefined,headers:csrf?{"X-Nexa-CSRF":csrf.requestToken}:undefined,signal:controller.signal,cache:"no-store"});
   if(generation.current!==run)return;
   const fresh=readSettlement(raw,organizationId,branchId,businessDate);
   if(finalize&&fresh.settlement?.command.operationId!==command?.operationId)throw new Error("Settlement operation mismatch");
   if(fresh.settlement&&command&&fresh.settlement.command.operationId===command.operationId)setPending(undefined);
   setValue(fresh);
   if(fresh.settlement?.receipt){const window=fresh.settlement.sources[0]?.proof.command.fence.window;if(window)setReviewScope({settlementId:fresh.settlement.id,window});}
  }catch{if(generation.current===run)setError("Settlement response unavailable or changed. Load progress or retry the same operation.");}
  finally{clearTimeout(timer);if(generation.current===run)setBusy(false);}
 };
 const settlement=value?.settlement;
 return <Space direction="vertical" style={{width:"100%"}}><Typography.Title level={3}>Finalize settlement</Typography.Title>
  <Alert type="warning" message="Finalization preserves the approved snapshot" description="Finalization permanently protects this day. Late financial work is retained for correction review. Load current approval and preparation before finalizing."/>
  <Space wrap><Button disabled={busy} onClick={()=>void execute(false)}>Load settlement progress</Button>
   {(value?.canFinalize||pending)&&<Button disabled={busy||!pending&&(!value?.canFinalize||!!settlement&&settlement.status!=="aborted"||preparation?.status!=="prepared")}
    onClick={()=>void execute(true)}>{pending?"Retry same settlement operation":"Finalize settlement"}</Button>}</Space>
  {error&&<Alert role="status" type="error" message={error}/>}
  {settlement&&<><Descriptions column={1} bordered><Descriptions.Item label="Settlement status">{settlement.status}</Descriptions.Item>
   <Descriptions.Item label="Settlement identifier"><span id={`settlement-${settlement.id}`}>{settlement.id}</span></Descriptions.Item><Descriptions.Item label="Reviewed approval">{settlement.command.approvalId}</Descriptions.Item></Descriptions>
   {!value?.sourceProofCurrent&&<Alert type="info" message="Source counts are from saved progress. Load progress to request current counts."/>}
   <List header="Source acknowledgements" dataSource={settlement.sources} renderItem={s=><List.Item>{s.source}: {s.proof.phase}; late work retained: {s.proof.lateWorkCount}.</List.Item>}/>
   {settlement.receipt&&<Descriptions title="Immutable settlement receipt" column={1} bordered><Descriptions.Item label="Receipt event">{settlement.receipt.eventId}</Descriptions.Item>
    <Descriptions.Item label="Settled at">{settlement.receipt.settledAtUtc}</Descriptions.Item><Descriptions.Item label="Gross sales">THB {settlement.receipt.snapshot.grossSales}</Descriptions.Item>
    <Descriptions.Item label="Completed refunds">THB {settlement.receipt.snapshot.completedRefunds}</Descriptions.Item><Descriptions.Item label="Net sales">THB {settlement.receipt.snapshot.netSales}</Descriptions.Item>
    <Descriptions.Item label="Cash variance">THB {settlement.receipt.snapshot.cashVariance}</Descriptions.Item></Descriptions>}</>}
  {reviewScope&&<LateWorkReviewPanel key={reviewScope.settlementId} scope={reviewScope} businessDate={businessDate}/>}
 </Space>;
}
