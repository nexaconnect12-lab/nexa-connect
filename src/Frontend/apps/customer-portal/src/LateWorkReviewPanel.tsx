import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Descriptions,List,Select,Space,Typography} from "antd";
import {ApiError,createApiClient} from "@nexaconnect/api-client";
import {LateDetail,LatePage,LateScope,ReviewCommand,readLateDetail,readLatePage,readLateResult} from "./lateWork";
import {CashCorrectionPanel} from "./CashCorrectionPanel";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function LateWorkReviewPanel({scope,businessDate}:{scope:LateScope;businessDate:string}){
 const [source,setSource]=useState("POS"),[page,setPage]=useState<LatePage>(),[detail,setDetail]=useState<LateDetail>(),[pending,setPending]=useState<ReviewCommand>(),[busy,setBusy]=useState(false),[error,setError]=useState<string>();
 const [postingPending,setPostingPending]=useState(false);
 const generation=useRef(0),active=useRef<AbortController>();
 useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
 const execute=async(action:"list"|"detail"|"review",workId?:string,cursor?:string,command?:ReviewCommand)=>{
  const run=++generation.current;active.current?.abort();const controller=new AbortController();active.current=controller;setBusy(true);setError(undefined);const timer=setTimeout(()=>controller.abort(),40_000);
  const path="/bff/customer/day-close-late-work",query=new URLSearchParams({branchId:scope.window.branchId,businessDate,source});if(cursor)query.set("cursor",cursor);
  try{
   const csrf=action==="review"?await api.request<{requestToken:string}>(path+"/csrf",{signal:controller.signal,cache:"no-store"}):undefined;
   if(generation.current!==run)return;
   const raw=await api.request<unknown>(action==="review"?path:`${path}${action==="detail"?`/${workId}`:""}?${query}`,{method:action==="review"?"POST":"GET",body:action==="review"?{branchId:scope.window.branchId,businessDate,source,workId:command!.workId,command}:undefined,headers:csrf?{"X-Nexa-CSRF":csrf.requestToken}:undefined,signal:controller.signal,cache:"no-store"});
   if(generation.current!==run)return;
   if(action==="list"){setPage(readLatePage(raw,scope));setDetail(undefined);}
   else if(action==="detail")setDetail(readLateDetail(raw,scope,workId!));
   else{setDetail(readLateResult(raw,scope,command!));setPending(undefined);setPage(undefined);}
  }catch(e){if(generation.current===run){if(action==="review"&&e instanceof ApiError&&[400,403,404,409].includes(e.status)){setPending(undefined);setDetail(undefined);setError("Review rejected. Load current work before deciding again.");}else setError("Review response unavailable or changed. Retry the same decision if its result is uncertain.");}}
  finally{clearTimeout(timer);if(generation.current===run)setBusy(false);}
 };
 const review=(decision:string,reasonCode:string)=>{if(!detail?.canReview||busy||pending||postingPending)return;const command={workId:detail.item.workId,operationId:crypto.randomUUID(),expectedVersion:detail.item.version,decision,reasonCode};setPending(command);void execute("review",undefined,undefined,command);};
 return <Space direction="vertical" style={{width:"100%"}}><Typography.Title level={3}>Late financial work review</Typography.Title>
  <Alert type="info" message="Review decisions preserve settlement receipts" description="Investigate retained work, require a correction or record that evidence was checked. POS late cash has a separate verified posting workflow."/>
  <Space><Select aria-label="Late work source" value={source} disabled={busy||!!pending||postingPending} options={["Order","Payment","POS"].map(value=>({value,label:value}))} onChange={value=>{setSource(value);setPage(undefined);setDetail(undefined);}}/>
   <Button disabled={busy||!!pending||postingPending} onClick={()=>void execute("list")}>Load late work</Button>
   {pending&&<Button disabled={busy} onClick={()=>void execute("review",undefined,undefined,pending)}>Retry same review decision</Button>}</Space>
  {error&&<Alert role="status" type="error" message={error}/>}
  {page&&<><List locale={{emptyText:"No retained work for this source and settlement"}} dataSource={page.items} renderItem={item=><List.Item><Space>{item.eventType}: {item.status}<Button disabled={busy||!!pending||postingPending} onClick={()=>void execute("detail",item.workId)}>View retained work</Button></Space></List.Item>}/>
   {page.nextCursor&&<Button disabled={busy||!!pending||postingPending} onClick={()=>void execute("list",undefined,page.nextCursor!)}>Next page</Button>}</>}
  {detail&&<><Descriptions bordered column={1}><Descriptions.Item label="Work identifier">{detail.item.workId}</Descriptions.Item><Descriptions.Item label="Status">{detail.item.status} (version {detail.item.version})</Descriptions.Item><Descriptions.Item label="Custody reason">Late delivery for a settled day</Descriptions.Item><Descriptions.Item label="Received">{detail.item.receivedAtUtc}</Descriptions.Item>
   {Object.entries(detail.item.records).map(([key,id])=><Descriptions.Item key={key} label={key}>{id}</Descriptions.Item>)}<Descriptions.Item label="Settlement links"><a href={`#settlement-${scope.settlementId}`} onClick={event=>{event.preventDefault();document.getElementById(`settlement-${scope.settlementId}`)?.scrollIntoView({behavior:"smooth",block:"center"});}}>View current settlement</a>{detail.settlementLinks.map(id=><div key={id}>{id}</div>)}</Descriptions.Item></Descriptions>
   {detail.canReview&&<Space wrap><Button disabled={busy||!!pending||postingPending} onClick={()=>review("investigate","investigate_delivery")}>Investigate delivery</Button><Button disabled={busy||!!pending||postingPending} onClick={()=>review("require_correction","correction_needed")}>Require correction</Button><Button disabled={busy||!!pending||postingPending} onClick={()=>review("acknowledge","evidence_checked")}>Evidence checked</Button></Space>}
   <List header="Review history" dataSource={detail.history} renderItem={h=><List.Item>Version {h.version}: {h.status}; {h.reasonCode}; {h.reviewedAtUtc}</List.Item>}/>{detail.historyTruncated&&<Alert type="info" message="Showing the latest 20 decisions"/>}
   {source==="POS"&&!pending&&<CashCorrectionPanel key={detail.item.workId} scope={scope} workId={detail.item.workId} businessDate={businessDate} onPending={setPostingPending}/>}</>}
 </Space>;
}
