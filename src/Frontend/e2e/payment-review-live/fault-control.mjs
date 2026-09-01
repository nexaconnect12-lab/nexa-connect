export async function setProxyEnabled(control,enabled,fetchImpl=fetch){
  const response=await fetchImpl(`${control.url}/proxies/${encodeURIComponent(control.proxyName)}`,{
    method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({enabled}),redirect:"error"
  });
  if(!response.ok)throw new Error(`Run-scoped fault control rejected the state change (${response.status}).`);
}
