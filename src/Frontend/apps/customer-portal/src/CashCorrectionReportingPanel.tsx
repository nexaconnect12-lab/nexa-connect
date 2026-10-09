import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,Input,Space,Table,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {CorrectionReport,correctionReportingRequest,readCorrectionReport} from "./cashCorrectionReporting";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function CashCorrectionReportingPanel({organizationId,branchId}:{organizationId:string;branchId:string}){
 const [from,setFrom]=useState(()=>new Date(Date.now()-86400000).toISOString().slice(0,19));
 const [to,setTo]=useState(()=>new Date().toISOString().slice(0,19));
 const [value,setValue]=useState<CorrectionReport>();const [busy,setBusy]=useState(false);const [error,setError]=useState(false);
 const generation=useRef(0),active=useRef<AbortController>();
 const clear=()=>{generation.current++;active.current?.abort();setValue(undefined);setError(false);setBusy(false);};
 useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
 const utc=(v:string)=>v.length===16?v+":00Z":v+"Z";
 const request=correctionReportingRequest(branchId,utc(from),utc(to));
 async function load(){if(!request)return;clear();const id=++generation.current,controller=new AbortController();active.current=controller;setBusy(true);
  const timer=setTimeout(()=>{if(generation.current===id){generation.current++;controller.abort();setBusy(false);setError(true);}},35000);
  try{const raw=await api.request<unknown>("/bff/customer/reports/cash-corrections?"+new URLSearchParams(request),{signal:controller.signal,cache:"no-store"});
   if(generation.current===id&&!controller.signal.aborted)setValue(readCorrectionReport(raw,organizationId,request));
  }catch{if(generation.current===id&&!controller.signal.aborted)setError(true);}finally{clearTimeout(timer);if(generation.current===id)setBusy(false);}
 }
 return <Space direction="vertical" style={{width:"100%"}}>
  <Typography.Title level={3}>Cash correction reporting</Typography.Title>
  <Typography.Paragraph>Compare posted cash variance adjustments with Reporting delivery for a UTC window of up to 31 days. Original sales, tenders and settled drawers remain fixed. This comparison observes separate source and Reporting snapshots; reload to check later delivery.</Typography.Paragraph>
  <Space wrap><label>Correction from (UTC)<Input aria-label="Correction from (UTC)" type="datetime-local" step="1" value={from} onChange={e=>{clear();setFrom(e.target.value);}}/></label>
   <label>Correction to (UTC, exclusive)<Input aria-label="Correction to (UTC, exclusive)" type="datetime-local" step="1" value={to} onChange={e=>{clear();setTo(e.target.value);}}/></label>
   <Button disabled={!request||busy} loading={busy} onClick={()=>void load()}>Compare cash correction delivery</Button></Space>
  {error&&<Alert role="status" type="error" message="Cash correction reporting unavailable. Check access and services, or narrow the window."/>}
  {value&&<><Alert type={value.status==="matched"?"success":"warning"} message={value.status==="matched"?"Cash correction delivery matched":"Cash correction delivery has gaps"}/>
   <Descriptions column={1}><Descriptions.Item label="Source / projected adjustments">THB {value.sourceAdjustment} / {value.projectedAdjustment}</Descriptions.Item>
    <Descriptions.Item label="Delivery counts">Expected {value.expected}; matched {value.matched}; missing {value.missing}; conflicting {value.conflicting}; unexpected {value.unexpected}</Descriptions.Item>
    <Descriptions.Item label="Observation times">POS {value.sourceObservedAtUtc}; Reporting {value.comparedAtUtc}</Descriptions.Item></Descriptions>
   <Table rowKey="eventId" pagination={{pageSize:10}} dataSource={value.items} columns={[{title:"Correction",dataIndex:"correctionId"},{title:"Original settlement",dataIndex:"originalSettlementId"},{title:"Posting day",dataIndex:"postingDate"},{title:"Adjustment (THB)",dataIndex:"adjustment"},{title:"Delivery",dataIndex:"status"}]}/></>}
 </Space>;
}
