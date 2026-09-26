import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,mkdirSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {readSettings} from './settings.mjs';
import {completeEvidence} from './safe-reporter.mjs';
const run='a'.repeat(32),id='11111111-1111-1111-1111-111111111111';
function environment(){
  const temp=mkdtempSync(join(tmpdir(),'cash-portal-'));mkdirSync(join(temp,run));
  const path=join(temp,run,'fixture.json');
  writeFileSync(path,JSON.stringify({runId:run,...Object.fromEntries(['organizationId','otherOrganizationId','restaurantId','branchId','storeId','deniedBranchId','deniedStoreId','sessionId'].map((k,i)=>[k,id.slice(0,-1)+i]))}));
  const values={ENABLED:'1',CONFIRM_DISPOSABLE:'1',RUN_ID:run,BASE_URL:'https://127.0.0.1:4443',OIDC_ISSUER:`http://127.0.0.1:8080/realms/nexa-review-it-${run}`,STATE_PATH:path,
    FIXTURE_DLL:resolve('../Tools/NexaConnect.CashClosePortalAcceptance/bin/Debug/net10.0/NexaConnect.CashClosePortalAcceptance.dll'),PROXY_PORT:'8001',POS_PORT:'8002',READER_USERNAME:'reader',READER_PASSWORD:'synthetic',RESOLVER_USERNAME:'resolver',RESOLVER_PASSWORD:'synthetic'};
  return {env:Object.fromEntries(Object.entries(values).map(([k,v])=>['NEXACONNECT_CASH_PORTAL_'+k,v])),cleanup:()=>rmSync(temp,{recursive:true})};
}
test('guard rejects missing opt-in, remote endpoints, wrong realm and executable',()=>{
  const {env,cleanup}=environment();try{
    assert.equal(readSettings(env).runId,run);
    for(const [key,value] of [['ENABLED','0'],['CONFIRM_DISPOSABLE','0'],['BASE_URL','https://example.com'],['OIDC_ISSUER','http://127.0.0.1:8080/realms/production'],['FIXTURE_DLL',resolve('other.dll')],['PROXY_PORT','0']])
      assert.throws(()=>readSettings({...env,['NEXACONNECT_CASH_PORTAL_'+key]:value}));
  }finally{cleanup();}
});
test('guard rejects mismatched retained fixture',()=>{
  const {env,cleanup}=environment();try{const path=env.NEXACONNECT_CASH_PORTAL_STATE_PATH;writeFileSync(path,JSON.stringify({runId:'b'.repeat(32)}));assert.throws(()=>readSettings(env));}finally{cleanup();}
});
test('evidence requires all five unique scenarios and rejects skips or retries',()=>{
  const results=Array.from({length:5},(_,i)=>({title:String(i),status:'passed'}));
  assert.equal(completeEvidence('passed',results),true);
  assert.equal(completeEvidence('failed',results),false);
  assert.equal(completeEvidence('passed',results.slice(1)),false);
  assert.equal(completeEvidence('passed',[...results,results[0]]),false);
  assert.equal(completeEvidence('passed',results.map((r,i)=>i===0?{...r,status:'skipped'}:r)),false);
});
