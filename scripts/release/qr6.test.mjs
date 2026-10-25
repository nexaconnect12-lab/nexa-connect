import test from 'node:test';
import assert from 'node:assert/strict';
import {assess,gates,phoneCases} from './qr6.mjs';
const current={version:1,revision:'a'.repeat(40),dirty:true,inputHash:'b'.repeat(64),fileCount:1};
const schemas={Order:22};const outputs={customer:{sha256:'c'.repeat(64),fileCount:1}};
function fixture(){return {version:1,kind:'qr6',runId:'11111111-1111-4111-8111-111111111111',source:{...current},schemas,artifacts:outputs,passed:true,productionVerified:false,completedAtUtc:new Date().toISOString(),checks:Object.fromEntries(Object.entries(gates).map(([id,min])=>[id,{passed:true,count:min,...id==='owning'?{cleanupVerified:true,brokerRecoveryVerified:true,evidenceSha256:'d'.repeat(64)}:{}}]))};}
test('local passing evidence never certifies hosted phones CI or release',()=>{const c=assess(fixture(),current,schemas,outputs);assert.equal(c.releaseReady,false);assert.equal(c.status,'local-test-candidate');assert.equal(c.pending.length,8);assert.equal(phoneCases.length,12);});
for(const id of Object.keys(gates))test(`missing or failed ${id} cannot prepare candidate`,()=>{for(const mutate of [e=>delete e.checks[id],e=>e.checks[id].passed=false,e=>e.checks[id].count=0]){const e=fixture();mutate(e);assert.throws(()=>assess(e,current,schemas,outputs));}});
test('changed input schema artifact cleanup broker or claimed production evidence fails closed',()=>{
 for(const mutate of [e=>e.source.inputHash='0'.repeat(64),e=>e.source.dirty=false,e=>e.schemas={},e=>e.artifacts={},e=>e.checks.owning.cleanupVerified=false,e=>e.checks.owning.brokerRecoveryVerified=false,e=>e.productionVerified=true,e=>e.completedAtUtc='2000-01-01T00:00:00Z']){const e=fixture();mutate(e);assert.throws(()=>assess(e,current,schemas,outputs));}
});
test('safe candidate projection drops arbitrary log bodies and attachments',()=>{const e=fixture();e.headers={Authorization:'secret'};e.checks.unit.body='private';const c=assess(e,current,schemas,outputs);assert(!JSON.stringify(c).includes('private'));assert(!JSON.stringify(c).includes('secret'));});
