import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,Form,Input,List,Space,Table,Typography} from "antd";
import {createApiClient} from "@nexaconnect/api-client";
import {DayDraft,dayRequest,readDayDraft} from "./endOfDay";

import {DayClosePreparationPanel} from "./DayClosePreparationPanel";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
const issueNames:Record<string,string>={unresolved_orders:"Unresolved orders",unresolved_payments:"Unresolved payments",unresolved_refunds:"Unresolved refunds",open_shifts:"Open shifts",open_cash_sessions:"Open cash sessions",pending_cash_reviews:"Pending cash reviews",missing_source_evidence:"Missing receipt or retained publication evidence",projection_totals_differ:"Reporting totals differ from source totals",financial_evidence_not_checked:"No financial evidence check recorded for this exact day",recorded_check_is_historical:"Recorded financial check is historical; request a fresh check before closing",recorded_financial_gaps:"Recorded financial check contains gaps",cash_variance:"Cash variance requires review"};
export function EndOfDayPanel({organizationId}:{organizationId:string}){
  const [branch,setBranch]=useState("");const [date,setDate]=useState("");const [report,setReport]=useState<DayDraft>();
  const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();
  const generation=useRef(0);const active=useRef<AbortController>();
  const clear=()=>{generation.current++;active.current?.abort();setReport(undefined);setError(undefined);setBusy(false);};
  useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
  const input=dayRequest(branch,date);
  const load=async()=>{
    if(!input)return;clear();const id=++generation.current;const controller=new AbortController();active.current=controller;setBusy(true);
    const timer=globalThis.setTimeout(()=>{if(generation.current===id){generation.current++;controller.abort();setBusy(false);setError("End-of-day draft unavailable. Reload to try again.");}},40_000);
    try{
      const value=await api.request<unknown>(`/bff/customer/reports/end-of-day?${new URLSearchParams(input)}`,{signal:controller.signal,cache:"no-store"});
      if(generation.current!==id||controller.signal.aborted)return;
      setReport(readDayDraft(value,organizationId,input.branchId,input.businessDate));
    }catch{if(generation.current===id&&!controller.signal.aborted){setReport(undefined);setError("End-of-day draft unavailable. Check your access, branch, date and service availability.");}}
    finally{globalThis.clearTimeout(timer);if(generation.current===id)setBusy(false);}
  };
  return <Space direction="vertical" size="middle" style={{width:"100%"}}>
    <Typography.Title level={2}>End-of-day reconciliation draft</Typography.Title>
    <Alert type="info" message="Read-only draft" description="Select a completed date in the branch timezone. This view does not approve, lock or certify settlement. Source reads happen separately; reload after resolving issues."/>
    <Form onFinish={()=>void load()} layout="vertical"><Space wrap>
      <Form.Item label="Branch ID"><Input aria-label="End-of-day branch ID" value={branch} onChange={e=>{clear();setBranch(e.target.value.trim());}}/></Form.Item>
      <Form.Item label="Business date (branch local)"><Input aria-label="End-of-day business date" type="date" value={date} onChange={e=>{clear();setDate(e.target.value);}}/></Form.Item>
      <Button htmlType="submit" disabled={!input||busy} loading={busy}>Load day draft</Button>
    </Space></Form>
    {input&&<DayClosePreparationPanel key={`${organizationId}|${input.branchId}|${input.businessDate}`} organizationId={organizationId} branchId={input.branchId} businessDate={input.businessDate}/>}
    {error&&<Alert role="status" type="error" message={error}/>}
    {report&&<>
      <Descriptions bordered column={1}>
        <Descriptions.Item label="Branch timezone">{report.branch.timeZone}</Descriptions.Item>
        <Descriptions.Item label="UTC window">{report.window.fromUtc} to {report.window.toUtc} (exclusive)</Descriptions.Item>
        <Descriptions.Item label="Gross sales">{report.branch.currency} {report.grossSales}</Descriptions.Item>
        <Descriptions.Item label="Completed refunds">{report.branch.currency} {report.completedRefunds}</Descriptions.Item>
        <Descriptions.Item label="Net sales">{report.branch.currency} {report.netSales}</Descriptions.Item>
        <Descriptions.Item label="Closed drawer cash variance">{report.branch.currency} {report.cashVariance}</Descriptions.Item>
        <Descriptions.Item label="Unresolved work">Orders {report.order.unresolvedOrders}; payments {report.payment.unresolvedPayments}; refunds {report.payment.unresolvedRefunds}; open shifts {report.pos.openShifts}; open cash sessions {report.pos.openCashSessions}; pending cash reviews {report.pos.pendingCashReviews}</Descriptions.Item>
        <Descriptions.Item label="Source evidence gaps">Orders {report.order.evidenceGaps}; refunds {report.payment.evidenceGaps}</Descriptions.Item>
        <Descriptions.Item label="Source observation times (UTC)">Order {report.order.observedAtUtc}; Payment {report.payment.observedAtUtc}; POS {report.pos.observedAtUtc}</Descriptions.Item>
      </Descriptions>
      <Typography.Paragraph>Gross sales use order time; tenders use receipt paid time; refunds use completion time; drawer variance uses close time. Outstanding work includes earlier items originating before the selected day ended, using their current status. Tender totals are gross receipts, before refunds. Historical dates use the branch’s current timezone configuration.</Typography.Paragraph>
      <Table rowKey={r=>`${r.method}|${r.currency}`} pagination={false} dataSource={report.tenders} columns={[{title:"Tender",dataIndex:"method"},{title:"Currency",dataIndex:"currency"},{title:"Received",dataIndex:"amount"}]}/>
      <List header="Items to investigate" dataSource={report.issues} renderItem={item=><List.Item>{issueNames[item]??"Additional source issue requires investigation"}</List.Item>}/>
    </>}
  </Space>;
}
