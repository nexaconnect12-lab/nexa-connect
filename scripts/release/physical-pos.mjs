import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import {X509Certificate} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {sha,source,sameSource} from './source.mjs';
const requireValue=ok=>{if(!ok)throw Error('Physical POS onboarding inputs are incomplete, incompatible or changed.');};
export const privateIPv4=value=>typeof value==='string'&&net.isIP(value)===4&&(/^10\./.test(value)||/^192\.168\./.test(value)||/^172\.(1[6-9]|2\d|3[01])\./.test(value));
const validOrigin=plan=>{try{const u=new URL(plan.origin);return typeof plan.origin==='string'&&u.protocol==='https:'&&u.hostname===plan.hostAddress&&u.port!==''&&Number(u.port)>=1024&&u.origin===plan.origin&&u.pathname==='/'&&!u.username&&!u.password&&!u.search&&!u.hash;}catch{return false;}};
export function prerequisites(plan){
 requireValue(plan?.version===1&&/^[a-f0-9]{32}$/.test(plan.runId));
 return [
  ...!privateIPv4(plan.hostAddress)?['development-pc-ipv4']:[],...!privateIPv4(plan.deviceAddress)||plan.deviceAddress===plan.hostAddress?['physical-pos-ipv4']:[],
  ...!validOrigin(plan)?['staff-https-origin']:[],
  ...['certificatePath','privateKeyPath','clientCaPath','deviceWindowsAccount'].filter(k=>typeof plan[k]!=='string'||!plan[k].trim()).map(k=>k),
  ...['confirmFreshTestData','hostTrustConfirmed','deviceTrustConfirmed'].filter(k=>plan[k]!==true).map(k=>k)
 ];
}
const guid=value=>typeof value==='string'&&/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value)&&!/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
export function hostedProfile(plan,state,original){
 requireValue(prerequisites(plan).length===0&&state.runId===plan.runId&&state.project===`nexa-draft-${plan.runId}`&&state.status==='Ready'&&state.provisionedBranch
  &&state.physicalPosPilot?.origin===plan.origin&&state.authority===`${plan.origin}/realms/nexa-review-it-${plan.runId}`);
 const pos=original.Pos;
 requireValue(['OrganizationId','RestaurantId','BranchId','StoreId','TerminalId'].every(k=>guid(pos?.[k]))&&pos.PaymentMethod==='cash_manual'&&!pos.EnableOmiseTestCheckout);
 return {Identity:{Authority:state.authority,ClientId:'nexaconnect-pos',RedirectUri:'nexaconnect-pos://oauth/callback',Scopes:'openid profile email nexaconnect-api'},
  Session:{IdleTimeoutMinutes:5,AbsoluteTimeoutHours:10,TokenRefreshLeadSeconds:60,EnablePinUnlock:true},
  Services:Object.fromEntries(['PosApi','OrderApi','CatalogApi','PaymentApi','MediaApi'].map(k=>[k,plan.origin+'/'])),
  Pos:Object.fromEntries([...['OrganizationId','RestaurantId','BranchId','StoreId','TerminalId'].map(k=>[k,pos[k]]),['Currency','THB'],['PaymentMethod','cash_manual'],['EnableOmiseTestCheckout',false],['PromptPayQrImagePath',''],['UseBranchPromptPayQr',true],['ShopName','NexaConnect']])};
}
export function compatible(previous,next,previousVersion,nextVersion){
 const version=value=>{requireValue(typeof value==='string'&&/^\d{1,4}\.\d{1,4}\.\d{1,4}$/.test(value));return value.split('.').map(Number);};
 const a=version(previousVersion),b=version(nextVersion);let newer=false;
 for(let i=0;i<3;i++){if(b[i]!==a[i]){newer=b[i]>a[i];break;}}requireValue(newer);
 requireValue(['Authority','ClientId','RedirectUri'].every(k=>previous.Identity[k]===next.Identity[k])
  &&['OrganizationId','RestaurantId','BranchId','StoreId','TerminalId','Currency','PaymentMethod','EnableOmiseTestCheckout'].every(k=>previous.Pos[k]===next.Pos[k]));
}
export function read(file){for(let p=path.resolve(file);;p=path.dirname(p)){requireValue(!fs.lstatSync(p).isSymbolicLink());if(path.dirname(p)===p)break;}requireValue(fs.statSync(file).size<=4*1024*1024);return JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));}
export function bundle(planFile,stateFile,profileFile,candidateDirectory,output,previousDirectory){
 const plan=read(planFile),state=read(stateFile),profile=hostedProfile(plan,state,read(profileFile)),candidate=read(path.join(candidateDirectory,'installer-candidate.json'));
 requireValue(candidate.format===1&&/^\d{1,4}\.\d{1,4}\.\d{1,4}$/.test(candidate.packageVersion)&&candidate.installed===false&&candidate.storeSubmitted===false&&sameSource(candidate.releaseSource,source())&&sameSource(state.publishedSource,candidate.releaseSource));
 const setup=path.join(candidateDirectory,'installer',`NexaConnect-POS-${candidate.packageVersion}-win-x64-setup.exe`);
 requireValue(sha(fs.readFileSync(setup))===candidate.setupSha256.toLowerCase());
 if(previousDirectory){const previous=read(path.join(previousDirectory,'onboarding.json'));const oldProfile=read(path.join(previousDirectory,'terminal-profile.json'));requireValue(sha(fs.readFileSync(path.join(previousDirectory,'terminal-profile.json')))===previous.files['terminal-profile.json']);compatible(oldProfile,profile,previous.packageVersion,candidate.packageVersion);}
 const ca=new X509Certificate(fs.readFileSync(plan.clientCaPath));requireValue(ca.ca&&Date.parse(ca.validFrom)<=Date.now()&&Date.parse(ca.validTo)>Date.now());
 requireValue(!fs.existsSync(output));fs.mkdirSync(output,{recursive:true,mode:0o700});
 fs.copyFileSync(setup,path.join(output,'NexaConnect-POS-setup.exe'));
 fs.writeFileSync(path.join(output,'terminal-profile.json'),JSON.stringify(profile,null,2)+'\n',{flag:'wx',mode:0o600});
 // Canonical public DER only: never copy a PEM container that could have trailing private-key material.
 fs.writeFileSync(path.join(output,'client-ca.cer'),ca.raw,{flag:'wx'});
 const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
 fs.copyFileSync(path.join(repository,'packaging/windows-pos/Test-DeviceOnboarding.ps1'),path.join(output,'Test-DeviceOnboarding.ps1'));
 fs.writeFileSync(path.join(output,'START-HERE.txt'),`NexaConnect physical POS ${candidate.packageVersion}\r\nPRIVATE TEST ONLY - not a Store or production release.\r\nUse the intended ordinary Windows account. Never copy another device's tokens/database/PIN files.\r\n1. Verify onboarding.json SHA-256 against the manifest value supplied separately by your operator.\r\n2. BEFORE executing the checker or installing trust, independently compare Test-DeviceOnboarding.ps1 and client-ca.cer SHA-256 against verified onboarding.json. Then confirm the CA fingerprint with the operator and install only that public CA. Never copy server private keys.\r\n3. Run Test-DeviceOnboarding.ps1 -ExpectedManifestSha256 <operator-supplied-manifest-hash>; it only checks the bundle, HTTPS/OIDC and API denial.\r\n4. Close POS after finishing any action. Run NexaConnect-POS-setup.exe and select terminal-profile.json.\r\n5. Sign in with the NEW pilot credentials; old pilot data/accounts are separate.\r\n6. Configure displays (both 1366x768), printer and scanner; perform fresh acceptance tests.\r\nUpdates: use a newer compatible version and run setup with POS closed. Keep recovery intact. No auto-update or arbitrary downgrade.\r\nManual cash/PromptPay only; Omise, real drawer opening and unattended startup remain disabled.\r\nPrivate unsigned installer: ${candidate.privateUnsigned?'YES - not publisher authenticated':'NO - verify trusted signature'}\r\n`,{flag:'wx'});
 const files=Object.fromEntries(fs.readdirSync(output).sort().map(f=>[f,sha(fs.readFileSync(path.join(output,f)))]));
 const receipt={version:1,kind:'physical-pos-onboarding',runId:plan.runId,packageVersion:candidate.packageVersion,source:candidate.releaseSource,privateUnsigned:candidate.privateUnsigned,files,installed:false,deviceVerified:false,releaseReady:false,automaticUpdates:false,financialActions:0};
 const manifest=JSON.stringify(receipt,null,2)+'\n';fs.writeFileSync(path.join(output,'onboarding.json'),manifest,{flag:'wx'});return {prepared:true,packageVersion:receipt.packageVersion,manifestSha256:sha(manifest),setupSha256:files['NexaConnect-POS-setup.exe'],clientCaSha256:files['client-ca.cer'],installed:false,releaseReady:false};
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===path.resolve(process.argv[1])){
 try{const [action,...args]=process.argv.slice(2);if(action==='status'&&args.length===1)console.log(JSON.stringify({prerequisites:prerequisites(read(args[0])),installed:false,releaseReady:false}));else if(action==='bundle'&&args.length>=5&&args.length<=6)console.log(JSON.stringify(bundle(...args)));else throw Error();}
 catch{console.error('Physical POS onboarding stopped. Review private inputs; no installation or financial action performed.');process.exitCode=1;}
}
