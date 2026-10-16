import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {readSettings} from './settings.mjs';
import {completeEvidence} from './safe-reporter.mjs';
const run='a'.repeat(32),id='11111111-1111-1111-1111-111111111111';
function environment(){
  const temp=mkdtempSync(join(tmpdir(),'financial-portal-'));mkdirSync(join(temp,run));
  const path=join(temp,run,'fixture.json');
  writeFileSync(path,JSON.stringify({runId:run,fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",...Object.fromEntries(['organizationId','otherOrganizationId','restaurantId','branchId','deniedBranchId'].map((k,i)=>[k,id.slice(0,-1)+i]))}));
  const values={ENABLED:'1',CONFIRM_DISPOSABLE:'1',RUN_ID:run,BASE_URL:'https://127.0.0.1:4443',OIDC_ISSUER:`http://127.0.0.1:8080/realms/nexa-review-it-${run}`,STATE_PATH:path,
    FIXTURE_DLL:resolve('../Tools/NexaConnect.FinancialPortalAcceptance/bin/Debug/net10.0/NexaConnect.FinancialPortalAcceptance.dll'),PROXY_PORT:'8001',REPORTING_PORT:'8002',READER_USERNAME:'reader',READER_PASSWORD:'synthetic',RESOLVER_USERNAME:'resolver',RESOLVER_PASSWORD:'synthetic'};
  return {env:Object.fromEntries(Object.entries(values).map(([k,v])=>['NEXACONNECT_FINANCIAL_PORTAL_'+k,v])),cleanup:()=>rmSync(temp,{recursive:true})};
}
test('guard rejects missing opt-in, remote endpoints, wrong realm and executable',()=>{
  const {env,cleanup}=environment();try{
    assert.equal(readSettings(env).runId,run);
    for(const [key,value] of [['ENABLED','0'],['CONFIRM_DISPOSABLE','0'],['BASE_URL','https://example.com'],['OIDC_ISSUER','http://127.0.0.1:8080/realms/production'],['FIXTURE_DLL',resolve('other.dll')],['PROXY_PORT','0']])
      assert.throws(()=>readSettings({...env,['NEXACONNECT_FINANCIAL_PORTAL_'+key]:value}));
  }finally{cleanup();}
});
test('guard rejects mismatched retained fixture',()=>{
  const {env,cleanup}=environment();try{const path=env.NEXACONNECT_FINANCIAL_PORTAL_STATE_PATH;writeFileSync(path,JSON.stringify({runId:'b'.repeat(32)}));assert.throws(()=>readSettings(env));}finally{cleanup();}
});
test('guard rejects open, invalid or imprecise windows and empty or reused scopes',()=>{
  const {env,cleanup}=environment();try{
    const path=env.NEXACONNECT_FINANCIAL_PORTAL_STATE_PATH;
    const source=JSON.parse(readFileSync(path,'utf8'));
    for(const invalid of [{toUtc:'2099-09-02T00:00:00Z'},{toUtc:source.fromUtc},{fromUtc:'2026-07-01T00:00:00Z'},
      {fromUtc:'2026-09-01T00:00:01Z'},{branchId:'00000000-0000-0000-0000-000000000000'},{otherOrganizationId:source.organizationId}]){
      writeFileSync(path,JSON.stringify({...source,...invalid}));assert.throws(()=>readSettings(env));
    }
  }finally{cleanup();}
});
test('evidence requires all seven unique scenarios and rejects skips or retries',()=>{
  const results=Array.from({length:7},(_,i)=>({title:String(i),status:'passed'}));
  assert.equal(completeEvidence('passed',results),true);
  assert.equal(completeEvidence('failed',results),false);
  assert.equal(completeEvidence('passed',results.slice(1)),false);
  assert.equal(completeEvidence('passed',[...results,results[0]]),false);
  assert.equal(completeEvidence('passed',results.map((r,i)=>i===0?{...r,status:'skipped'}:r)),false);
});
