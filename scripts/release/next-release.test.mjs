import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { source, sameSource, sha } from './source.mjs';
import { assess, observationIds, uatIds, validateObservations, validateCi, validateVerification, validateFrozenBinding, sourceChecks, inspectArtifacts, create, freeze, prepareUat, status } from './next-release.mjs';
import { files, inventory } from '../../docker/pilot/hosting.mjs';
import { tap, playwright, vitest } from './results.mjs';
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'nexa-freeze-it-'));
  t.after(() => { assert.equal(path.dirname(path.resolve(root)), path.resolve(os.tmpdir())); assert.match(path.basename(root), /^nexa-freeze-it-[a-zA-Z0-9_-]+$/); fs.rmSync(root, { recursive: true, force: true }); }); return root;
}
const bound = { candidateHash: 'a'.repeat(64), artifactsDigest: 'b'.repeat(64), configurationDigest: 'c'.repeat(64) };
const stamp = { version: 1, revision: 'a'.repeat(40), inputHash: 'd'.repeat(64), dirty: false, fileCount: 1 };
function observations() { return { version: 1, kind: 'operator-attested', ...bound, environmentId: randomUUID(), cases: Object.fromEntries(observationIds.map(id => [id, { status: 'passed', evidenceId: randomUUID(), observedAtUtc: new Date().toISOString() }])) }; }
function ready() { return { current: true, dirty: false, artifacts: true, privateUnsigned: false, unsignedPayloadPeCount: 0, signatures: true, configuration: true, sourceTests: true, joinedTests: true, ci: true, observations: observations().cases }; }
test('all prerequisites are needed and user acceptance always remains pending', () => assert.deepEqual(assess(ready()), { readyToFreeze: true, blockers: [], acceptance: 'pending-user-test' }));
for (const key of ['current', 'artifacts', 'signatures', 'configuration', 'sourceTests', 'joinedTests', 'ci']) test(`missing ${key} cannot freeze`, () => { const s = ready(); s[key] = false; assert.equal(assess(s).readyToFreeze, false); });
test('dirty source and unsigned candidate cannot be waived by passed attestations', () => { const s = ready(); s.dirty = true; s.privateUnsigned = true; assert.deepEqual(assess(s).blockers, ['clean-source-required', 'trusted-installer-signing-required']); });
for (const id of observationIds) test(`pending ${id} blocks freeze`, () => { const s = ready(); s.observations[id].status = 'pending'; assert(assess(s).blockers.includes(`observation:${id}`)); });
test('no tests/observations implies all release gates remain pending', () => { const r = assess({}); assert.equal(r.readyToFreeze, false); assert.equal(r.acceptance, 'pending-user-test'); assert.equal(r.blockers.filter(v => v.startsWith('observation:')).length, observationIds.length); });
test('observation binding rejects old artifacts, config and invented measured automation', () => {
  for (const key of ['candidateHash', 'artifactsDigest', 'configurationDigest']) { const v = observations(); v[key] = '0'.repeat(64); assert.throws(() => validateObservations(v, bound, '2020-01-01T00:00:00Z')); }
  const v = observations(); v.kind = 'measured'; assert.throws(() => validateObservations(v, bound, '2020-01-01T00:00:00Z'));
});
test('arbitrary operator notes and secret-bearing attachments never enter the persisted report', () => {
  const v = observations(); v.notes = 'secret'; v.cases[observationIds[0]].headers = { Authorization: 'secret' };
  assert(!JSON.stringify(validateObservations(v, bound, '2020-01-01T00:00:00Z')).includes('secret'));
});
test('pending observations cannot smuggle arbitrary evidence strings', () => {
  const v = observations(); v.cases[observationIds[0]] = { status: 'pending', evidenceId: 'secret', observedAtUtc: null };
  assert.throws(() => validateObservations(v, bound, '2020-01-01T00:00:00Z'));
});
test('operator dates and evidence UUIDs are validated; future and old observations fail', () => {
  for (const value of ['invalid', '2099-01-01T00:00:00Z', '2000-01-01T00:00:00Z']) { const v = observations(); v.cases[observationIds[0]].observedAtUtc = value; assert.throws(() => validateObservations(v, bound, '2020-01-01T00:00:00Z')); }
  const v = observations(); v.cases[observationIds[0]].evidenceId = 'handwritten'; assert.throws(() => validateObservations(v, bound, '2020-01-01T00:00:00Z'));
});
function ciProof() { return [{ head_sha: stamp.revision, status: 'completed', conclusion: 'success', event: 'workflow_dispatch', path: '.github/workflows/next-iteration-release-verification.yml', updated_at: new Date().toISOString(), id: 123, run_attempt: 1 }, { total_count: 3, jobs: ['Windows source and POS', 'Linux source and portals', 'Joined financial recovery'].map(name => ({ name, status: 'completed', conclusion: 'success' })) }]; }
test('CI requires the exact source dispatch and all three actually successful jobs', () => { const [run, jobs] = ciProof(); assert.equal(validateCi(run, jobs, stamp.revision, '2020-01-01T00:00:00Z').kind, 'github-api-verified'); });
for (const key of ['head_sha', 'conclusion', 'event', 'path']) test(`CI mismatched ${key} fails`, () => { const [run, jobs] = ciProof(); run[key] = 'other'; assert.throws(() => validateCi(run, jobs, stamp.revision, '2020-01-01T00:00:00Z')); });
test('missing/skipped CI jobs and truncated API pagination cannot pass', () => {
  for (const mode of ['missing', 'skipped', 'truncated']) { const [run, jobs] = ciProof(); if (mode === 'missing') { jobs.jobs.pop(); jobs.total_count--; } if (mode === 'skipped') jobs.jobs[0].conclusion = 'skipped'; if (mode === 'truncated') jobs.total_count = 4; assert.throws(() => validateCi(run, jobs, stamp.revision, '2020-01-01T00:00:00Z')); }
});
function verification() { const checks = Object.fromEntries(sourceChecks.map(k => [k, { passed: true, count: 1 }])); for (const [k,v] of Object.entries({ unit: 937, architecture: 6, 'http-boundaries': 12, 'kitchen-browser': 19, 'frontend-unit': 76, 'gallery-browser': 7, 'ui-browser': 10, 'hosting-guards': 46, 'release-guards': 41, 'identity-guards': 10, 'installer-guards': 15 })) checks[k].count = v; return { version: 1, source: stamp, profile: 'Source', passed: true, completedAtUtc: new Date().toISOString(), checks }; }
test('PowerShell UTC receipts are accepted but source success does not imply joined financial/recovery completion', () => { const v = verification(); v.completedAtUtc = new Date().toISOString().replace('Z', '0000+00:00'); validateVerification(v, { source: stamp, createdAtUtc: '2020-01-01T00:00:00Z' }); assert.throws(() => validateVerification(v, { source: stamp, createdAtUtc: '2020-01-01T00:00:00Z' }, true)); });
test('missing/skipped/partial unit inventory and stale source are rejected', () => {
  for (const mode of ['missing', 'failed', 'count', 'stale']) { const v = verification(); if (mode === 'missing') delete v.checks['http-boundaries']; if (mode === 'failed') v.checks.unit.passed = 'true'; if (mode === 'count') v.checks['kitchen-browser'].count = 1; if (mode === 'stale') v.source = { ...stamp, inputHash: '0'.repeat(64) }; assert.throws(() => validateVerification(v, { source: stamp, createdAtUtc: '2020-01-01T00:00:00Z' })); }
});
test('joined results require every cleanup proof', () => {
  const v = verification(); v.profile = 'Joined'; for (const k of ['checkout-restore', 'settlement-reporting', 'settlement-replay', 'historical-cutoff', 'gallery-persistence']) v.checks[k] = { passed: true, count: 1, cleanupVerified: true };
  validateVerification(v, { source: stamp, createdAtUtc: '2020-01-01T00:00:00Z' }, true); v.checks['checkout-restore'].cleanupVerified = false; assert.throws(() => validateVerification(v, { source: stamp, createdAtUtc: '2020-01-01T00:00:00Z' }, true));
});
test('shared source binding detects source, dirty state and even packaging input changes', t => {
  const d = fixture(t); function git(args) { const r = spawnSync('git', args, { cwd: d, encoding: 'utf8' }); assert.equal(r.status, 0); }
  git(['init', '-q']); fs.writeFileSync(path.join(d, '.gitattributes'), '*.cs text eol=lf\n'); fs.writeFileSync(path.join(d, 'sample.cs'), 'line\n'); git(['add', '.']); git(['-c','user.name=Fixture','-c','user.email=fixture@nexa.test','commit','-qm','Synthetic source fixture']);
  const clean = source(d); assert.equal(clean.dirty, false); assert(sameSource(clean, source(d)));
  fs.writeFileSync(path.join(d, 'sample.cs'), 'line\r\n'); assert.equal(source(d).inputHash, clean.inputHash);
  fs.writeFileSync(path.join(d, 'installer.iss'), 'new packaging'); assert.notEqual(source(d).inputHash, clean.inputHash); assert(!sameSource(clean, source(d)));
});
function artifacts(d) {
  const bundle = path.join(d, 'bundle'), installer = path.join(d, 'installer'); fs.mkdirSync(bundle); fs.mkdirSync(installer);
  for (const s of inventory) { const out = path.join(bundle, s.id); fs.mkdirSync(out); fs.writeFileSync(path.join(out, s.assembly + '.dll'), 'synthetic'); if (s.spa) { fs.mkdirSync(path.join(out, 'wwwroot')); fs.writeFileSync(path.join(out, 'wwwroot/index.html'), 'synthetic'); } }
  const schemas = { POS: 23 }; const m = { version: 1, sourceRevision: stamp.revision, releaseSource: stamp, runtime: 'linux-x64', schemas, files: files(bundle) }; fs.writeFileSync(path.join(bundle, 'manifest.json'), JSON.stringify(m));
  const payload = path.join(installer, 'payload'); fs.mkdirSync(payload); fs.writeFileSync(path.join(payload, 'coreclr.dll'), 'synthetic'); fs.writeFileSync(path.join(payload, 'NexaConnect.POS.runtimeconfig.json'), JSON.stringify({ runtimeOptions: { includedFrameworks: [{ name: 'Microsoft.WindowsDesktop.App' }] } }));
  fs.mkdirSync(path.join(installer, 'installer')); const setup = path.join(installer, 'installer/NexaConnect-POS-0.7.0-win-x64-setup.exe'); fs.writeFileSync(setup, 'synthetic');
  const i = { format: 1, releaseSource: stamp, packageVersion: '0.7.0', privateUnsigned: true, unsignedPayloadPeCount: 2, payloadFiles: Object.fromEntries(files(payload).map(f => [f.path, f.sha256])), setupSha256: sha(fs.readFileSync(setup)) }; fs.writeFileSync(path.join(installer, 'installer-candidate.json'), JSON.stringify(i)); return { bundle, installer, c: { source: stamp, schemas } };
}
test('binding uses actual bundle and installer bytes; unsigned stays marked unsigned', t => { const a = artifacts(fixture(t)); const p = inspectArtifacts(a.c, a.bundle, a.installer); assert.equal(p.privateUnsigned, true); fs.appendFileSync(path.join(a.installer, 'payload/coreclr.dll'), 'changed'); assert.throws(() => inspectArtifacts(a.c, a.bundle, a.installer)); });
test('old pre-shared-source artifacts and wrong schema targets cannot bind', t => { const a = artifacts(fixture(t)); assert.throws(() => inspectArtifacts({ ...a.c, source: { ...stamp, inputHash: '0'.repeat(64) } }, a.bundle, a.installer)); assert.throws(() => inspectArtifacts({ ...a.c, schemas: { POS: 24 } }, a.bundle, a.installer)); });
test('legacy bundle inventory ordering stays readable without relaxing byte integrity', t => {
  const a = artifacts(fixture(t)), file = path.join(a.bundle, 'manifest.json'), m = JSON.parse(fs.readFileSync(file)); m.files.reverse(); fs.writeFileSync(file, JSON.stringify(m));
  inspectArtifacts(a.c, a.bundle, a.installer);
  m.files[0].sha256 = '0'.repeat(64); fs.writeFileSync(file, JSON.stringify(m)); assert.throws(() => inspectArtifacts(a.c, a.bundle, a.installer));
});
test('unbound release cannot create frozen or UAT output and cannot overwrite previous records', t => {
  const root = fixture(t), d = path.join(root, 'candidate'), out = path.join(root, 'uat'); create(d);
  assert.equal(status(d).readyToFreeze, false); assert.throws(() => freeze(d)); assert(!fs.existsSync(path.join(d, 'frozen.json')));
  assert.throws(() => prepareUat(d, out)); assert(!fs.existsSync(out)); assert.throws(() => create(d));
});
test('second test catalog covers agreed scope and original next-day observation', () => { assert.equal(new Set(uatIds).size, uatIds.length); for (const id of ['dual-displays-hotplug','approved-gallery-fallback','singapore-next-day','settlement-restart-ledger-csv']) assert(uatIds.includes(id)); });
test('TAP parser refuses skips/todo even when the command exited successfully', () => {
  const report = '# tests 3\n# pass 3\n# fail 0\n# cancelled 0\n# skipped 0\n# todo 0\n'; assert.equal(tap(report, 3), 3);
  assert.throws(() => tap(report.replace('# skipped 0', '# skipped 1'), 3)); assert.throws(() => tap(report.replace('# todo 0', '# todo 1'), 3)); assert.throws(() => tap(report, 4));
});
test('browser parser rejects expected failures, retries and forged aggregate success', () => {
  const report = { stats: { expected: 1, skipped: 0, flaky: 0, unexpected: 0 }, suites: [{ specs: [{ tests: [{ status: 'expected', expectedStatus: 'passed', results: [{ status: 'passed', retry: 0 }] }] }] }] };
  assert.equal(playwright(report, 1), 1);
  const t = report.suites[0].specs[0].tests[0]; t.expectedStatus = 'failed'; assert.throws(() => playwright(report, 1)); t.expectedStatus = 'passed'; t.results.push({ status: 'passed', retry: 1 }); assert.throws(() => playwright(report, 1));
  assert.throws(() => playwright({ stats: report.stats }, 1));
});
test('frontend unit parser rejects todo, skipped and partial reports', () => {
  const r = { success: true, numTotalTests: 76, numPassedTests: 76, numFailedTests: 0, numPendingTests: 0, numTodoTests: 0 }; assert.equal(vitest(r, 76), 76);
  assert.throws(() => vitest({ ...r, numTodoTests: 1 }, 76)); assert.throws(() => vitest({ ...r, numPassedTests: 75 }, 76));
});
test('UAT handoff rejects changed frozen fields even with an unchanged artifact digest', () => {
  const c = { id: randomUUID(), idHash: bound.candidateHash, source: stamp, schemas: { POS: 23 }, createdAtUtc: '2020-01-01T00:00:00Z' };
  const artifacts = { digest: bound.artifactsDigest, setupSha256: 'a'.repeat(64) };
  const frozen = { version: 1, releaseId: c.id, candidateHash: c.idHash, source: stamp, schemas: c.schemas, artifacts, configurationDigest: bound.configurationDigest, evidenceDigest: 'f'.repeat(64), acceptance: 'pending-user-test', frozenAtUtc: new Date().toISOString() };
  const validate = value => validateFrozenBinding(value, c, artifacts, bound.configurationDigest, 'f'.repeat(64)); validate(frozen);
  assert.throws(() => validate({ ...frozen, artifacts: { ...artifacts, setupSha256: '0'.repeat(64) } }));
  assert.throws(() => validate({ ...frozen, schemas: { POS: 24 } })); assert.throws(() => validate({ ...frozen, acceptance: 'accepted' }));
});
