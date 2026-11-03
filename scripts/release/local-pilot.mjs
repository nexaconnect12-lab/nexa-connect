import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {read,blockers} from './qr7.mjs';
import {sha} from './source.mjs';
const requireValue=ok=>{if(!ok)throw Error('Same-scope QR refresh guard rejected the input.');};
export function refreshPlan(previous,activation,fresh,state,reference,previousSha256){
 const {binding,...value}=activation;
 requireValue(binding===sha(JSON.stringify(value))&&activation.pilotId===previous.id&&activation.planSha256===previousSha256);
 requireValue(previous.stackRunId===state.runId&&activation.stackRunId===state.runId&&state.status==='Ready'&&state.qrPilotOrigin===previous.publicOrigin);
 requireValue(!reference.recoveryLabAccessCompleted&&reference.runId===state.runId&&fresh.id!==previous.id&&fresh.version===1);
 requireValue(blockers(previous).length===0);
 for(const name of ['OrganizationId','RestaurantId','BranchId','StoreId','TerminalId'])requireValue(activation.scope[name]===reference[name[0].toLowerCase()+name.slice(1)]);
 return {...fresh,stackRunId:state.runId,publicOrigin:previous.publicOrigin,listenAddress:previous.listenAddress,trust:previous.trust,devices:previous.devices};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{
  const [oldDir,newDir,stateFile,referenceFile]=process.argv.slice(2);
  const previous=read(path.join(oldDir,'plan.json')),activation=read(path.join(oldDir,'activation.json')),fresh=read(path.join(newDir,'plan.json'));
  const next=refreshPlan(previous,activation,fresh,read(stateFile),read(referenceFile),sha(fs.readFileSync(path.join(oldDir,'plan.json'))));
  for(const name of ['certificate.pem','private-key.pem','client-ca.cer']){
   const file=path.join(oldDir,name);readPath(file);
   requireValue(activation.files[name]===sha(fs.readFileSync(file))&&!fs.existsSync(path.join(newDir,name)));
  }
  for(const name of ['certificate.pem','private-key.pem','client-ca.cer'])fs.copyFileSync(path.join(oldDir,name),path.join(newDir,name),fs.constants.COPYFILE_EXCL);
  fs.writeFileSync(path.join(newDir,'plan.json'),JSON.stringify(next,null,2)+'\n');
  console.log('Same-scope plan prepared; existing certificate identity retained; new phone observations remain pending.');
 }catch{console.error('QR refresh rejected. Preserve diagnostics; no old plan or observations were changed.');process.exitCode=1;}
}
function readPath(file){for(let p=path.resolve(file);;p=path.dirname(p)){requireValue(!fs.lstatSync(p).isSymbolicLink());if(path.dirname(p)===p)break;}}
