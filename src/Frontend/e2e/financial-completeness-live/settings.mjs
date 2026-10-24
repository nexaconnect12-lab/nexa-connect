import {readFileSync} from 'node:fs';
import {resolve,basename,dirname} from 'node:path';
export function readSettings(env){
  const get=k=>{const v=env[`NEXACONNECT_FINANCIAL_PORTAL_${k}`];if(!v)throw new Error(`Missing financial-completeness acceptance setting: ${k}`);return v;};
  const runId=get('RUN_ID');
  if(get('ENABLED')!=='1'||get('CONFIRM_DISPOSABLE')!=='1'||!/^[a-f0-9]{32}$/.test(runId))throw new Error('Explicit disposable acceptance required.');
  const base=new URL(get('BASE_URL')),issuer=new URL(get('OIDC_ISSUER'));
  const local=u=>u.hostname==='127.0.0.1'&&!u.username&&!u.password&&!u.search&&!u.hash;
  if(!local(base)||base.protocol!=='https:'||base.pathname!=='/'||!local(issuer)||issuer.protocol!=='http:'||issuer.pathname!==`/realms/nexa-review-it-${runId}`)throw new Error('Only the generated local identity environment is allowed.');
  const statePath=resolve(get('STATE_PATH'));
  if(basename(statePath)!=='fixture.json'||basename(dirname(statePath))!==runId)throw new Error('Run-scoped state required.');
  const fixture=JSON.parse(readFileSync(statePath,'utf8').replace(/^\uFEFF/,''));
  if(fixture.runId!==runId)throw new Error('Fixture run mismatch.');
  for(const key of ['organizationId','otherOrganizationId','restaurantId','branchId','deniedBranchId'])if(!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(fixture[key])||/^0{8}(?:-0{4}){3}-0{12}$/.test(fixture[key]))throw new Error('Invalid fixture scope.');
  if(fixture.organizationId===fixture.otherOrganizationId||fixture.branchId===fixture.deniedBranchId||get('READER_USERNAME')===get('RESOLVER_USERNAME'))throw new Error('Distinct scope and identity fixtures required.');
  const from=Date.parse(fixture.fromUtc),to=Date.parse(fixture.toUtc);
  if(!Number.isFinite(from)||!Number.isFinite(to)||to<=from||to-from>31*86400000||to>Date.now()
    ||new Date(from).getUTCSeconds()!==0||new Date(to).getUTCSeconds()!==0||from%60000!==0||to%60000!==0)throw new Error('A closed minute-aligned fixture window is required.');
  const port=k=>{const n=Number(get(k));if(!Number.isInteger(n)||n<1024||n>65535)throw new Error('Invalid local proxy port.');return n;};
  const fixtureDll=resolve(get('FIXTURE_DLL'));
  if(fixtureDll!==resolve('../Tools/NexaConnect.FinancialPortalAcceptance/bin/Debug/net10.0/NexaConnect.FinancialPortalAcceptance.dll'))throw new Error('Only the repository fixture executable is allowed.');
  if(port('PROXY_PORT')===port('REPORTING_PORT'))throw new Error('Proxy must not forward to itself.');
  return {runId,baseURL:base.origin,issuer:issuer.href,fixture,fixtureDll,proxyPort:port('PROXY_PORT'),reportingPort:port('REPORTING_PORT'),
    reader:{username:get('READER_USERNAME'),password:get('READER_PASSWORD')},resolver:{username:get('RESOLVER_USERNAME'),password:get('RESOLVER_PASSWORD')}};
}
