import {test} from "node:test";
import assert from "node:assert/strict";
import {readSettings} from "./settings.mjs";
import {completeEvidence} from "./safe-reporter.mjs";
const prefix="NEXACONNECT_REVIEW_LIVE_";
function configured(){
  const values={ENABLED:"1",CONFIRM_DISPOSABLE:"1",RUN_ID:"a".repeat(32),BASE_URL:"https://localhost:51829",OIDC_ISSUER:`http://localhost:18080/realms/nexa-review-it-${"a".repeat(32)}`,FAULT_CONTROL_URL:"http://localhost:18474",FAULT_PROXY_NAME:`nexa-review-it-${"a".repeat(32)}-inventory`,PROCESS_CONTROL_URL:"http://localhost:18475",PROCESS_CONTROL_TOKEN:"b".repeat(64),RESOLVER_USERNAME:"resolver",RESOLVER_PASSWORD:"synthetic-secret",READER_USERNAME:"reader",READER_PASSWORD:"synthetic-secret-two"};
  for(const [i,key] of ["ORGANIZATION_ID","OTHER_ORGANIZATION_ID","BRANCH_ID","CONCURRENCY_ORDER_ID","RESUME_ORDER_ID","VOID_ORDER_ID","OUTAGE_ORDER_ID","LOST_RESPONSE_ORDER_ID","INVENTORY_PROCESS_ORDER_ID","KITCHEN_PROCESS_ORDER_ID","COMBINED_PROCESS_ORDER_ID"].entries())values[key]=`${(i+1).toString(16)}`.repeat(8)+"-1111-1111-1111-111111111111";
  return Object.fromEntries(Object.entries(values).map(([key,value])=>[prefix+key,value]));
}
test("valid isolated live settings remain in memory",()=>{const settings=readSettings(configured());assert.equal(settings.fixtureReason,`browser-acceptance:${"a".repeat(32)}`);assert.equal(settings.baseURL,"https://localhost:51829");});
for(const [key,value] of [["ENABLED","0"],["CONFIRM_DISPOSABLE","0"],["RUN_ID","../unsafe"],["BASE_URL","https://example.com"],["BASE_URL","http://localhost:51829"],["BASE_URL","https://user:password@localhost:51829"],["BASE_URL","https://localhost:51829/?token=secret"],["OIDC_ISSUER","http://localhost:18080/realms/nexa-dev"],["OIDC_ISSUER",`https://example.com/realms/nexa-review-it-${"a".repeat(32)}`],["FAULT_CONTROL_URL","http://example.com:8474"],["FAULT_CONTROL_URL","https://localhost:8474"],["FAULT_PROXY_NAME","inventory"],["PROCESS_CONTROL_URL","http://example.com:8475"],["PROCESS_CONTROL_TOKEN","short"],["BRANCH_ID","invalid"],["READER_USERNAME","resolver"]]){
  test(`reject unsafe ${key}: case ${value.length}`,()=>{const env=configured();env[prefix+key]=value;assert.throws(()=>readSettings(env));});
}
test("missing password reports only setting name",()=>{const env=configured();delete env[prefix+"READER_PASSWORD"];assert.throws(()=>readSettings(env),error=>!error.message.includes("synthetic-secret")&&error.message.includes("READER_PASSWORD"));});
test("same tenant and reused review fixtures are rejected",()=>{let env=configured();env[prefix+"OTHER_ORGANIZATION_ID"]=env[prefix+"ORGANIZATION_ID"];assert.throws(()=>readSettings(env));env=configured();env[prefix+"VOID_ORDER_ID"]=env[prefix+"RESUME_ORDER_ID"];assert.throws(()=>readSettings(env));});
test("UUID normalization cannot disguise duplicate tenant or fixture IDs",()=>{
  const env=configured(),id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
  env[prefix+"CONCURRENCY_ORDER_ID"]=id;env[prefix+"RESUME_ORDER_ID"]=id.toUpperCase();assert.throws(()=>readSettings(env));
  env[prefix+"RESUME_ORDER_ID"]="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  env[prefix+"ORGANIZATION_ID"]=id;env[prefix+"OTHER_ORGANIZATION_ID"]=id.toUpperCase();assert.throws(()=>readSettings(env));
});
test("zero, partial, skipped and retried runs cannot claim complete evidence",()=>{
  const passes=Array.from({length:10},(_,i)=>({title:`scenario-${i}`,status:"passed"}));
  assert.equal(completeEvidence("passed",passes),true);
  for(const results of [[],passes.slice(1),[...passes,{status:"passed"}],[...passes.slice(1),{status:"skipped"}],Array(10).fill(passes[0])])assert.equal(completeEvidence("passed",results),false);
  assert.equal(completeEvidence("failed",passes),false);
});
test("process control is token protected and target allow-listed",async()=>{
  const settings=readSettings(configured());let request;
  const {setProcessRunning}=await import("./fault-control.mjs");
  await setProcessRunning(settings.processControl,"kitchen",false,async(url,options)=>{request={url,options};return {ok:true,status:200};});
  assert.equal(request.url,`${settings.processControl.url}/services/kitchen`);
  assert.equal(request.options.headers.Authorization,`Bearer ${settings.processControl.token}`);
  assert.deepEqual(JSON.parse(request.options.body),{running:false});
  await assert.rejects(()=>setProcessRunning(settings.processControl,"order",false));
});
test("fault control sends only the scoped proxy state",async()=>{
  const settings=readSettings(configured());let request;
  const {setProxyEnabled}=await import("./fault-control.mjs");
  await setProxyEnabled(settings.faultControl,false,async(url,options)=>{request={url,options};return {ok:true,status:200};});
  assert.equal(request.url,`${settings.faultControl.url}/proxies/${settings.faultControl.proxyName}`);
  assert.deepEqual(JSON.parse(request.options.body),{enabled:false});
});
