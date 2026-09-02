export async function setProxyEnabled(control,enabled,fetchImpl=fetch){
  const response=await fetchImpl(`${control.url}/proxies/${encodeURIComponent(control.proxyName)}`,{
    method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({enabled}),redirect:"error"
  });
  if(!response.ok)throw new Error(`Run-scoped fault control rejected the state change (${response.status}).`);
}

export async function setProcessRunning(control,service,running,fetchImpl=fetch){
  if(!["inventory","kitchen"].includes(service))throw new Error("Unsupported process-control target.");
  const response=await fetchImpl(`${control.url}/services/${service}`,{
    method:"POST",headers:{"Authorization":`Bearer ${control.token}`,"Content-Type":"application/json"},body:JSON.stringify({running}),redirect:"error"
  });
  if(!response.ok)throw new Error(`Run-scoped process control rejected the state change (${response.status}).`);
}
