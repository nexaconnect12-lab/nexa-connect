import React,{useEffect,useRef,useState} from "react";
import {Alert,Button,Checkbox,Descriptions,Space,Typography} from "antd";
import {ApiError,createApiClient} from "@nexaconnect/api-client";
import {LateScope} from "./lateWork";
import {CorrectionCommand,CorrectionView,readCorrection} from "./cashCorrection";
const api=createApiClient({onUnauthorized:()=>location.assign("/bff/customer/login")});
export function CashCorrectionPanel({scope,workId,businessDate,onPending}:{scope:LateScope;workId:string;businessDate:string;onPending:(pending:boolean)=>void}){
 const [value,setValue]=useState<CorrectionView>(),[pending,setPending]=useState<CorrectionCommand>(),[reviewed,setReviewed]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState<string>();const generation=useRef(0),active=useRef<AbortController>();
 useEffect(()=>()=>{generation.current++;active.current?.abort();},[]);
 const execute=async(post:boolean)=>{
  let command=pending;if(post&&!command){if(!value?.canPost||!value.preview||!reviewed)return;command={workId,operationId:crypto.randomUUID(),expectedReviewVersion:value.preview.reviewVersion,previewFingerprint:value.preview.fingerprint};setPending(command);onPending(true);}
  const run=++generation.current;active.current?.abort();const controller=new AbortController();active.current=controller;setBusy(true);setError(undefined);if(!post){setValue(undefined);setReviewed(false);}const timer=setTimeout(()=>controller.abort(),40_000);
  try{const path="/bff/customer/late-cash-corrections",csrf=post?await api.request<{requestToken:string}>(path+"/csrf",{signal:controller.signal,cache:"no-store"}):undefined;if(generation.current!==run)return;
   const raw=await api.request<unknown>(post?path:`${path}?${new URLSearchParams({branchId:scope.window.branchId,businessDate,workId})}`,{method:post?"POST":"GET",body:post?{branchId:scope.window.branchId,businessDate,workId,command}:undefined,headers:csrf?{"X-Nexa-CSRF":csrf.requestToken}:undefined,signal:controller.signal,cache:"no-store"});
   if(generation.current!==run)return;setValue(readCorrection(raw,scope,workId,post?command:undefined));if(post){setPending(undefined);onPending(false);setReviewed(false);}
  }catch(e){if(generation.current===run){if(post&&e instanceof ApiError&&[400,403,404,409].includes(e.status)){setPending(undefined);onPending(false);setValue(undefined);setReviewed(false);setError("Correction rejected. Reload current evidence before posting again.");}else setError("Correction response unavailable or changed. Retry the same posting operation if its result is uncertain.");}}
  finally{clearTimeout(timer);if(generation.current===run)setBusy(false);}
 };
 const p=value?.preview,r=value?.receipt;
 return <Space direction="vertical" style={{width:"100%"}}><Typography.Title level={4}>Late cash correction</Typography.Title><Alert type="info" message="Separate cash reconciliation adjustment" description="The server verifies original Order evidence and derives the adjustment. Posting preserves the closed drawer and original settlement. Sales and tender totals stay unchanged."/>
  <Space><Button disabled={busy||!!pending} onClick={()=>void execute(false)}>Load cash correction</Button>{pending&&<Button disabled={busy} onClick={()=>void execute(true)}>Retry same cash correction</Button>}</Space>
  {error&&<Alert role="status" type="error" message={error}/>}{value?.blocker&&<Alert type="info" message={value.blocker==="correction_review_required"?"Mark this work as requiring correction before preparing a posting.":"This retained work has no supported late-cash correction."}/>}
  {p&&<><Descriptions bordered column={1}><Descriptions.Item label="Proposed cash variance adjustment">{p.currency} {p.adjustment}</Descriptions.Item><Descriptions.Item label="Posting date">{p.postingDate} ({p.timeZone})</Descriptions.Item><Descriptions.Item label="Original order">{p.orderId}</Descriptions.Item><Descriptions.Item label="Original tender">{p.tenderId}</Descriptions.Item><Descriptions.Item label="Closed drawer">{p.drawerId}</Descriptions.Item><Descriptions.Item label="Reviewed decision version">{p.reviewVersion}</Descriptions.Item></Descriptions>
   {value?.canPost&&<><Checkbox disabled={busy||!!pending} checked={reviewed} onChange={e=>setReviewed(e.target.checked)}>I reviewed this adjustment and posting date</Checkbox><Button disabled={busy||!!pending||!reviewed} onClick={()=>void execute(true)}>Post reviewed cash correction</Button></>}</>}
  {r&&<Descriptions title="Immutable cash correction receipt" bordered column={1}><Descriptions.Item label="Correction identifier">{r.correctionId}</Descriptions.Item><Descriptions.Item label="Cash variance adjustment">{r.currency} {r.adjustment}</Descriptions.Item><Descriptions.Item label="Posting date">{r.postingDate}</Descriptions.Item><Descriptions.Item label="Posted at">{r.postedAtUtc}</Descriptions.Item><Descriptions.Item label="Original settlement">{r.scope.settlementId}</Descriptions.Item><Descriptions.Item label="Publication event">{r.eventId}</Descriptions.Item></Descriptions>}
 </Space>;
}
