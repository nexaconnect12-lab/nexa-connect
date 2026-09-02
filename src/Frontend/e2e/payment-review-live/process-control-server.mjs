import http from "node:http";
import net from "node:net";
import {spawn} from "node:child_process";
import {closeSync,openSync} from "node:fs";

const token=process.env.NEXACONNECT_REVIEW_PROCESS_CONTROL_TOKEN;
const encoded=process.env.NEXACONNECT_REVIEW_PROCESS_CONTROL_CONFIG;
if(!token||!encoded)throw new Error("Process control configuration is required.");
const config=JSON.parse(Buffer.from(encoded,"base64").toString("utf8"));
const allowed=new Map(config.services.map(service=>[service.name,{...service,process:null}]));
if(allowed.size!==2||!allowed.has("inventory")||!allowed.has("kitchen"))throw new Error("Only Inventory and Kitchen may be process-controlled.");

function waitForPort(port,timeoutMs=15000){return new Promise((resolve,reject)=>{const deadline=Date.now()+timeoutMs;const probe=()=>{const socket=net.createConnection({host:"127.0.0.1",port});socket.once("connect",()=>{socket.destroy();resolve();});socket.once("error",()=>{socket.destroy();if(Date.now()>=deadline)reject(new Error("Controlled service did not become ready."));else setTimeout(probe,100);});};probe();});}
function waitForExit(child,timeoutMs){
  if(child.exitCode!==null)return Promise.resolve(true);
  return new Promise(resolve=>{const timer=setTimeout(()=>{child.off("exit",exited);resolve(child.exitCode!==null);},timeoutMs);const exited=()=>{clearTimeout(timer);resolve(true);};child.once("exit",exited);});
}
async function start(service){
  if(service.process&&!service.process.killed)return;
  const output=openSync(service.log,"a"),error=openSync(`${service.log}.error`,"a");
  const child=spawn("dotnet",[service.assembly,"--urls",service.url],{cwd:service.workingDirectory,env:{...process.env,...service.environment},windowsHide:true,stdio:["ignore",output,error]});
  closeSync(output);closeSync(error);
  service.process=child;
  await Promise.race([waitForPort(service.port),new Promise((_,reject)=>child.once("exit",code=>reject(new Error(`Controlled service exited during startup (${code}).`))) )]);
}
async function stop(service){
  const child=service.process;if(!child||child.exitCode!==null){service.process=null;return;}
  child.kill();if(!await waitForExit(child,5000)){child.kill("SIGKILL");if(!await waitForExit(child,5000))throw new Error("Controlled service did not exit.");}service.process=null;
}
async function stopAll(){for(const service of allowed.values())await stop(service);}

await Promise.all([...allowed.values()].map(start));
const server=http.createServer(async(req,res)=>{
  try{
    if(req.headers.authorization!==`Bearer ${token}`){res.writeHead(401).end();return;}
    if(req.method==="GET"&&req.url==="/health"){res.writeHead(200,{"Content-Type":"application/json"}).end('{"status":"ready"}');return;}
    if(req.method==="POST"&&req.url==="/shutdown"){await stopAll();res.writeHead(200).end();server.close(()=>process.exit(0));return;}
    const match=/^\/services\/(inventory|kitchen)$/.exec(req.url??"");if(req.method!=="POST"||!match){res.writeHead(404).end();return;}
    let body="";for await(const chunk of req){body+=chunk;if(body.length>64)throw new Error("Request too large.");}
    const state=JSON.parse(body);if(typeof state.running!=="boolean")throw new Error("Invalid process state.");
    const service=allowed.get(match[1]);if(state.running)await start(service);else await stop(service);
    res.writeHead(200,{"Content-Type":"application/json"}).end(JSON.stringify({name:match[1],running:state.running}));
  }catch{res.writeHead(400).end();}
});
server.listen(config.controlPort,"127.0.0.1");
for(const signal of ["SIGINT","SIGTERM"]){process.on(signal,async()=>{await stopAll();server.close(()=>process.exit(0));});}
