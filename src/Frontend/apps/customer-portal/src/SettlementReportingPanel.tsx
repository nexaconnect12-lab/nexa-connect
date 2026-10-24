import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,Input,Space,Table,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {SettlementLedger,readSettlementLedger,settlementReportingRequest} from "./settlementReporting";
import {fetchSettlementExport,saveSettlementExport} from "./settlementExport";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function SettlementReportingPanel({organizationId,branchId}:{organizationId:string;branchId:string}){
 const [from,setFrom]=useState(()=>new Date(Date.now()-7*86400000).toISOString().slice(0,10)),[to,setTo]=useState(()=>new Date().toISOString().slice(0,10));
 const [value,setValue]=useState<SettlementLedger>(),[busy,setBusy]=useState(false),[error,setError]=useState(false);
 const generation=useRef(0),active=useRef<AbortController>();
 const clear=()=>{generation.current++;active.current?.abort();setValue(undefined);setError(false);setBusy(false);};
 useEffect(()=>{clear();return()=>{generation.current++;active.current?.abort();};},[organizationId,branchId]);
 const request=settlementReportingRequest(branchId,from,to);
 async function load(exportCsv=false){if(!request)return;clear();const version=++generation.current,controller=new AbortController();active.current=controller;setBusy(true);
  const timer=setTimeout(()=>{if(generation.current===version){generation.current++;controller.abort();setBusy(false);setError(true);}},35000);
  try{if(exportCsv){const blob=await fetchSettlementExport(request,controller.signal);if(generation.current===version&&!controller.signal.aborted)saveSettlementExport(blob,request);return;}
   const raw=await api.request<unknown>("/bff/customer/reports/day-settlements?"+new URLSearchParams(request),{signal:controller.signal,cache:"no-store"});
   if(generation.current===version&&!controller.signal.aborted)setValue(readSettlementLedger(raw,organizationId,request));
  }catch{if(generation.current===version&&!controller.signal.aborted)setError(true);}finally{clearTimeout(timer);if(generation.current===version)setBusy(false);}
 }
 return <Space direction="vertical" style={{width:"100%"}}><Typography.Title level={3}>Settlement ledger</Typography.Title>
  <Typography.Paragraph>Review committed branch-day receipts and their delivery to Reporting. Select up to 31 business dates. Correction adjustments explain the retained cash variance; they do not change sales or refunds. Historical receipts may lack correction proof.</Typography.Paragraph>
  <Space wrap><label>Settlement from date<Input aria-label="Settlement from date" type="date" value={from} onChange={e=>{clear();setFrom(e.target.value);}}/></label>
   <label>Settlement to date (exclusive)<Input aria-label="Settlement to date (exclusive)" type="date" value={to} onChange={e=>{clear();setTo(e.target.value);}}/></label>
   <Button disabled={!request||busy} loading={busy} onClick={()=>void load()}>Load settlement ledger</Button>
   <Button disabled={!request||busy} onClick={()=>void load(true)}>Download settlement CSV</Button></Space>
  <Typography.Paragraph>The CSV runs a fresh comparison and includes delivery gaps and historical proof availability. Correction rows explain amounts already included in cash variance. Import money and identifier columns as text to preserve their exact values.</Typography.Paragraph>
  {error&&<Alert role="status" type="error" message="Settlement ledger unavailable. Check access and services, or narrow the dates."/>}
  {value&&<><Alert type={value.status==="matched"?"success":"warning"} message={value.status==="matched"?"Settlement delivery matched":"Settlement delivery has gaps"}/>
   <Typography.Text>Expected {value.expected}; matched {value.matched}; missing {value.missing}; conflicting {value.conflicting}; unexpected {value.unexpected}. Reload to observe later delivery.</Typography.Text>
   <Table rowKey="eventId" pagination={{pageSize:10}} dataSource={value.items} columns={[{title:"Business date",dataIndex:"businessDate"},{title:"Settlement",dataIndex:"settlementId"},{title:"Sales (THB)",dataIndex:"grossSales"},{title:"Refunds (THB)",dataIndex:"completedRefunds"},{title:"Net (THB)",dataIndex:"netSales"},{title:"Cash variance (THB)",dataIndex:"cashVariance"},{title:"Corrections included in variance (THB)",render:(_,r)=>r.corrections?.adjustment??"Proof unavailable"},{title:"Delivery",dataIndex:"status"}]}
    expandable={{expandedRowRender:r=><Space direction="vertical"><Descriptions column={1}><Descriptions.Item label="Approval">{r.approvalId}</Descriptions.Item><Descriptions.Item label="Correction proof">{r.correctionEvidence}</Descriptions.Item><Descriptions.Item label="Correction manifest">{r.corrections?.manifestId??"Unavailable in historical publication"}</Descriptions.Item><Descriptions.Item label="Source seals">{r.sources.map(s=>`${s.source}: ${s.sealId}`).join("; ")}</Descriptions.Item></Descriptions>
     {r.corrections&&<Table rowKey="eventId" pagination={{pageSize:10}} dataSource={r.corrections.items} columns={[{title:"Correction",dataIndex:"correctionId"},{title:"Original settlement",dataIndex:"originalSettlementId"},{title:"Posting date",dataIndex:"postingDate"},{title:"Adjustment (THB)",dataIndex:"adjustment"}]}/>}</Space>}}/></>}
 </Space>;
}
