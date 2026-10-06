import {readSettings as endOfDaySettings} from '../end-of-day-live/settings.mjs';
import {resolve,dirname,join} from 'node:path';
export function readSettings(env){
 const s=endOfDaySettings(env),p='NEXACONNECT_FINANCIAL_PORTAL_';
 if(env[p+'DAY_CLOSE']!=='1')throw new Error('Day-close launcher mode required.');
 const user=env[p+'SECOND_MANAGER_USERNAME'],password=env[p+'SECOND_MANAGER_PASSWORD'];
 if(!user||!password||[s.reader.username,s.resolver.username].includes(user))throw new Error('A distinct second manager is required.');
 const posPort=Number(env[p+'POS_PORT']);
 const ports=[posPort,s.proxyPort,s.reportingPort,s.sourceProxyPort,s.paymentPort,Number(new URL(s.baseURL).port),Number(new URL(s.issuer).port)];
 if(!Number.isInteger(posPort)||posPort<1024||posPort>65535||new Set(ports).size!==ports.length)throw new Error('Distinct generated POS listener required.');
 const ids=s.fixture.pos;
 if(!ids||Object.keys(ids).length!==6||new Set(Object.values(ids)).size!==6||Object.values(ids).some(x=>typeof x!=='string'||!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(x)||/^0{8}(?:-0{4}){3}-0{12}$/.test(x)))throw new Error('Run-owned POS fixture identifiers required.');
 return {...s,posPort,controlPath:join(dirname(resolve(env[p+'STATE_PATH'])),'pos-control'),secondManager:{username:user,password}};
}
