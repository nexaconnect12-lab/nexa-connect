import {readFileSync} from 'node:fs';
import {resolve,basename,dirname} from 'node:path';
export function readSettings(env){
  const get=k=>{const v=env[`NEXACONNECT_CASH_PORTAL_${k}`];if(!v)throw new Error(`Missing cash-close acceptance setting: ${k}`);return v;};
  const runId=get('RUN_ID');
  if(get('ENABLED')!=='1'||get('CONFIRM_DISPOSABLE')!=='1'||!/^[a-f0-9]{32}$/.test(runId))throw new Error('Explicit disposable acceptance required.');
  const base=new URL(get('BASE_URL')),issuer=new URL(get('OIDC_ISSUER'));
  const local=u=>u.hostname==='127.0.0.1'&&!u.username&&!u.password&&!u.search&&!u.hash;
  if(!local(base)||base.protocol!=='https:'||base.pathname!=='/'||!local(issuer)||issuer.protocol!=='http:'||issuer.pathname!==`/realms/nexa-review-it-${runId}`)throw new Error('Only the generated local identity environment is allowed.');
  const statePath=resolve(get('STATE_PATH'));
  if(basename(statePath)!=='fixture.json'||basename(dirname(statePath))!==runId)throw new Error('Run-scoped state required.');
  const fixture=JSON.parse(readFileSync(statePath,'utf8').replace(/^\uFEFF/,''));
  if(fixture.runId!==runId)throw new Error('Fixture run mismatch.');
  for(const key of ['organizationId','otherOrganizationId','restaurantId','branchId','storeId','deniedBranchId','deniedStoreId','sessionId'])if(!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(fixture[key]))throw new Error('Invalid fixture scope.');
  if(fixture.organizationId===fixture.otherOrganizationId||fixture.branchId===fixture.deniedBranchId||fixture.storeId===fixture.deniedStoreId||get('READER_USERNAME')===get('RESOLVER_USERNAME'))throw new Error('Distinct scope and identity fixtures required.');
  const port=k=>{const n=Number(get(k));if(!Number.isInteger(n)||n<1024||n>65535)throw new Error('Invalid local proxy port.');return n;};
  const fixtureDll=resolve(get('FIXTURE_DLL'));
  if(fixtureDll!==resolve('../Tools/NexaConnect.CashClosePortalAcceptance/bin/Debug/net10.0/NexaConnect.CashClosePortalAcceptance.dll'))throw new Error('Only the repository fixture executable is allowed.');
  if(port('PROXY_PORT')===port('POS_PORT'))throw new Error('Proxy must not forward to itself.');
  return {runId,baseURL:base.origin,issuer:issuer.href,fixture,fixtureDll,proxyPort:port('PROXY_PORT'),posPort:port('POS_PORT'),
    reader:{username:get('READER_USERNAME'),password:get('READER_PASSWORD')},resolver:{username:get('RESOLVER_USERNAME'),password:get('RESOLVER_PASSWORD')}};
}
