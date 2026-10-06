import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,List,Select,Space,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {Preparation,PreparationCommand,readPreparation,readCutoffPreparation} from "./dayClosePreparation";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function DayClosePreparationPanel({organizationId,branchId,businessDate,cutoff=false}:{organizationId:string;branchId:string;businessDate:string;cutoff?:boolean}){
  const path=cutoff?"/bff/customer/day-close-cutoffs":"/bff/customer/day-close-preparations";
  const [value,setValue]=useState<Preparation>();const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();
  const [reason,setReason]=useState<"routine_close"|"recheck">("routine_close");
  const [pending,setPending]=useState<PreparationCommand>();const active=useRef<AbortController>();const generation=useRef(0);
  useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
  const execute=async(prepare:boolean)=>{
    if(prepare&&!pending&&!value?.canPrepare)return;
    const command=prepare?(pending??{branchId,businessDate,operationId:crypto.randomUUID(),expectedVersion:value!.version,reasonCode:reason}):undefined;
    if(command)setPending(command);
    const id=++generation.current;active.current?.abort();const controller=new AbortController();active.current=controller;
    setValue(undefined);setError(undefined);setBusy(true);
    const timer=setTimeout(()=>{if(id===generation.current){generation.current++;controller.abort();setBusy(false);setError("Preparation unavailable. Load the saved record or retry the same operation after 30 seconds.");}},40_000);
    try{
      const csrf=prepare?await api.request<{requestToken:string}>(`${path}/csrf`,{signal:controller.signal,cache:"no-store"}):undefined;
      if(controller.signal.aborted||generation.current!==id)return;
      const response=await api.request<unknown>(prepare?path:`${path}?${new URLSearchParams({branchId,businessDate})}`,{method:prepare?"POST":"GET",body:command,headers:csrf?{"X-Nexa-CSRF":csrf.requestToken}:undefined,signal:controller.signal,cache:"no-store"});
      if(controller.signal.aborted||generation.current!==id)return;
      const fresh=(cutoff?readCutoffPreparation:readPreparation)(response,organizationId,branchId,businessDate);setValue(fresh);setPending(fresh.pendingCommand??undefined);
    }catch{if(generation.current===id&&!controller.signal.aborted)setError("Preparation unavailable or changed. Load the saved record before a new attempt; an interrupted operation can resume after 30 seconds.");}
    finally{clearTimeout(timer);if(generation.current===id)setBusy(false);}
  };
  return <Space direction="vertical" style={{width:"100%"}}>
    <Typography.Title level={3}>{cutoff?"Day-close cutoff evidence":"Day-close preparation"}</Typography.Title>
    <Alert type="info" message={cutoff?"Retained cutoff evidence":"Preparation only"} description={cutoff?"Each source retains a generation of financial evidence. A fresh check compares Reporting identities and hashes and revalidates the sources. Changes require a new generation. This does not approve settlement or prevent late recovery.":"Ready for review means this request revalidated the saved evidence. It does not approve settlement or stop new source changes. Reload before review; refresh preparation after resolving blockers. Another manager can replace interrupted work after its 30-second lease expires."}/>
    <Space wrap><Button disabled={busy} onClick={()=>void execute(false)}>{cutoff?"Load cutoff evidence":"Load preparation"}</Button>
      {(value?.canPrepare||pending)&&<><Select aria-label={cutoff?"Cutoff reason":"Preparation reason"} value={pending?.reasonCode??reason} disabled={busy||!!pending} onChange={setReason} options={[{value:"routine_close",label:"Routine day close"},{value:"recheck",label:"Recheck evidence"}]}/>
        <Button disabled={busy} loading={busy} onClick={()=>void execute(true)}>{pending?(cutoff?"Resume cutoff":"Resume preparation"):value?.status==="preparing"?(cutoff?"Replace interrupted cutoff":"Replace interrupted preparation"):value?.version?(cutoff?"Refresh cutoff evidence":"Refresh preparation"):(cutoff?"Capture cutoff evidence":"Prepare day close")}</Button></>}
    </Space>
    {error&&<Alert role="status" type="error" message={error}/>}
    {value&&<><Descriptions bordered column={1}><Descriptions.Item label="Preparation status">{value.status.replaceAll("_"," ")}</Descriptions.Item><Descriptions.Item label="Version">{value.version}</Descriptions.Item><Descriptions.Item label="Fresh validation">{value.validatedAtUtc??"No fresh readiness validation"}</Descriptions.Item></Descriptions>
      {value.snapshot&&<Typography.Paragraph>Saved evidence: {value.snapshot.currency} gross {value.snapshot.grossSales}, refunds {value.snapshot.completedRefunds}, net {value.snapshot.netSales}, cash variance {value.snapshot.cashVariance}; {value.snapshot.timeZone}, {value.snapshot.fromUtc} to {value.snapshot.toUtc}. Observed {value.snapshot.observedAtUtc}. This snapshot is retained until an explicit refresh.</Typography.Paragraph>}
      {cutoff&&value.snapshot?.cutoff&&<Typography.Paragraph>Retained source generations: Order {value.snapshot.cutoff.order.generation}, Payment {value.snapshot.cutoff.payment.generation}, POS {value.snapshot.cutoff.pos.generation}. Saved financial gaps: {value.snapshot.cutoff.financialGaps}. {value.status==="ready_for_review"?"Sources unchanged at this validation.":"Retained evidence is blocked or not freshly validated; resolve blockers and refresh."}</Typography.Paragraph>}
      <List header={cutoff?"Cutoff blockers":"Preparation blockers"} dataSource={value.blockers} renderItem={item=><List.Item>{item.replaceAll("_"," ")}</List.Item>}/>
    </>}
  </Space>;
}
