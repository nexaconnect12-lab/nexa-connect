const prefix="NEXACONNECT_REVIEW_LIVE_";
const uuid=/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
export function readSettings(env){
  const required=["ENABLED","CONFIRM_DISPOSABLE","RUN_ID","BASE_URL","OIDC_ISSUER","FAULT_CONTROL_URL","FAULT_PROXY_NAME","PROCESS_CONTROL_URL","PROCESS_CONTROL_TOKEN","RESOLVER_USERNAME","RESOLVER_PASSWORD","READER_USERNAME","READER_PASSWORD","ORGANIZATION_ID","OTHER_ORGANIZATION_ID","BRANCH_ID","CONCURRENCY_ORDER_ID","RESUME_ORDER_ID","VOID_ORDER_ID","OUTAGE_ORDER_ID","LOST_RESPONSE_ORDER_ID","INVENTORY_PROCESS_ORDER_ID","KITCHEN_PROCESS_ORDER_ID","COMBINED_PROCESS_ORDER_ID"];
  for(const key of required)if(!env[prefix+key])throw new Error(`Missing live acceptance setting: ${prefix+key}. Inject it without printing its value.`);
  if(env[prefix+"ENABLED"]!=="1"||env[prefix+"CONFIRM_DISPOSABLE"]!=="1")throw new Error("Live acceptance requires explicit enablement and disposable-target confirmation.");
  const read=key=>key.endsWith("_ID")&&key!=="RUN_ID"?env[prefix+key].toLowerCase():env[prefix+key];
  if(!/^[a-f0-9]{32}$/.test(read("RUN_ID")))throw new Error("Live acceptance run ID must be 32 lowercase hexadecimal characters.");
  const local=url=>["localhost","127.0.0.1","[::1]"].includes(url.hostname)&&!url.username&&!url.password&&!url.search&&!url.hash;
  let base,issuer,faultControl,processControl;
  try{base=new URL(read("BASE_URL"));issuer=new URL(read("OIDC_ISSUER"));faultControl=new URL(read("FAULT_CONTROL_URL"));processControl=new URL(read("PROCESS_CONTROL_URL"));}catch{throw new Error("Invalid live acceptance endpoint URL.");}
  if(!local(base)||base.protocol!=="https:"||base.pathname!=="/")throw new Error("Live acceptance BFF must be a loopback HTTPS origin.");
  if(!local(issuer)||!["http:","https:"].includes(issuer.protocol)||issuer.pathname!==`/realms/nexa-review-it-${read("RUN_ID")}`)throw new Error("Live acceptance requires the run-specific loopback Keycloak realm.");
  if(!local(faultControl)||faultControl.protocol!=="http:"||faultControl.pathname!=="/")throw new Error("Fault control must be an unauthenticated loopback HTTP origin with no path.");
  if(!local(processControl)||processControl.protocol!=="http:"||processControl.pathname!=="/")throw new Error("Process control must be a loopback HTTP origin with no path.");
  if(!/^[a-f0-9]{64}$/.test(read("PROCESS_CONTROL_TOKEN")))throw new Error("Process control token is invalid.");
  if(read("FAULT_PROXY_NAME")!==`nexa-review-it-${read("RUN_ID")}-inventory`)throw new Error("Fault proxy name must be scoped to the live acceptance run.");
  for(const key of required.filter(x=>x.endsWith("_ID")&&x!=="RUN_ID"))if(!uuid.test(read(key)))throw new Error(`Invalid UUID setting: ${prefix+key}.`);
  if(read("ORGANIZATION_ID")===read("OTHER_ORGANIZATION_ID"))throw new Error("Tenant isolation requires two different organizations.");
  if(read("READER_USERNAME")===read("RESOLVER_USERNAME"))throw new Error("Read-only and resolver identities must differ.");
  const orderIds=["CONCURRENCY_ORDER_ID","RESUME_ORDER_ID","VOID_ORDER_ID","OUTAGE_ORDER_ID","LOST_RESPONSE_ORDER_ID","INVENTORY_PROCESS_ORDER_ID","KITCHEN_PROCESS_ORDER_ID","COMBINED_PROCESS_ORDER_ID"].map(read);
  if(new Set(orderIds).size!==orderIds.length)throw new Error("Each mutating scenario requires its own fresh review fixture.");
  return {runId:read("RUN_ID"),baseURL:base.origin,issuer:issuer.href,faultControl:{url:faultControl.origin,proxyName:read("FAULT_PROXY_NAME")},processControl:{url:processControl.origin,token:read("PROCESS_CONTROL_TOKEN")},organizationId:read("ORGANIZATION_ID"),otherOrganizationId:read("OTHER_ORGANIZATION_ID"),branchId:read("BRANCH_ID"),resolver:{username:read("RESOLVER_USERNAME"),password:read("RESOLVER_PASSWORD")},reader:{username:read("READER_USERNAME"),password:read("READER_PASSWORD")},orders:{concurrency:orderIds[0],resume:orderIds[1],void:orderIds[2],outage:orderIds[3],lost:orderIds[4],inventoryProcess:orderIds[5],kitchenProcess:orderIds[6],combinedProcess:orderIds[7]},fixtureReason:`browser-acceptance:${read("RUN_ID")}`};
}
