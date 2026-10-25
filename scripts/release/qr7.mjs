import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import {randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {source,sha} from './source.mjs';
import {catalog} from './next-release.mjs';
import {assess,artifacts,phoneCases} from './qr6.mjs';
const requireValue=ok=>{if(!ok)throw new Error('QR7 input or binding is missing, changed or invalid.');};
export const uuid=v=>typeof v==='string'&&/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/.test(v);
export const read=file=>{for(let p=path.resolve(file);;p=path.dirname(p)){requireValue(!fs.lstatSync(p).isSymbolicLink());if(path.dirname(p)===p)break;}requireValue(fs.statSync(file).size<=4*1024*1024);return JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));};
export const write=(file,value)=>fs.writeFileSync(file,JSON.stringify(value,null,2)+'\n',{flag:'wx',mode:0o600});
export function currentCandidate(dir){
 const candidate=read(path.join(dir,'candidate.json'));const proof=read(path.join(dir,'verification.json'));
 assess(proof,source(),catalog(),artifacts());
 const {binding,...original}=candidate;requireValue(binding===sha(JSON.stringify(original))&&candidate.verificationSha256===sha(fs.readFileSync(path.join(dir,'verification.json'))));
 const owning=read(path.join(dir,'owning.json'));requireValue(owning.passed&&owning.cleanupVerified&&owning.brokerRecoveryVerified&&owning.productionVerified===false&&sha(fs.readFileSync(path.join(dir,'owning.json')))===proof.checks.owning.evidenceSha256);
 requireValue(candidate.id===proof.runId&&JSON.stringify(candidate.source)===JSON.stringify(proof.source));return candidate;
}
export function privateIPv4(value){if(typeof value!=='string'||net.isIP(value)!==4)return false;const b=value.split('.').map(Number);return b[0]===10||b[0]===172&&b[1]>=16&&b[1]<=31||b[0]===192&&b[1]===168;}
export function origin(value){try{const u=new URL(value);return typeof value==='string'&&value.length<=256&&u.protocol==='https:'&&u.origin===value&&u.pathname==='/'&&!u.username&&!u.password&&!u.search&&!u.hash&&Number(u.port)>=1024&&u.hostname!=='localhost'&&!u.hostname.endsWith('.localhost')&&(net.isIP(u.hostname)===0||privateIPv4(u.hostname));}catch{return false;}}
export function blockers(plan){
 requireValue(plan?.version===1&&/^[a-f0-9]{32}$/.test(plan.id)&&/^[a-f0-9]{32}$/.test(plan.stackRunId)&&/^[a-f0-9]{64}$/.test(plan.candidateBinding));
 return [...!origin(plan.publicOrigin)?['trusted-https-origin']:[],...!privateIPv4(plan.listenAddress)?['pc-private-ipv4']:[],...['windows','ios-safari','android-chrome'].filter(k=>plan.trust?.[k]!==true).map(k=>'certificate-trust:'+k),...['ios-safari','android-chrome'].filter(k=>!['model','osVersion','browserVersion'].every(f=>typeof plan.devices?.[k]?.[f]==='string'&&plan.devices[k][f].trim().length>0&&plan.devices[k][f].length<=100&&!/[\x00-\x1f\x7f]/.test(plan.devices[k][f]))).map(k=>'device-facts:'+k)];
}
export const safeFile=name=>/^(?:certificate\.pem|private-key\.pem|client-ca\.cer|edge\/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*\/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*\.[A-Za-z0-9]+)$/.test(name)&&!name.includes('..');
export function initialize(run,candidateDirectory){
 const candidate=currentCandidate(candidateDirectory);const id=path.basename(run);requireValue(/^[a-f0-9]{32}$/.test(id));
 const plan={version:1,id,candidateDirectory:path.resolve(candidateDirectory),candidateBinding:candidate.binding,stackRunId:randomUUID().replaceAll('-',''),publicOrigin:null,listenAddress:null,trust:{windows:false,'ios-safari':false,'android-chrome':false},devices:{'ios-safari':{model:null,osVersion:null,browserVersion:null},'android-chrome':{model:null,osVersion:null,browserVersion:null}}};
 write(path.join(run,'plan.json'),plan);write(path.join(run,'acceptance.json'),{version:1,pilotId:id,candidateBinding:candidate.binding,kind:'operator-attestations',releaseReady:false});return report(run);
}
export function report(run){
 const plan=read(path.join(run,'plan.json'));let missing=blockers(plan);
 try{requireValue(currentCandidate(plan.candidateDirectory).binding===plan.candidateBinding);}catch{missing.push('fresh-matching-candidate');}
 for(const name of ['certificate.pem','private-key.pem','client-ca.cer'])if(!fs.existsSync(path.join(run,name)))missing.push('certificate-file:'+name);
 const activationFile=path.join(run,'activation.json');let activation=null;
 if(fs.existsSync(activationFile)){activation=read(activationFile);const {binding,...value}=activation;requireValue(binding===sha(JSON.stringify(value))&&activation.candidateBinding===plan.candidateBinding&&activation.planSha256===sha(fs.readFileSync(path.join(run,'plan.json'))));
  for(const [name,hash]of Object.entries(activation.files)){requireValue(safeFile(name));const file=path.join(run,name);try{for(let p=path.resolve(file);;p=path.dirname(p)){requireValue(!fs.lstatSync(p).isSymbolicLink());if(path.dirname(p)===p)break;}requireValue(sha(fs.readFileSync(file))===hash);}catch{missing.push('configured-files-changed');break;}}
 }
 const records=[];const dir=path.join(run,'observations');if(fs.existsSync(dir))for(const f of fs.readdirSync(dir).filter(f=>f.endsWith('.json')).sort()){
  const r=read(path.join(dir,f));requireValue(r.pilotId===plan.id&&r.candidateBinding===plan.candidateBinding&&r.activationBinding===activation?.binding&&['ios-safari','android-chrome'].includes(r.device)&&phoneCases.includes(r.caseId)&&['passed','failed','blocked'].includes(r.result)&&uuid(r.evidenceId)&&uuid(r.id)&&Number.isFinite(Date.parse(r.observedAtUtc))&&Date.parse(r.observedAtUtc)<=Date.now());records.push(r);
 }
 const cases=Object.fromEntries(['ios-safari','android-chrome'].map(device=>[device,Object.fromEntries(phoneCases.map(caseId=>{const r=records.filter(r=>r.device===device&&r.caseId===caseId).sort((a,b)=>Date.parse(a.observedAtUtc)-Date.parse(b.observedAtUtc)).at(-1);return [caseId,r?{status:r.result,evidenceId:r.evidenceId,observedAtUtc:r.observedAtUtc}:{status:'pending'}];}))]));
 return {version:1,pilotId:plan.id,candidateBinding:plan.candidateBinding,configured:!!activation,prerequisites:missing,kind:'operator-attestations',cases,releaseReady:false,productionVerified:false};
}
export function record(run,device,caseId,result,evidenceId,confirmed){
 const plan=read(path.join(run,'plan.json'));const activation=read(path.join(run,'activation.json'));requireValue(report(run).prerequisites.length===0);
 requireValue(confirmed==='observed'&&activation.candidateBinding===plan.candidateBinding&&['ios-safari','android-chrome'].includes(device)&&phoneCases.includes(caseId)&&['passed','failed','blocked'].includes(result)&&uuid(evidenceId));
 const dir=path.join(run,'observations');fs.mkdirSync(dir,{recursive:true,mode:0o700});
 const value={version:1,id:randomUUID(),pilotId:plan.id,candidateBinding:plan.candidateBinding,activationBinding:activation.binding,device,caseId,result,evidenceId,observedAtUtc:new Date().toISOString()};write(path.join(dir,value.id+'.json'),value);return {recorded:true,releaseReady:false};
}
export function activate(run,input){
 const plan=read(path.join(run,'plan.json'));requireValue(blockers(plan).length===0&&currentCandidate(plan.candidateDirectory).binding===plan.candidateBinding);
 requireValue(input.stackRunId===plan.stackRunId&&Object.values(input.scope??{}).length===5&&Object.values(input.scope).every(uuid)
   &&/^https:\/\/localhost:\d+$/.test(input.backendOrigin)&&/^[a-f0-9]{64}$/.test(input.backendCertificateSha256)&&/^[a-f0-9]{64}$/.test(input.backendArtifacts)
   &&Object.values(input.files??{}).length>=6&&Object.entries(input.files).every(([k,v])=>safeFile(k)&&typeof v==='string'&&/^[a-f0-9]{64}$/.test(v)));
 const value={version:1,id:randomUUID(),pilotId:plan.id,candidateBinding:plan.candidateBinding,planSha256:sha(fs.readFileSync(path.join(run,'plan.json'))),stackRunId:plan.stackRunId,
  scope:input.scope,backendOrigin:input.backendOrigin,backendCertificateSha256:input.backendCertificateSha256,backendArtifacts:input.backendArtifacts,files:input.files,createdAtUtc:new Date().toISOString(),kind:'configured-local-pilot',releaseReady:false};
 value.binding=sha(JSON.stringify(value));write(path.join(run,'activation.json'),value);return {configured:true,binding:value.binding,releaseReady:false};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{const [command,run,...args]=process.argv.slice(2);let result;if(command==='init')result=initialize(run,args[0]);else if(command==='record')result=record(run,...args);else if(command==='report')result=report(run);else if(command==='candidate')result=currentCandidate(run);else if(command==='activate')result=activate(run,read(args[0]));else throw new Error();process.stdout.write(JSON.stringify(result)+'\n');}
 catch{process.stderr.write('QR7 validation failed; no private details emitted.\n');process.exitCode=1;}
}
