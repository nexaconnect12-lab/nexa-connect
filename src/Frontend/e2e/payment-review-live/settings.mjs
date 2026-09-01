const prefix="NEXACONNECT_REVIEW_LIVE_";
const uuid=/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
export function readSettings(env){
  const required=["ENABLED","CONFIRM_DISPOSABLE","RUN_ID","BASE_URL","OIDC_ISSUER","RESOLVER_USERNAME","RESOLVER_PASSWORD","READER_USERNAME","READER_PASSWORD","ORGANIZATION_ID","OTHER_ORGANIZATION_ID","BRANCH_ID","CONCURRENCY_ORDER_ID","RESUME_ORDER_ID","VOID_ORDER_ID","LOST_RESPONSE_ORDER_ID"];
  for(const key of required)if(!env[prefix+key])throw new Error(`Missing live acceptance setting: ${prefix+key}. Inject it without printing its value.`);
  if(env[prefix+"ENABLED"]!=="1"||env[prefix+"CONFIRM_DISPOSABLE"]!=="1")throw new Error("Live acceptance requires explicit enablement and disposable-target confirmation.");
  const read=key=>key.endsWith("_ID")&&key!=="RUN_ID"?env[prefix+key].toLowerCase():env[prefix+key];
  if(!/^[a-f0-9]{32}$/.test(read("RUN_ID")))throw new Error("Live acceptance run ID must be 32 lowercase hexadecimal characters.");
  const local=url=>["localhost","127.0.0.1","[::1]"].includes(url.hostname)&&!url.username&&!url.password&&!url.search&&!url.hash;
  let base,issuer;
  try{base=new URL(read("BASE_URL"));issuer=new URL(read("OIDC_ISSUER"));}catch{throw new Error("Invalid live acceptance endpoint URL.");}
  if(!local(base)||base.protocol!=="https:"||base.pathname!=="/")throw new Error("Live acceptance BFF must be a loopback HTTPS origin.");
  if(!local(issuer)||!["http:","https:"].includes(issuer.protocol)||issuer.pathname!==`/realms/nexa-review-it-${read("RUN_ID")}`)throw new Error("Live acceptance requires the run-specific loopback Keycloak realm.");
  for(const key of required.filter(x=>x.endsWith("_ID")&&x!=="RUN_ID"))if(!uuid.test(read(key)))throw new Error(`Invalid UUID setting: ${prefix+key}.`);
  if(read("ORGANIZATION_ID")===read("OTHER_ORGANIZATION_ID"))throw new Error("Tenant isolation requires two different organizations.");
  if(read("READER_USERNAME")===read("RESOLVER_USERNAME"))throw new Error("Read-only and resolver identities must differ.");
  const orderIds=["CONCURRENCY_ORDER_ID","RESUME_ORDER_ID","VOID_ORDER_ID","LOST_RESPONSE_ORDER_ID"].map(read);
  if(new Set(orderIds).size!==orderIds.length)throw new Error("Each mutating scenario requires its own fresh review fixture.");
  return {runId:read("RUN_ID"),baseURL:base.origin,issuer:issuer.href,organizationId:read("ORGANIZATION_ID"),otherOrganizationId:read("OTHER_ORGANIZATION_ID"),branchId:read("BRANCH_ID"),resolver:{username:read("RESOLVER_USERNAME"),password:read("RESOLVER_PASSWORD")},reader:{username:read("READER_USERNAME"),password:read("READER_PASSWORD")},orders:{concurrency:orderIds[0],resume:orderIds[1],void:orderIds[2],lost:orderIds[3]},fixtureReason:`browser-acceptance:${read("RUN_ID")}`};
}
