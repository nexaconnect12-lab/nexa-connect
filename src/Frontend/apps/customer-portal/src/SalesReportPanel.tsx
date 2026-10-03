import { useEffect, useRef, useState } from "react";
import { Alert, Button, Card, Descriptions, Form, Input, Space, Table, Tag, Typography } from "antd";
import { ApiError, createApiClient } from "@nexaconnect/api-client";
import { Completeness, readCompleteness, reportWindow } from "./financialCompleteness";

type SalesReport={items:Array<{orderId:string;channel:string;serviceType:string;currency:string;totalAmount:number;orderStatus:string;orderedAtUtc:string}>;
  grossSales:number;refundedAmount:number;netSales:number;currency?:string;latestGlobalCheckpointUpdatedAtUtc?:string};
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});

export function SalesReportPanel({organizationId}:{organizationId:string}) {
  const [branch,setBranch]=useState("");
  const [from,setFrom]=useState(()=>new Date(Date.now()-7*86400000).toISOString().slice(0,16));
  const [to,setTo]=useState(()=>new Date().toISOString().slice(0,16));
  const [sales,setSales]=useState<SalesReport>();const [completeness,setCompleteness]=useState<Completeness>();
  const [salesError,setSalesError]=useState<string>();const [statusError,setStatusError]=useState<string>();
  const [busy,setBusy]=useState(false);const generation=useRef(0);const request=useRef<AbortController>();
  useEffect(()=>()=>{generation.current++;request.current?.abort();},[]);
  const clear=()=>{
    generation.current++;request.current?.abort();setBusy(false);setSales(undefined);setCompleteness(undefined);setSalesError(undefined);setStatusError(undefined);
  };
  const window=reportWindow(branch,from,to);
  const load=async()=>{
    if(!window)return;clear();const id=++generation.current,controller=new AbortController();request.current=controller;setBusy(true);
    const timer=globalThis.setTimeout(()=>{
      if(generation.current!==id)return;generation.current++;controller.abort();setBusy(false);
      setSalesError("Sales report unavailable. Reload when your connection is restored.");
      setStatusError("Reconciliation status unavailable. Reload to try again.");
    },25_000);
    const params=new URLSearchParams(window);
    try{
      const [report,status]=await Promise.allSettled([
        api.request<SalesReport>(`/bff/customer/reports/sales?${params}`,{signal:controller.signal,cache:"no-store"}),
        api.request<unknown>(`/bff/customer/reports/financial-completeness?${params}`,{signal:controller.signal,cache:"no-store"}),
      ]);
      if(generation.current!==id||controller.signal.aborted)return;
      const denied=[report,status].some(result=>result.status==="rejected"&&result.reason instanceof ApiError&&[401,403].includes(result.reason.status));
      if(denied){setSalesError("You no longer have access to this report.");setStatusError("Reconciliation status unavailable: access denied.");return;}
      if(report.status==="fulfilled")setSales(report.value);else setSalesError("Sales report unavailable. Check the date range and reload.");
      if(status.status==="fulfilled"){
        try{setCompleteness(readCompleteness(status.value,organizationId,window));}
        catch{setStatusError("Reconciliation status unavailable. Reload to try again.");}
      }else setStatusError("Reconciliation status unavailable. Reload to try again.");
    }finally{globalThis.clearTimeout(timer);if(generation.current===id)setBusy(false);}
  };
  const observation=completeness?.observation;
  return <Space direction="vertical" size="middle" style={{width:"100%"}}>
    <Typography.Title level={2}>Sales report</Typography.Title>
    <Form layout="vertical" onFinish={()=>void load()}>
      <Space wrap>
        <Form.Item label="Branch ID"><Input aria-label="Sales branch ID" value={branch} onChange={e=>{clear();setBranch(e.target.value.trim());}}/></Form.Item>
        <Form.Item label="From (UTC, inclusive)"><Input aria-label="Sales from UTC" type="datetime-local" value={from} onChange={e=>{clear();setFrom(e.target.value);}}/></Form.Item>
        <Form.Item label="To (UTC, exclusive)"><Input aria-label="Sales to UTC" type="datetime-local" value={to} onChange={e=>{clear();setTo(e.target.value);}}/></Form.Item>
      </Space>
      <Button htmlType="submit" loading={busy} disabled={!window||busy}>Load sales report</Button>
    </Form>
    <Typography.Text>One branch and a closed UTC period of at most 31 days. Enter the branch ID supplied by your administrator.</Typography.Text>
    {salesError&&<Alert role="status" type="error" message={salesError}/>}
    {sales&&<>
      <Descriptions bordered><Descriptions.Item label="Completed sales">{sales.currency??""} {sales.grossSales}</Descriptions.Item>
        <Descriptions.Item label="Refunded in range">{sales.currency??""} {sales.refundedAmount}</Descriptions.Item>
        <Descriptions.Item label="Net sales">{sales.currency??""} {sales.netSales}</Descriptions.Item></Descriptions>
      <Table rowKey="orderId" scroll={{x:650}} dataSource={sales.items} columns={[{title:"Ordered (UTC)",dataIndex:"orderedAtUtc"},{title:"Channel",dataIndex:"channel"},
        {title:"Service",dataIndex:"serviceType"},{title:"Status",dataIndex:"orderStatus",render:value=><Tag>{value}</Tag>},
        {title:"Total",render:(_,row)=>`${row.currency} ${row.totalAmount}`}]}/>
      <Typography.Text type="secondary">Latest global projector checkpoint: {sales.latestGlobalCheckpointUpdatedAtUtc??"No projector checkpoint yet"}. This does not establish completeness for this branch.</Typography.Text>
    </>}
    <Card title="Financial reconciliation" aria-label="Financial reconciliation">
      <Typography.Paragraph>This view reads a recorded check; it does not run reconciliation or repair records. A check reflects evidence available at that time. Later activity may change it, and it does not certify settlement or complete accounting.</Typography.Paragraph>
      {busy&&<Typography.Text role="status">Loading reconciliation status…</Typography.Text>}
      {statusError&&<Alert role="status" type="error" message={statusError}/>}
      {completeness?.status==="not_checked"&&<Alert role="status" type="info" message="Not checked" description="No recorded check exists for this branch and exact period. Request a reconciliation check before relying on completeness."/>}
      {observation&&<>
        <Alert role="status" type={completeness?.status==="observed_complete"?"success":"warning"}
          message={completeness?.status==="observed_complete"?"Observed complete":"Gaps detected"}
          description={completeness?.status==="observed_complete"?"Available source evidence matched the report at the recorded check time.":"Some source evidence or report records need investigation."}/>
        <Descriptions bordered style={{marginTop:16}} column={1}>
          <Descriptions.Item label="Checked at (UTC)">{observation.checkedAtUtc}</Descriptions.Item>
          <Descriptions.Item label="Order evidence read (UTC)">{observation.orderObservedAtUtc}</Descriptions.Item>
          <Descriptions.Item label="Refund evidence read (UTC)">{observation.refundObservedAtUtc}</Descriptions.Item>
          <Descriptions.Item label="Source evidence gaps">Sales: {observation.saleEvidenceGaps}; refunds: {observation.refundEvidenceGaps}</Descriptions.Item>
          <Descriptions.Item label="Unretained source evidence">Sales: {observation.unretainedSales}; refunds: {observation.unretainedRefunds}</Descriptions.Item>
        </Descriptions>
        <Table pagination={false} scroll={{x:700}} rowKey="kind" dataSource={[{kind:"Sales",...observation.sales},{kind:"Payments",...observation.payments},{kind:"Refunds",...observation.refunds}]} columns={[
          {title:"Evidence",dataIndex:"kind"},{title:"Expected",dataIndex:"expected"},{title:"Matched",dataIndex:"matched"},
          {title:"Missing",dataIndex:"missing"},{title:"Conflicting",dataIndex:"conflicting"},{title:"Unexpected",dataIndex:"unexpected"},{title:"Gaps",dataIndex:"gaps"},
        ]}/>
      </>}
    </Card>
  </Space>;
}
