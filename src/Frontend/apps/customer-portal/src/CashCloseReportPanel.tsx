import { useEffect, useRef, useState } from "react";
import { Alert, Button, Form, Input, Space, Table, Typography } from "antd";
import { createApiClient } from "@nexaconnect/api-client";

type Snapshot = { sessionId:string; shiftId:string; currency:string; expectedAmount:number; countedAmount:number;
  varianceAmount:number; closedAtUtc:string; reviewStatus:string; financialVersion:number; reviewVersion:number;
  snapshotVersion:number; capturedAtUtc:string };
type Page = { items:Array<{snapshot:Snapshot;projectedAtUtc:string}>;nextCursor?:string };
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
const uuid=/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function CashCloseReportPanel(){
  const [branch,setBranch]=useState("");const [store,setStore]=useState("");
  const [from,setFrom]=useState(()=>new Date(Date.now()-7*86400000).toISOString().slice(0,16));
  const [to,setTo]=useState(()=>new Date().toISOString().slice(0,16));
  const [page,setPage]=useState<Page>();const [cursor,setCursor]=useState<string>();
  const [busy,setBusy]=useState(false);const [error,setError]=useState<string>();
  const request=useRef<AbortController>();const lock=useRef(false);
  useEffect(()=>()=>request.current?.abort(),[]);
  const clear=()=>{setPage(undefined);setCursor(undefined);setError(undefined);};
  const load=async(after?:string)=>{
    if(lock.current)return;lock.current=true;setBusy(true);setError(undefined);
    request.current?.abort();const controller=new AbortController();request.current=controller;
    let timedOut=false;const timer=window.setTimeout(()=>{timedOut=true;controller.abort();},25_000);
    try{
      const params=new URLSearchParams({branchId:branch,storeId:store,fromUtc:new Date(from+"Z").toISOString(),toUtc:new Date(to+"Z").toISOString(),limit:"50"});
      if(after)params.set("cursor",after);
      const result=await api.request<Page>(`/bff/customer/reports/cash-close?${params}`,{signal:controller.signal});
      if(!controller.signal.aborted){setPage(result);setCursor(after);}
    }catch{if(!controller.signal.aborted||timedOut){setPage(undefined);setError("Report unavailable. Check your connection, store access and date range, then reload.");}}
    finally{window.clearTimeout(timer);if(!controller.signal.aborted||timedOut){lock.current=false;setBusy(false);}}
  };
  const span=Date.parse(to+"Z")-Date.parse(from+"Z");
  return <Space direction="vertical" size="middle" style={{width:"100%"}}>
    <Typography.Title level={2}>Cash-close report</Typography.Title>
    <Alert type="info" message="Read-only snapshots of closed cash sessions. Delivery can lag or be incomplete. Capture and projection times describe each row; they do not prove the store is fully synchronized. Use POS Cash Review for current decisions."/>
    <Form layout="vertical" onFinish={()=>void load()}>
      <Space wrap>
        <Form.Item label="Branch UUID"><Input aria-label="Report branch UUID" disabled={busy} value={branch} onChange={e=>{clear();setBranch(e.target.value.trim());}}/></Form.Item>
        <Form.Item label="Store UUID"><Input aria-label="Report store UUID" disabled={busy} value={store} onChange={e=>{clear();setStore(e.target.value.trim());}}/></Form.Item>
        <Form.Item label="From (UTC, inclusive)"><Input aria-label="Report from UTC" type="datetime-local" disabled={busy} value={from} onChange={e=>{clear();setFrom(e.target.value);}}/></Form.Item>
        <Form.Item label="To (UTC, exclusive)"><Input aria-label="Report to UTC" type="datetime-local" disabled={busy} value={to} onChange={e=>{clear();setTo(e.target.value);}}/></Form.Item>
      </Space>
      <Button htmlType="submit" loading={busy} disabled={!uuid.test(branch)||!uuid.test(store)||!(span>0&&span<=31*86400000)}>Load cash-close report</Button>
    </Form>
    <Typography.Text>One store, at most 31 days, newest closure first. Amounts retain each row's currency; no cross-currency totals are calculated.</Typography.Text>
    {error&&<Alert role="status" type="error" message={error}/>}
    {page&&<>
      {!page.items.length&&<Alert type="info" message="No projected sessions in this page/range. This does not confirm that all cash sessions have arrived."/>}
      <Table rowKey={row=>row.snapshot.sessionId} dataSource={page.items} pagination={false} scroll={{x:1500}} columns={[
        {title:"Cash session",render:(_,row)=>row.snapshot.sessionId},
        {title:"Closed (UTC)",render:(_,row)=>row.snapshot.closedAtUtc},
        {title:"Currency",render:(_,row)=>row.snapshot.currency},
        {title:"Expected",render:(_,row)=>row.snapshot.expectedAmount},
        {title:"Counted",render:(_,row)=>row.snapshot.countedAmount},
        {title:"Variance",render:(_,row)=>row.snapshot.varianceAmount},
        {title:"Review",render:(_,row)=>row.snapshot.reviewStatus},
        {title:"Financial / review version",render:(_,row)=>`${row.snapshot.financialVersion} / ${row.snapshot.reviewVersion}`},
        {title:"Captured (UTC)",render:(_,row)=>row.snapshot.capturedAtUtc},
        {title:"Projected (UTC)",dataIndex:"projectedAtUtc"},
      ]}/>
      <Space><Button disabled={busy||!cursor} onClick={()=>void load()}>First report page</Button>
        <Button disabled={busy||!page.nextCursor} onClick={()=>void load(page.nextCursor)}>Next report page</Button></Space>
    </>}
  </Space>;
}
