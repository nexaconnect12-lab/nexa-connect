import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {prerequisites,hostedProfile,compatible,bundle} from './physical-pos.mjs';
import {sha,source} from './source.mjs';
const id='0123456789abcdef0123456789abcdef',guid='12345678-1234-4234-8234-123456789abc';
const plan=()=>({version:1,runId:id,hostAddress:'192.168.1.10',deviceAddress:'192.168.1.20',origin:'https://192.168.1.10:9443',certificatePath:'private.pem',privateKeyPath:'private.key',clientCaPath:'public.cer',deviceWindowsAccount:'test-cashier',confirmFreshTestData:true,hostTrustConfirmed:true,deviceTrustConfirmed:true});
const state=()=>({runId:id,project:`nexa-draft-${id}`,status:'Ready',provisionedBranch:true,physicalPosPilot:{origin:plan().origin},authority:`${plan().origin}/realms/nexa-review-it-${id}`});
const original=()=>({Pos:{OrganizationId:guid,RestaurantId:guid,BranchId:guid,StoreId:guid,TerminalId:guid,PaymentMethod:'cash_manual',EnableOmiseTestCheckout:false,PromptPayQrImagePath:'private-local-path'},Identity:{ClientSecret:'must-not-copy'},Services:{PosApi:'http://localhost:1234/'}});
test('unconfirmed network/account/trust is preparation only',()=>{const p=plan();p.deviceWindowsAccount=null;p.hostTrustConfirmed=false;p.deviceAddress='8.8.8.8';assert.deepEqual(prerequisites(p),['physical-pos-ipv4','deviceWindowsAccount','hostTrustConfirmed']);});
test('explicit alternate unprivileged staff port is supported consistently',()=>{const p=plan();p.origin='https://192.168.1.10:10443';assert.deepEqual(prerequisites(p),[]);for(const origin of ['https://192.168.1.10:443','http://192.168.1.10:9443','https://192.168.1.10:9443/path']){p.origin=origin;assert.ok(prerequisites(p).includes('staff-https-origin'));}});
test('only isolated ready/provisioned state produces manual hosted profile without credentials or local paths',()=>{const profile=hostedProfile(plan(),state(),original());assert.equal(profile.Identity.Authority,state().authority);assert.equal(profile.Services.MediaApi,plan().origin+'/');assert.equal(profile.Pos.EnableOmiseTestCheckout,false);assert.equal(profile.Pos.PromptPayQrImagePath,'');assert.equal(profile.Session.EnablePinUnlock,true);assert.ok(!JSON.stringify(profile).includes('must-not-copy'));assert.ok(!JSON.stringify(profile).includes('localhost'));assert.ok(!('UseIsolatedLocalPilotStorage'in profile.Pos));});
for(const mutate of [s=>s.runId='f'.repeat(32),s=>s.project='unowned',s=>s.physicalPosPilot=null,s=>s.status='Stopped',s=>s.authority='http://localhost:1234/realms/wrong',s=>s.provisionedBranch=false])test('reject unrelated/unready/loopback state '+mutate.toString(),()=>{const s=state();mutate(s);assert.throws(()=>hostedProfile(plan(),s,original()));});
test('compatible update requires strictly newer version and unchanged custody',()=>{const p=hostedProfile(plan(),state(),original());compatible(p,p,'0.8.0','0.8.1');compatible(p,p,'0.9.9','1.0.0');for(const version of ['0.7.9','0.8.0','bad'])assert.throws(()=>compatible(p,p,'0.8.0',version));const different=structuredClone(p);different.Pos.TerminalId='f'.repeat(36);assert.throws(()=>compatible(p,different,'0.8.0','0.8.1'));different.Pos.TerminalId=p.Pos.TerminalId;different.Identity.Authority+='2';assert.throws(()=>compatible(p,different,'0.8.0','0.8.1'));});
test('bundle is immutable, source-bound, hashed and contains only explicit public onboarding files',()=>{
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'nexa-physical-pos-'));try{
  const write=(name,value)=>{const file=path.join(root,name);fs.writeFileSync(file,JSON.stringify(value));return file;};
  const p=plan();p.clientCaPath=path.join(root,'public.cer');fs.copyFileSync(new URL('../../tests/Fixtures/physical-pos-test-ca.pem',import.meta.url),p.clientCaPath);
  fs.appendFileSync(p.clientCaPath,'\n-----BEGIN PRIVATE KEY-----\nnot-a-real-key\n-----END PRIVATE KEY-----\n');
  const candidate=path.join(root,'candidate');fs.mkdirSync(path.join(candidate,'installer'),{recursive:true});const bytes=Buffer.from('synthetic-setup');fs.writeFileSync(path.join(candidate,'installer','NexaConnect-POS-0.8.0-win-x64-setup.exe'),bytes);
  const manifest={format:1,packageVersion:'0.8.0',installed:false,storeSubmitted:false,releaseSource:source(),privateUnsigned:true,setupSha256:sha(bytes)};fs.writeFileSync(path.join(candidate,'installer-candidate.json'),JSON.stringify(manifest));
  const freshState=state();freshState.publishedSource=manifest.releaseSource;
  const args=[write('plan.json',p),write('state.json',freshState),write('profile.json',original()),candidate,path.join(root,'output')];
  const result=bundle(...args);assert.equal(result.installed,false);const receipt=JSON.parse(fs.readFileSync(path.join(args[4],'onboarding.json')));assert.equal(receipt.automaticUpdates,false);assert.equal(receipt.releaseReady,false);assert.equal(Object.keys(receipt.files).length,5);assert.ok(!Object.keys(receipt.files).some(f=>f.includes('key')));assert.ok(!fs.readFileSync(path.join(args[4],'client-ca.cer')).toString().includes('PRIVATE KEY'));assert.throws(()=>bundle(...args));
  freshState.publishedSource={...manifest.releaseSource,inputHash:'0'.repeat(64)};fs.writeFileSync(args[1],JSON.stringify(freshState));args[4]=path.join(root,'mismatched-backend');assert.throws(()=>bundle(...args));assert.ok(!fs.existsSync(args[4]));freshState.publishedSource=manifest.releaseSource;fs.writeFileSync(args[1],JSON.stringify(freshState));
  fs.writeFileSync(path.join(candidate,'installer','NexaConnect-POS-0.8.0-win-x64-setup.exe'),'changed');args[4]=path.join(root,'tampered');assert.throws(()=>bundle(...args));assert.ok(!fs.existsSync(args[4]));
 }finally{fs.rmSync(root,{recursive:true,force:true});}
});
