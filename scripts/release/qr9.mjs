import fs from 'node:fs';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {read,write,report as pilotReport,record as pilotRecord,uuid} from './qr7.mjs';
import {sha} from './source.mjs';

// Bounded facts only. No notes, QR capabilities, cookies, payment references or bodies.
export const checks={
 'camera-scan-https':['cameraOpenedExactOrigin','noCertificateWarning','correctTable','fragmentRemoved','noStaffLogin'],
 'independent-guests-same-table':['independentDevices','sameTable','differentPrivateHistories','noOtherGuestReceipt'],
 'menu-cart-price-review':['quantitiesAndOrientation','serverTotalsReviewed','changedPricesRequireConsent','staffAcceptsOnlyAfterConsent'],
 'submit-refresh-original':['oneSubmission','refreshReturnsOriginal','pendingBlocksDuplicate','originalIdentityRetry'],
 'staff-accept-kitchen-once':['noKitchenBeforeAcceptance','oneOriginalOrder','oneInventoryReservation','oneKitchenTicketAfterRetry'],
 'cash-original-order':['originalRowAndBreakdown','originalCashierShift','cashConfirmedOnce','onePaidOrderAndCashMovement','noRepeatedKitchenEffect'],
 'manual-promptpay-original-order':['separateOriginalOrder','configuredPaymentImage','authorizedTestReceiptVerified','oneSettlementAndReceipt','noCashMovement'],
 'switch-app-lock-resume':['phoneVisitSurvives','posLocksWithWorkRetained','originalCashierResumes','historyCleared','customerDisplaySafe'],
 'network-loss-original-recovery':['phoneResponseLoss','cashierResponseLossAfterCommit','originalIdentityRecovered','noDuplicateOrderOrPayment'],
 'qr-revocation-scope-denial':['oldEntryDenied','wrongScopeDenied','otherTableUsable','acceptedOrderRecoveryPreserved'],
 'paid-receipt-pdf-reprint':['privateOriginalReceipt','actualPdfSavedAndReviewed','sameReceiptOnReprint','printFailureDoesNotChangePaid','endedVisitDenied'],
 'cash-reporting-reconciliation':['actualBrokerDelivery','oneCashEffect','oneReportingEffect','originalBreakdownAndReceipt','authorizedReconciliation']
};
const devices=['ios-safari','android-chrome'];
const requireValue=ok=>{if(!ok)throw new Error('QR9 evidence or binding is incomplete, changed or invalid.');};
function exactKeys(value,keys){return value&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).length===keys.length&&keys.every(k=>Object.hasOwn(value,k));}
export function validateObservation(value){
 requireValue(exactKeys(value,['version','id','device','caseId','result','checks'])&&value.version===1&&uuid(value.id)
  &&devices.includes(value.device)&&Object.hasOwn(checks,value.caseId)&&['passed','failed','blocked'].includes(value.result));
 const keys=checks[value.caseId];requireValue(exactKeys(value.checks,keys)&&Object.values(value.checks).every(v=>v===true||v===false||v===null));
 requireValue(value.result!=='passed'||Object.values(value.checks).every(v=>v===true));
 requireValue(value.result==='passed'||Object.values(value.checks).some(v=>v!==true));
 return value;
}
function context(run){
 const status=pilotReport(run),plan=read(path.join(run,'plan.json')),activation=read(path.join(run,'activation.json'));
 requireValue(status.configured&&status.prerequisites.length===0);
 return {status,plan,activation};
}
export function prepare(run){
 return preparePack(run,context(run));
}
export function preparePack(run,ctx){
 const {plan,activation}=ctx,dir=path.join(run,'qr9');
 // Existing directories must also pass link checks before any write.
 if(fs.existsSync(dir))requireValue(fs.lstatSync(dir).isDirectory()&&!fs.lstatSync(dir).isSymbolicLink());
 fs.mkdirSync(dir,{recursive:true,mode:0o700});
 const binding={version:1,pilotId:plan.id,candidateBinding:plan.candidateBinding,activationBinding:activation.binding,kind:'operator-checklists',releaseReady:false,productionVerified:false};
 if(fs.existsSync(path.join(dir,'session.json')))session(run,ctx);else write(path.join(dir,'session.json'),binding);
 for(const device of devices)for(const [caseId,keys]of Object.entries(checks)){
  const file=path.join(dir,`${device}--${caseId}.template.json`);
  if(fs.existsSync(file)){const value=validateObservation(read(file));requireValue(value.device===device&&value.caseId===caseId);}
  else write(file,{version:1,id:randomUUID(),device,caseId,result:'blocked',checks:Object.fromEntries(keys.map(k=>[k,null]))});
 }
 return {prepared:true,pilotId:plan.id,templates:24,releaseReady:false};
}
function session(run,ctx){
 const value=read(path.join(run,'qr9/session.json'));
 requireValue(value.version===1&&value.kind==='operator-checklists'&&value.releaseReady===false&&value.productionVerified===false
  &&value.pilotId===ctx.plan.id&&value.candidateBinding===ctx.plan.candidateBinding&&value.activationBinding===ctx.activation.binding);
 return value;
}
export function record(run,input,confirmed){
 requireValue(confirmed==='observed');validateObservation(input);
 const ctx=context(run);session(run,ctx);
 return appendObservation(run,input,ctx,()=>pilotRecord(run,input.device,input.caseId,input.result,input.id,'observed'));
}
// File transaction/retry boundary, exercised with isolated fixture contexts in tests.
export function appendObservation(run,input,ctx,commit){
 validateObservation(input);
 const dir=path.join(run,'qr9/evidence');
 if(fs.existsSync(dir))requireValue(fs.lstatSync(dir).isDirectory()&&!fs.lstatSync(dir).isSymbolicLink());
 fs.mkdirSync(dir,{recursive:true,mode:0o700});
 const file=path.join(dir,input.id+'.json'),digest=sha(JSON.stringify(input));
 if(fs.existsSync(file)){
  const prior=read(file);requireValue(prior.inputSha256===digest&&JSON.stringify(prior.observation)===JSON.stringify(input)
   &&prior.pilotId===ctx.plan.id&&prior.candidateBinding===ctx.plan.candidateBinding&&prior.activationBinding===ctx.activation.binding);
 }else write(file,{version:1,pilotId:ctx.plan.id,candidateBinding:ctx.plan.candidateBinding,activationBinding:ctx.activation.binding,
  inputSha256:digest,observation:input,observedAtUtc:new Date().toISOString(),kind:'operator-attestation',productionVerified:false});
 // An interrupted write can be retried without issuing a second case observation.
 const observations=path.join(run,'observations');
 const existing=fs.existsSync(observations)?fs.readdirSync(observations).filter(f=>f.endsWith('.json')).map(f=>read(path.join(observations,f))).filter(r=>r.evidenceId===input.id):[];
 requireValue(existing.every(r=>r.device===input.device&&r.caseId===input.caseId&&r.result===input.result));
 if(existing.length===0)commit();
 return {recorded:true,replayed:existing.length!==0,evidenceId:input.id,releaseReady:false};
}
export function summarize(status,activationBinding,evidence){
 const cases={};let passed=0,failed=0,blocked=0,pending=0;
 for(const device of devices){cases[device]={};for(const caseId of Object.keys(checks)){
  const latest=status.cases[device][caseId],value=evidence(latest.evidenceId);
  if(!value){cases[device][caseId]={status:'pending'};pending++;continue;}
  validateObservation(value.observation);
  const input=value.observation;
  requireValue(value.pilotId===status.pilotId&&value.candidateBinding===status.candidateBinding&&value.activationBinding===activationBinding
   &&input.device===device&&input.caseId===caseId&&input.id===latest.evidenceId&&input.result===latest.status
   &&value.inputSha256===sha(JSON.stringify(input))&&value.kind==='operator-attestation'&&value.productionVerified===false
   &&Number.isFinite(Date.parse(value.observedAtUtc))&&Date.parse(value.observedAtUtc)<=Date.now());
  cases[device][caseId]={status:input.result,evidenceId:input.id};
  if(input.result==='passed')passed++;else if(input.result==='failed')failed++;else blocked++;
 }}
 return {version:1,pilotId:status.pilotId,cases,counts:{passed,failed,blocked,pending},operatorCasesComplete:passed===24,releaseReady:false,productionVerified:false};
}
export function report(run){
 const ctx=context(run);session(run,ctx);
 return summarize(ctx.status,ctx.activation.binding,id=>{
  if(!id)return null;requireValue(uuid(id));const file=path.join(run,'qr9/evidence',id+'.json');return fs.existsSync(file)?read(file):null;
 });
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{const [action,run,file,confirmed]=process.argv.slice(2);const result=action==='prepare'?prepare(run):action==='report'?report(run):action==='record'?record(run,read(file),confirmed):null;requireValue(result);process.stdout.write(JSON.stringify(result)+'\n');}
 catch{process.stderr.write('QR9 validation failed; no private input details emitted.\n');process.exitCode=1;}
}
