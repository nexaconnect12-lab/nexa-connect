import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {repository,source,sameSource,sha} from './source.mjs';
import {catalog} from './next-release.mjs';

export const gates={unit:1390,architecture:7,http:136,hosting:47,'candidate-guards':22,'acceptance-guards':39,'media-profile-guards':1,'omise-profile-guards':1,'promptpay-profile-guards':1,'drawer-profile-guards':1,'frontend-unit':90,'frontend-check':1,'frontend-build':1,'audit-production':1,'audit-all':1,'qr-browser':75,'menu-photo-browser':34,'day-close-browser':8,'day-cutoff-browser':8,'day-seal-browser':22,'settlement-reporting-browser':7,'pos-build':1,'pos-print-layout':1,'pos-scanning-fixture':1,owning:13,'drawer-authority-owning':4};
export const phoneCases=['camera-scan-https','independent-guests-same-table','menu-cart-price-review','submit-refresh-original','staff-accept-kitchen-once','cash-original-order','manual-promptpay-original-order','switch-app-lock-resume','network-loss-original-recovery','qr-revocation-scope-denial','paid-receipt-pdf-reprint','cash-reporting-reconciliation'];
const pending=['clean-source-remote-ci','hosted-https-configuration','signed-matching-installer-and-hosting-artifacts','ios-safari-camera-acceptance','android-chrome-camera-acceptance','windows-pos-oidc-displays-pdf','dedicated-server-reboot-restore-monitoring','user-acceptance'];
const requireValue=(ok)=>{if(!ok)throw new Error('QR6 evidence is missing, changed, failed or incomplete.');};
function noLinks(file){for(let current=path.resolve(file);;current=path.dirname(current)){requireValue(!fs.existsSync(current)||!fs.lstatSync(current).isSymbolicLink());if(path.dirname(current)===current)break;}}
function read(file){noLinks(file);requireValue(fs.statSync(file).size<=4*1024*1024);return JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));}
const write=(file,value)=>fs.writeFileSync(file,JSON.stringify(value,null,2)+'\n',{flag:'wx',mode:0o600});
export function artifacts(root=repository){
 const result={};
 for(const [key,dir,include]of [
  ['customer','src/Frontend/apps/customer-portal/dist',()=>true],['owner','src/Frontend/apps/product-owner-portal/dist',()=>true],
  ['pos','src/Clients/NexaConnect.POS/bin/Debug/net10.0-windows',name=>/\.(dll|exe)$|\.(deps|runtimeconfig)\.json$/.test(name)]]){
  const entries=[];const base=path.join(root,dir);noLinks(base);requireValue(fs.existsSync(base));
  function visit(folder){for(const item of fs.readdirSync(folder,{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name))){const file=path.join(folder,item.name);requireValue(!item.isSymbolicLink());if(item.isDirectory())visit(file);else if(item.isFile()&&include(item.name))entries.push([path.relative(base,file).replaceAll('\\','/'),sha(fs.readFileSync(file))]);}}
  visit(base);entries.sort((a,b)=>a[0].localeCompare(b[0]));requireValue(entries.length>0);result[key]={fileCount:entries.length,sha256:sha(JSON.stringify(entries))};
 }
 return result;
}
export function assess(evidence,current,schemas,outputs){
 requireValue(evidence?.version===1&&evidence.kind==='qr6'&&evidence.passed===true&&evidence.productionVerified===false);
 requireValue(typeof evidence.runId==='string'&&/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/.test(evidence.runId));
 requireValue(sameSource(evidence.source,current)&&JSON.stringify(evidence.schemas)===JSON.stringify(schemas)&&JSON.stringify(evidence.artifacts)===JSON.stringify(outputs));
 const completed=Date.parse(evidence.completedAtUtc);requireValue(Number.isFinite(completed)&&completed<=Date.now()&&Date.now()-completed<86400000);
 const checks={};for(const [id,min]of Object.entries(gates)){const check=evidence.checks?.[id];requireValue(check?.passed===true&&Number.isInteger(check.count)&&check.count>=min);checks[id]={passed:true,count:check.count};}
 const owning=evidence.checks.owning;requireValue(owning.cleanupVerified===true&&owning.brokerRecoveryVerified===true&&/^[a-f0-9]{64}$/.test(owning.evidenceSha256));
 checks.owning={...checks.owning,cleanupVerified:true,brokerRecoveryVerified:true,evidenceSha256:owning.evidenceSha256};
 const drawer=evidence.checks['drawer-authority-owning'];requireValue(drawer.cleanupVerified===true&&/^[a-f0-9]{64}$/.test(drawer.evidenceSha256));
 checks['drawer-authority-owning']={...checks['drawer-authority-owning'],cleanupVerified:true,evidenceSha256:drawer.evidenceSha256};
 return {version:1,id:evidence.runId,status:'local-test-candidate',source:current,schemas,artifacts:outputs,checks,releaseReady:false,pending:[...pending]};
}
export function prepare(run){
 noLinks(run);const evidence=read(path.join(run,'verification.json'));
 const candidate=assess(evidence,source(),catalog(),artifacts());
 const own=read(path.join(run,'owning.json'));requireValue(own.passed===true&&own.caseCount>=11&&own.cleanupVerified===true&&own.brokerRecoveryVerified===true&&own.productionVerified===false);
 requireValue(sha(fs.readFileSync(path.join(run,'owning.json')))===candidate.checks.owning.evidenceSha256);
 const drawer=read(path.join(run,'drawer-owning.json'));requireValue(drawer.passed===true&&drawer.caseCount>=4&&drawer.cleanupVerified===true&&drawer.productionVerified===false);
 requireValue(sha(fs.readFileSync(path.join(run,'drawer-owning.json')))===candidate.checks['drawer-authority-owning'].evidenceSha256);
 candidate.verificationSha256=sha(fs.readFileSync(path.join(run,'verification.json')));
 candidate.binding=sha(JSON.stringify(candidate));
 write(path.join(run,'candidate.json'),candidate);
 write(path.join(run,'phone-test.json'),{version:1,candidateBinding:candidate.binding,kind:'operator-attestation-template',environmentId:null,
  devices:Object.fromEntries(['ios-safari','android-chrome'].map(device=>[device,{deviceModel:null,osVersion:null,browserVersion:null,cases:Object.fromEntries(phoneCases.map(id=>[id,{status:'pending',observedAtUtc:null,evidenceId:null}]))}])),releaseReady:false});
 return {id:candidate.id,binding:candidate.binding,releaseReady:false,pending:candidate.pending};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{const [command,run]=process.argv.slice(2);if(command==='artifacts')process.stdout.write(JSON.stringify(artifacts())+'\n');
 else if(command==='catalog')process.stdout.write(JSON.stringify(catalog())+'\n');
 else {requireValue(command==='prepare'&&typeof run==='string');process.stdout.write(JSON.stringify(prepare(path.resolve(run)))+'\n');}}
 catch{process.stderr.write('QR6 evidence validation failed; no private data emitted.\n');process.exitCode=1;}
}
