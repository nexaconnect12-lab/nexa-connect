import { useEffect, useRef, useState } from "react";
import { Alert, Button, Card, Form, Input, Space, Tag, Typography } from "antd";
import { ApiError, createApiClient, type RequestOptions } from "@nexaconnect/api-client";

type Ticket = { ticketId:string; orderId:string; status:string; concurrencyVersion:number; queuedAtUtc:string;
  lines:Array<{productId:string; name:string; quantity:number; preparationStation:string}> };
type Page = { items:Ticket[]; nextCursor?:string; canTransition:boolean };
const api = createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
const base = "/bff/customer/kitchen";
const next:Record<string,{label:string;status:string}> = {
  Queued:{label:"Start",status:"InProgress"}, InProgress:{label:"Ready",status:"Ready"}, Ready:{label:"Complete",status:"Completed"},
};

async function request<T>(path:string, parent:AbortSignal, options:RequestOptions = {}):Promise<T> {
  const controller = new AbortController();
  const abort = () => controller.abort();
  parent.addEventListener("abort",abort);
  if(parent.aborted) controller.abort();
  const timer = window.setTimeout(abort,20_000);
  try { return await api.request<T>(path,{...options,signal:controller.signal}); }
  finally { window.clearTimeout(timer); parent.removeEventListener("abort",abort); }
}

export function KitchenQueuePanel() {
  const [branch,setBranch] = useState("");
  const [station,setStation] = useState("");
  const [active,setActive] = useState(false);
  const [page,setPage] = useState<Page>();
  const [cursor,setCursor] = useState<string>();
  const [busy,setBusy] = useState(false);
  const [stale,setStale] = useState(true);
  const [updated,setUpdated] = useState<number>();
  const [notice,setNotice] = useState<string>();
  const pending = useRef<AbortController>();
  const locked = useRef(false);
  useEffect(()=>()=>pending.current?.abort(),[]);

  const path = `${base}/branches/${branch}/tickets`;
  const begin = () => {
    if(locked.current) return undefined;
    locked.current=true;
    pending.current?.abort();
    const controller=new AbortController(); pending.current=controller;
    setBusy(true);setStale(true);
    return controller.signal;
  };
  const finish = (signal:AbortSignal) => {if(!signal.aborted){locked.current=false;setBusy(false);}};
  const fetchPage = async (signal:AbortSignal, after?:string) => {
    const params=new URLSearchParams({limit:"50"});
    if(station.trim())params.set("station",station.trim());
    if(after)params.set("cursor",after);
    const result=await request<Page>(`${path}?${params}`,signal);
    if(!signal.aborted){setPage(result);setCursor(after);setUpdated(Date.now());setStale(false);setActive(true);}
  };
  const load = async (after?:string) => {
    const signal=begin();if(!signal)return;
    try {await fetchPage(signal,after);}
    catch {if(!signal.aborted){setStale(true);setNotice("Queue unavailable. Actions are locked until a successful refresh. Check your connection and branch access.");}}
    finally {finish(signal);}
  };
  // Refresh the current page only; mutations are always explicit and never replayed.
  useEffect(()=>{
    if(!active)return;
    const timer=window.setInterval(()=>{if(!locked.current)void load(cursor);},10_000);
    return ()=>window.clearInterval(timer);
  });

  const transition = async (ticket:Ticket) => {
    const target=next[ticket.status];
    if(!target||stale||!page?.canTransition)return;
    const signal=begin();if(!signal)return;
    let message=`${target.label} saved.`;
    try {
      const csrf=await request<{requestToken:string}>(`${base}/csrf`,signal);
      await request(`${path}/${ticket.ticketId}/transitions`,signal,{method:"POST",headers:{"X-Nexa-CSRF":csrf.requestToken},
        body:{targetStatus:target.status,expectedConcurrencyVersion:ticket.concurrencyVersion}});
    } catch(error) {
      message=error instanceof ApiError&&error.status===409
        ? "Ticket changed. The action was not retried."
        : "Action outcome is unconfirmed. The action was not retried.";
    }
    if(signal.aborted)return;
    try {
      const authoritative=await request<Ticket>(`${path}/${ticket.ticketId}`,signal);
      message+=` Current ticket status: ${authoritative.status}.`;
      await fetchPage(signal);
    } catch {
      if(!signal.aborted)setStale(true);
      message+=" Refresh failed. Reload the queue before continuing.";
    } finally {if(!signal.aborted)setNotice(message);finish(signal);}
  };
  const clear = () => {setPage(undefined);setActive(false);setStale(true);setUpdated(undefined);setCursor(undefined);setNotice(undefined);};

  return <Space direction="vertical" size="middle" style={{width:"100%"}}>
    <Typography.Title level={2}>Kitchen queue</Typography.Title>
    <Alert type="info" message="Preparation can begin before payment settles. Kitchen completion does not confirm payment. A payment failure can cancel unfinished tickets; completed preparation needs operator investigation."/>
    <Form layout="inline" onFinish={()=>void load()}>
      <Form.Item label="Branch UUID"><Input aria-label="Kitchen branch UUID" value={branch} disabled={busy} onChange={e=>{clear();setBranch(e.target.value.trim());}}/></Form.Item>
      <Form.Item label="Station"><Input aria-label="Kitchen station" placeholder="All stations" maxLength={100} value={station} disabled={busy} onChange={e=>{clear();setStation(e.target.value);}}/></Form.Item>
      <Button htmlType="submit" disabled={busy||!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(branch)}>Refresh queue</Button>
    </Form>
    <Typography.Text>Use the branch UUID and station code supplied by your administrator. Tickets are oldest first, 50 per page. The current page refreshes every 10 seconds.</Typography.Text>
    {notice&&<Alert role="status" type="warning" message={notice}/>}
    {page&&<>
      <Alert type={stale?"warning":"success"} message={stale?"Queue is stale or refreshing. Actions are locked.":`Last refreshed ${new Date(updated!).toLocaleTimeString()}`}/>
      {!page.canTransition&&<Alert type="info" message="Read-only kitchen access."/>}
      {!page.items.length&&<Typography.Paragraph>No active tickets on this page.</Typography.Paragraph>}
      {page.items.map(ticket=><Card key={ticket.ticketId} title={`Order ${ticket.orderId}`} extra={<Tag>{ticket.status}</Tag>}>
        <Typography.Paragraph>Station: {ticket.lines[0]?.preparationStation} · Age: {Math.max(0,Math.floor((Date.now()-Date.parse(ticket.queuedAtUtc))/60000))} minutes</Typography.Paragraph>
        {ticket.lines.map((line,index)=><Typography.Paragraph key={index}>{line.quantity} × {line.name}</Typography.Paragraph>)}
        {next[ticket.status]&&<Button size="large" type="primary" disabled={busy||stale||!page.canTransition} onClick={()=>void transition(ticket)}>{next[ticket.status]!.label}</Button>}
      </Card>)}
      <Space>
        <Button disabled={busy||!cursor} onClick={()=>void load()}>First page</Button>
        <Button disabled={busy||stale||!page.nextCursor} onClick={()=>void load(page.nextCursor)}>Next page</Button>
      </Space>
    </>}
  </Space>;
}
