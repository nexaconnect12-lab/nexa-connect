import {readSettings as financialSettings} from '../financial-completeness-live/settings.mjs';
export function readSettings(env){
  const s=financialSettings(env),prefix='NEXACONNECT_FINANCIAL_PORTAL_';
  if(env[prefix+'END_OF_DAY']!=='1')throw new Error('End-of-day launcher mode required.');
  const date=s.fixture.businessDate;
  if(typeof date!=='string'||!/^\d{4}-\d{2}-\d{2}$/.test(date))throw new Error('Completed business date required.');
  const from=Date.parse(`${date}T00:00:00+07:00`);
  if(!Number.isFinite(from)||new Date(from+7*3600000).toISOString().slice(0,10)!==date
    ||Date.parse(s.fixture.fromUtc)!==from||Date.parse(s.fixture.toUtc)!==from+86400000
    ||from+86400000>Date.now())throw new Error('Fixture must match a completed Bangkok business day.');
  const port=k=>{const p=Number(env[prefix+k]);if(!Number.isInteger(p)||p<1024||p>65535)throw new Error('Invalid source proxy port.');return p;};
  const sourceProxyPort=port('SOURCE_PROXY_PORT'),paymentPort=port('PAYMENT_PORT');
  const ports=[s.proxyPort,s.reportingPort,sourceProxyPort,paymentPort,Number(new URL(s.baseURL).port),Number(new URL(s.issuer).port)];
  if(new Set(ports).size!==ports.length)throw new Error('Distinct generated listeners required.');
  return {...s,sourceProxyPort,paymentPort};
}
