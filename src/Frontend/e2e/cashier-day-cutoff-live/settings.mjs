import {readSettings as baseSettings} from '../financial-completeness-live/settings.mjs';
import {dirname,join,resolve} from 'node:path';
export function readSettings(env){
 const s=baseSettings(env),p='NEXACONNECT_FINANCIAL_PORTAL_';
 if(['CASHIER_DAY_CUTOFF','CASHIER_DAY_CLOSE','DAY_CLOSE','END_OF_DAY'].some(k=>env[p+k]!=='1'))throw new Error('Cashier day-close launcher mode required.');
 const urls={};for(const name of ['POS','ORDER','CATALOG','INVENTORY','KITCHEN']){
  const u=new URL(env[p+name+'_URL']);if(u.protocol!=='http:'||u.hostname!=='127.0.0.1'||u.pathname!=='/'||u.username||u.password||u.search||u.hash)throw new Error('Generated local command endpoint required.');urls[name.toLowerCase()]=u.origin;
 }
 const callbackPort=Number(env[p+'CALLBACK_PORT']);
 const ports=[callbackPort,...Object.values(urls).map(u=>Number(new URL(u).port)),s.proxyPort,s.reportingPort,Number(new URL(s.baseURL).port),Number(new URL(s.issuer).port),Number(env[p+'SOURCE_PROXY_PORT']),Number(env[p+'PAYMENT_PORT'])];
 if(ports.some(n=>!Number.isInteger(n)||n<1024||n>65535)||new Set(ports).size!==ports.length)throw new Error('Distinct local ports required.');
 if(dirname(dirname(resolve(env[p+'STATE_PATH']))).split(/[\\/]/).pop()!=='cashier-day-cutoff')throw new Error('Cashier run directory required.');
 const refs=s.fixture.cashier;if(!refs||Object.keys(refs).length!==3||new Set(Object.values(refs)).size!==3||Object.values(refs).some(x=>typeof x!=='string'||!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(x)||/^0{8}(?:-0{4}){3}-0{12}$/.test(x)))throw new Error('Reference fixture required.');
 if(!/^\d{4}-\d{2}-\d{2}$/.test(s.fixture.businessDate)||Date.parse(s.fixture.businessDate+'T00:00:00+07:00')!==Date.parse(s.fixture.fromUtc)||Date.parse(s.fixture.toUtc)-Date.parse(s.fixture.fromUtc)!==86400000)throw new Error('Completed Bangkok branch day required.');
 const secondManager={username:env[p+'SECOND_MANAGER_USERNAME'],password:env[p+'SECOND_MANAGER_PASSWORD']};
 if(!secondManager.username||!secondManager.password||[s.reader.username,s.resolver.username].includes(secondManager.username))throw new Error('Distinct managers required.');
 const accountant={username:env[p+'ACCOUNTANT_USERNAME'],password:env[p+'ACCOUNTANT_PASSWORD']};if(!accountant.username||!accountant.password||[s.reader.username,s.resolver.username,secondManager.username].includes(accountant.username))throw new Error('Distinct accountant required.');
 urls.payment=`http://127.0.0.1:${Number(env[p+'PAYMENT_PORT'])}`;
 return {...s,urls,refs,secondManager,accountant,callbackURL:`http://127.0.0.1:${callbackPort}/callback`,clockPath:join(dirname(resolve(env[p+'STATE_PATH'])),'clock.json'),controlPath:join(dirname(resolve(env[p+'STATE_PATH'])),'pos-control'),sourceProxyPort:Number(env[p+'SOURCE_PROXY_PORT']),paymentPort:Number(env[p+'PAYMENT_PORT'])};
}
