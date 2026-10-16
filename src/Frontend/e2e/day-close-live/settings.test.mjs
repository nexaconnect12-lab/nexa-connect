import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve,basename} from 'node:path';
import {readSettings} from './settings.mjs';
import {completeEvidence} from './safe-reporter.mjs';
import {scenarios} from './scenarios.mjs';
function environment(){
 const run='a'.repeat(32),temp=mkdtempSync(join(tmpdir(),'day-close-portal-'));mkdirSync(join(temp,run));const path=join(temp,run,'fixture.json');
 const id=n=>`11111111-1111-1111-1111-${String(n).padStart(12,'0')}`;
 const fixture={runId:run,businessDate:'2026-09-01',fromUtc:'2026-08-31T17:00:00Z',toUtc:'2026-09-01T17:00:00Z',...Object.fromEntries(['organizationId','otherOrganizationId','restaurantId','branchId','deniedBranchId'].map((k,i)=>[k,id(i+1)])),pos:Object.fromEntries(['storeId','terminalId','closedSessionId','openShiftId','olderTerminalId','openSessionId'].map((k,i)=>[k,id(i+20)]))};
 writeFileSync(path,JSON.stringify(fixture));
 const values={ENABLED:'1',CONFIRM_DISPOSABLE:'1',END_OF_DAY:'1',DAY_CLOSE:'1',RUN_ID:run,BASE_URL:'https://127.0.0.1:4443',OIDC_ISSUER:`http://127.0.0.1:8080/realms/nexa-review-it-${run}`,STATE_PATH:path,FIXTURE_DLL:resolve('../Tools/NexaConnect.FinancialPortalAcceptance/bin/Debug/net10.0/NexaConnect.FinancialPortalAcceptance.dll'),PROXY_PORT:'8001',REPORTING_PORT:'8002',SOURCE_PROXY_PORT:'8003',PAYMENT_PORT:'8004',POS_PORT:'8005',READER_USERNAME:'reader',READER_PASSWORD:'synthetic',RESOLVER_USERNAME:'manager',RESOLVER_PASSWORD:'synthetic',SECOND_MANAGER_USERNAME:'other-manager',SECOND_MANAGER_PASSWORD:'synthetic'};
 return {env:Object.fromEntries(Object.entries(values).map(([k,v])=>['NEXACONNECT_FINANCIAL_PORTAL_'+k,v])),path,cleanup:()=>{const checked=resolve(temp);assert.equal(checked,resolve(join(tmpdir(),basename(temp))));assert.ok(basename(checked).startsWith('day-close-portal-'));rmSync(checked,{recursive:true});}};
}
test('requires new mode disposable opt-in exact realm and repository fixture',()=>{
 const {env,cleanup}=environment();try{assert.equal(readSettings(env).secondManager.username,'other-manager');for(const [key,value] of [['DAY_CLOSE','0'],['END_OF_DAY','0'],['CONFIRM_DISPOSABLE','0'],['BASE_URL','https://remote.example'],['OIDC_ISSUER','http://127.0.0.1:8080/realms/production'],['FIXTURE_DLL',resolve('other.dll')]])assert.throws(()=>readSettings({...env,['NEXACONNECT_FINANCIAL_PORTAL_'+key]:value}));}finally{cleanup();}
});
test('rejects missing duplicate managers or colliding POS listeners',()=>{
 const {env,cleanup}=environment();try{for(const [key,value] of [['SECOND_MANAGER_USERNAME',''],['SECOND_MANAGER_PASSWORD',''],['SECOND_MANAGER_USERNAME','reader'],['SECOND_MANAGER_USERNAME','manager'],...['0','65536','8001','8002','8003','8004','4443','8080'].map(v=>['POS_PORT',v])])assert.throws(()=>readSettings({...env,['NEXACONNECT_FINANCIAL_PORTAL_'+key]:value}));}finally{cleanup();}
});
test('rejects malformed POS fixture identities and wrong calendar window',()=>{
 const {env,path,cleanup}=environment();try{const source=JSON.parse(readFileSync(path,'utf8'));for(const change of [{pos:null},{pos:{...source.pos,storeId:'bad'}},{pos:{...source.pos,openSessionId:source.pos.closedSessionId}},{businessDate:'2099-01-01'},{toUtc:'2026-09-02T17:00:00Z'}]){writeFileSync(path,JSON.stringify({...source,...change}));assert.throws(()=>readSettings(env));}}finally{cleanup();}
});
test('requires the exact eleven scenario identities without retries or skips',()=>{
 const results=scenarios.map(title=>({title,status:'passed'}));assert.equal(completeEvidence('passed',results),true);assert.equal(completeEvidence('failed',results),false);assert.equal(completeEvidence('passed',results.slice(1)),false);assert.equal(completeEvidence('passed',[...results,results[0]]),false);assert.equal(completeEvidence('passed',results.map((x,i)=>i?x:{...x,title:'unexpected'})),false);assert.equal(completeEvidence('passed',results.map((x,i)=>i?x:{...x,status:'skipped'})),false);
});
