import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {readSettings} from './settings.mjs';
import {completeEvidence} from './safe-reporter.mjs';
function environment(){
  const run='a'.repeat(32),temp=mkdtempSync(join(tmpdir(),'eod-portal-'));mkdirSync(join(temp,run));const path=join(temp,run,'fixture.json');
  const fixture={runId:run,businessDate:'2026-09-01',fromUtc:'2026-08-31T17:00:00Z',toUtc:'2026-09-01T17:00:00Z',
    ...Object.fromEntries(['organizationId','otherOrganizationId','restaurantId','branchId','deniedBranchId'].map((k,i)=>[k,`11111111-1111-1111-1111-11111111111${i}`]))};
  writeFileSync(path,JSON.stringify(fixture));
  const values={ENABLED:'1',CONFIRM_DISPOSABLE:'1',END_OF_DAY:'1',RUN_ID:run,BASE_URL:'https://127.0.0.1:4443',OIDC_ISSUER:`http://127.0.0.1:8080/realms/nexa-review-it-${run}`,STATE_PATH:path,
    FIXTURE_DLL:resolve('../Tools/NexaConnect.FinancialPortalAcceptance/bin/Debug/net10.0/NexaConnect.FinancialPortalAcceptance.dll'),PROXY_PORT:'8001',REPORTING_PORT:'8002',SOURCE_PROXY_PORT:'8003',PAYMENT_PORT:'8004',READER_USERNAME:'reader',READER_PASSWORD:'synthetic',RESOLVER_USERNAME:'manager',RESOLVER_PASSWORD:'synthetic'};
  return {env:Object.fromEntries(Object.entries(values).map(([k,v])=>['NEXACONNECT_FINANCIAL_PORTAL_'+k,v])),path,cleanup:()=>rmSync(temp,{recursive:true})};
}
test('requires explicit mode and disposable loopback identity and fixture executable',()=>{
  const {env,cleanup}=environment();try{
    assert.equal(readSettings(env).fixture.businessDate,'2026-09-01');
    for(const [key,value] of [['END_OF_DAY','0'],['CONFIRM_DISPOSABLE','0'],['BASE_URL','https://example.com'],['OIDC_ISSUER','http://127.0.0.1:8080/realms/production'],['FIXTURE_DLL',resolve('other.dll')]])
      assert.throws(()=>readSettings({...env,['NEXACONNECT_FINANCIAL_PORTAL_'+key]:value}));
  }finally{cleanup();}
});
test('rejects historical scope/date/window mismatches and invalid calendar dates',()=>{
  const {env,path,cleanup}=environment();try{const source=JSON.parse(readFileSync(path,'utf8'));
    for(const change of [{businessDate:'2026-02-30'},{businessDate:'2099-01-01'},{fromUtc:'2026-09-01T00:00:00Z'},
      {toUtc:'2026-09-02T17:00:00Z'},{runId:'b'.repeat(32)},{branchId:'00000000-0000-0000-0000-000000000000'}]){
      writeFileSync(path,JSON.stringify({...source,...change}));assert.throws(()=>readSettings(env));}
  }finally{cleanup();}
});
test('source proxy rejects invalid ports and loops into generated listeners',()=>{
  const {env,cleanup}=environment();try{for(const value of ['0','65536','8001','8002','8004','4443','8080'])
    assert.throws(()=>readSettings({...env,NEXACONNECT_FINANCIAL_PORTAL_SOURCE_PROXY_PORT:value}));
  }finally{cleanup();}
});
test('requires exactly eight unique passes and rejects missing cases, retries and skips',()=>{
  const results=Array.from({length:8},(_,i)=>({title:String(i),status:'passed'}));
  assert.equal(completeEvidence('passed',results),true);assert.equal(completeEvidence('failed',results),false);
  assert.equal(completeEvidence('passed',results.slice(1)),false);assert.equal(completeEvidence('passed',[...results,results[0]]),false);
  assert.equal(completeEvidence('passed',results.map((r,i)=>i===0?{...r,status:'skipped'}:r)),false);
});
