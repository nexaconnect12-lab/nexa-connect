import {test} from 'node:test';
import assert from 'node:assert/strict';
import {refreshPlan} from './local-pilot.mjs';
import {sha} from './source.mjs';
function fixture(){
 const id='a'.repeat(32),old={version:1,id:'b'.repeat(32),stackRunId:id,candidateBinding:'c'.repeat(64),publicOrigin:'https://192.168.0.191:8443',listenAddress:'192.168.0.191',trust:{windows:true,'ios-safari':true,'android-chrome':true},devices:Object.fromEntries(['ios-safari','android-chrome'].map(k=>[k,{model:'fixture',osVersion:'fixture',browserVersion:'fixture'}]))};
 const reference={runId:id},scope={};for(const name of ['OrganizationId','RestaurantId','BranchId','StoreId','TerminalId'])scope[name]=reference[name[0].toLowerCase()+name.slice(1)]='11111111-1111-4111-8111-111111111111';
 const hash=sha('non-canonical old plan bytes'),value={pilotId:old.id,stackRunId:id,scope,planSha256:hash};
 return [old,{...value,binding:sha(JSON.stringify(value))},{version:1,id:'d'.repeat(32),candidateBinding:'e'.repeat(64)},{runId:id,status:'Ready',qrPilotOrigin:old.publicOrigin},reference,hash];
}
test('fresh plan retains exact scope/origin/facts with new candidate and no observations',()=>{
 const f=fixture(),result=refreshPlan(...f);assert.equal(result.stackRunId,f[3].runId);assert.equal(result.candidateBinding,'e'.repeat(64));assert.deepEqual(result.trust,f[0].trust);assert.equal(result.observations,undefined);assert.notEqual(result.id,f[0].id);
});
for(const change of ['activation','scope','origin','run','lab','plan-bytes','trust','same-id'])test(`reject ${change}`,()=>{
 const f=fixture();if(change==='activation')f[1].binding='bad';if(change==='scope')f[4].branchId='other';if(change==='origin')f[3].qrPilotOrigin='https://192.168.0.190:8443';if(change==='run')f[3].runId='f'.repeat(32);if(change==='lab')f[4].recoveryLabAccessCompleted=true;if(change==='plan-bytes')f[5]='bad';if(change==='trust')f[0].trust.windows=false;if(change==='same-id')f[2].id=f[0].id;assert.throws(()=>refreshPlan(...f));
});
