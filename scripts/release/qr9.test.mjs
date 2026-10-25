import test from 'node:test';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {checks,validateObservation,summarize,appendObservation,preparePack} from './qr9.mjs';
import {sha} from './source.mjs';
const observation=(caseId='camera-scan-https')=>({version:1,id:randomUUID(),device:'ios-safari',caseId,result:'passed',checks:Object.fromEntries(checks[caseId].map(k=>[k,true]))});
const status=()=>({pilotId:'a'.repeat(32),candidateBinding:'b'.repeat(64),cases:Object.fromEntries(['ios-safari','android-chrome'].map(d=>[d,Object.fromEntries(Object.keys(checks).map(c=>[c,{status:'pending'}]))]))});
const evidence=(s,value)=>({version:1,pilotId:s.pilotId,candidateBinding:s.candidateBinding,activationBinding:'c'.repeat(64),observation:value,inputSha256:sha(JSON.stringify(value)),observedAtUtc:new Date().toISOString(),kind:'operator-attestation',productionVerified:false});
for(const caseId of Object.keys(checks))test('complete observed checklist is accepted: '+caseId,()=>assert.equal(validateObservation(observation(caseId)).result,'passed'));
for(const change of ['unknown-case','unknown-device','free-text','token-field','missing-check','string-check','false-pass','null-pass','nonuuid','inherited-check','alltrue-failed'])test('rejects unsafe/incomplete observation: '+change,()=>{
 const v=observation();
 if(change==='unknown-case')v.caseId='__proto__';
 if(change==='unknown-device')v.device='desktop';
 if(change==='free-text')v.notes='unrestricted';
 if(change==='token-field')v.checks.token='redacted';
 if(change==='missing-check')delete v.checks.noStaffLogin;
 if(change==='string-check')v.checks.noStaffLogin='true';
 if(change==='false-pass')v.checks.noStaffLogin=false;
 if(change==='null-pass')v.checks.noStaffLogin=null;
 if(change==='nonuuid')v.id='../outside';
 if(change==='inherited-check'){delete v.checks.noStaffLogin;Object.setPrototypeOf(v.checks,{noStaffLogin:true});}
 if(change==='alltrue-failed')v.result='failed';
 assert.throws(()=>validateObservation(v));
});
test('blocked and failed outcomes can retain incomplete observations without becoming a pass',()=>{
 for(const result of ['blocked','failed']){const v=observation();v.result=result;v.checks.noCertificateWarning=null;assert.equal(validateObservation(v).result,result);}
});
test('all cases start pending; automated diagnostics cannot mark phones accepted',()=>{
 const result=summarize(status(),'c'.repeat(64),()=>null);assert.equal(result.counts.pending,24);assert.equal(result.operatorCasesComplete,false);assert.equal(result.releaseReady,false);
});
test('legacy QR7 evidence without a QR9 checklist remains pending',()=>{
 const s=status();s.cases['ios-safari']['camera-scan-https']={status:'passed',evidenceId:randomUUID()};assert.equal(summarize(s,'c'.repeat(64),()=>null).counts.pending,24);
});
test('24 complete same-activation operator records still cannot grant release readiness',()=>{
 const s=status(),values=new Map();
 for(const [device,cases]of Object.entries(s.cases))for(const caseId of Object.keys(cases)){
  const v={...observation(caseId),device};cases[caseId]={status:'passed',evidenceId:v.id};values.set(v.id,evidence(s,v));
 }
 const result=summarize(s,'c'.repeat(64),id=>values.get(id));assert.equal(result.counts.passed,24);assert.equal(result.operatorCasesComplete,true);assert.equal(result.releaseReady,false);assert.equal(result.productionVerified,false);
});
for(const field of ['pilotId','candidateBinding','activationBinding','inputSha256','observedAtUtc','productionVerified'])test('changed evidence fails closed: '+field,()=>{
 const s=status(),v=observation(),e=evidence(s,v);s.cases[v.device][v.caseId]={status:v.result,evidenceId:v.id};
 e[field]=field==='observedAtUtc'?new Date(Date.now()+3600000).toISOString():field==='productionVerified'?true:'changed';
 assert.throws(()=>summarize(s,'c'.repeat(64),()=>e));
});
test('a failed latest retest supersedes a pass and blocks completion',()=>{
 const s=status(),v=observation();v.result='failed';v.checks.noCertificateWarning=false;s.cases[v.device][v.caseId]={status:'failed',evidenceId:v.id};
 const result=summarize(s,'c'.repeat(64),id=>id===v.id?evidence(s,v):null);assert.equal(result.counts.failed,1);assert.equal(result.counts.passed,0);assert.equal(result.operatorCasesComplete,false);
});
function fixture(action){
 const run=fs.mkdtempSync(path.join(os.tmpdir(),'nexa-qr9-'));
 try{fs.mkdirSync(path.join(run,'qr9'));return action(run,{plan:{id:'a'.repeat(32),candidateBinding:'b'.repeat(64)},activation:{binding:'c'.repeat(64)}});}
 finally{fs.rmSync(run,{recursive:true,force:true});}
}
test('retry after evidence write interruption finishes one observation and then replays',()=>fixture((run,ctx)=>{
 const v=observation();let calls=0;
 assert.throws(()=>appendObservation(run,v,ctx,()=>{throw new Error('fixture interruption');}));
 const commit=()=>{calls++;fs.mkdirSync(path.join(run,'observations'));fs.writeFileSync(path.join(run,'observations','fixture.json'),JSON.stringify({evidenceId:v.id,device:v.device,caseId:v.caseId,result:v.result}));};
 assert.equal(appendObservation(run,v,ctx,commit).replayed,false);
 assert.equal(appendObservation(run,v,ctx,commit).replayed,true);assert.equal(calls,1);
}));
test('same evidence identity cannot be overwritten with a different outcome',()=>fixture((run,ctx)=>{
 const v=observation();appendObservation(run,v,ctx,()=>{});
 v.result='failed';v.checks.noCertificateWarning=false;
 assert.throws(()=>appendObservation(run,v,ctx,()=>assert.fail('must not commit')));
}));
test('evidence from a different activation cannot resume into this pilot',()=>fixture((run,ctx)=>{
 const v=observation();appendObservation(run,v,ctx,()=>{});ctx.activation.binding='d'.repeat(64);
 assert.throws(()=>appendObservation(run,v,ctx,()=>assert.fail('must not commit')));
}));
test('preparation resumes a partial pack and preserves existing template identities and facts',()=>fixture((run,ctx)=>{
 preparePack(run,ctx);
 const file=path.join(run,'qr9/ios-safari--camera-scan-https.template.json');
 const original=JSON.parse(fs.readFileSync(file,'utf8'));original.checks.noCertificateWarning=false;original.result='failed';
 fs.writeFileSync(file,JSON.stringify(original));
 fs.unlinkSync(path.join(run,'qr9/android-chrome--camera-scan-https.template.json'));
 preparePack(run,ctx);assert.deepEqual(JSON.parse(fs.readFileSync(file,'utf8')),original);
 assert.equal(fs.readdirSync(path.join(run,'qr9')).filter(n=>n.endsWith('.template.json')).length,24);
}));
test('preparation cannot adopt a pack from another activation',()=>fixture((run,ctx)=>{
 preparePack(run,ctx);ctx.activation.binding='d'.repeat(64);assert.throws(()=>preparePack(run,ctx));
}));
