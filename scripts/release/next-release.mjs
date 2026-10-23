import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { repository, source, sameSource, sha } from './source.mjs';
import { files, inventory, verifyBundle, validateInputs } from '../../docker/pilot/hosting.mjs';

export const observationIds = ['hosted-login-denial', 'remote-pos-kitchen', 'hosted-reboot-outage', 'offserver-full-graph-restore', 'alert-delivery', 'measured-capacity', 'installer-roundtrip-callback', 'installer-interruption-recovery'];
export const uatIds = ['owner-customer-isolation', 'kitchen-direct', 'cashier-lock-resume', 'dual-displays-hotplug', 'approved-gallery-fallback', 'installer-update', 'cash-checkout', 'manual-promptpay', 'kitchen-transitions', 'receipt-pdf-reprint', 'original-command-recovery', 'cash-variance', 'scope-revocation-denial', 'singapore-next-day', 'settlement-restart-ledger-csv'];
export class Invalid extends Error {}
function requireValue(ok, message) { if (!ok) throw new Invalid(message); }
const digest = file => sha(fs.readFileSync(file));
export function read(file) { requireValue(fs.statSync(file).size <= 16 * 1024 * 1024, 'Evidence file exceeds size limit.'); return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '')); }
const write = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
function noLinks(p) { for (let q = path.resolve(p);; q = path.dirname(q)) { requireValue(!fs.existsSync(q) || !fs.lstatSync(q).isSymbolicLink(), 'Release paths cannot traverse links.'); if (path.dirname(q) === q) break; } }
function fresh(dir) { noLinks(dir); requireValue(!fs.existsSync(dir), 'Use a fresh output directory.'); fs.mkdirSync(dir, { recursive: true, mode: 0o700 }); }
function execute(command, args) {
  const r = spawnSync(command, args, { cwd: repository, encoding: 'utf8', timeout: 60000, maxBuffer: 16 * 1024 * 1024, windowsHide: true });
  requireValue(r.status === 0, 'Evidence verification command failed; inspect private inputs locally.'); return r.stdout;
}
function time(value, minimum = '1970-01-01T00:00:00Z') {
  return typeof value === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value)) && Date.parse(value) >= Date.parse(minimum) && Date.parse(value) <= Date.now();
}
const uuid = v => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(v);
export function catalog(root = repository) {
  const base = path.join(root, 'src/Tools/NexaConnect.DataMigration/Scripts');
  return Object.fromEntries(fs.readdirSync(base).filter(d => fs.statSync(path.join(base, d)).isDirectory()).sort().map(d => [d, Math.max(...fs.readdirSync(path.join(base, d)).map(v => read(path.join(base, d, v, 'migration.json')).version))]));
}
function candidate(dir) {
  noLinks(dir); const c = read(path.join(dir, 'candidate.json'));
  requireValue(c.version === 1 && uuid(c.id) && time(c.createdAtUtc) && c.source?.version === 1 && typeof c.source.dirty === 'boolean', 'Invalid candidate.');
  requireValue(c.idHash === sha(JSON.stringify({ id: c.id, source: c.source, schemas: c.schemas })), 'Candidate binding changed.'); return c;
}
export function create(dir) {
  fresh(dir);
  if (process.platform === 'win32') execute('pwsh', ['-NoProfile', '-File', 'scripts/release/protect.ps1', '-Directory', path.resolve(dir)]);
  const c = { version: 1, id: randomUUID(), createdAtUtc: new Date().toISOString(), source: source(), schemas: catalog() };
  c.idHash = sha(JSON.stringify({ id: c.id, source: c.source, schemas: c.schemas }));
  write(path.join(dir, 'candidate.json'), c); return c;
}
function ensureCurrent(c) { requireValue(sameSource(c.source, source()) && JSON.stringify(c.schemas) === JSON.stringify(catalog()), 'Candidate inputs changed; create a replacement candidate.'); }

export function inspectArtifacts(c, bundle, installer) {
  noLinks(bundle); noLinks(installer);
  const b = verifyBundle(bundle), i = read(path.join(installer, 'installer-candidate.json'));
  requireValue(sameSource(b.releaseSource, c.source) && sameSource(i.releaseSource, c.source), 'Artifacts are stale or lack the shared release source binding; rebuild both.');
  requireValue(JSON.stringify(Object.entries(b.schemas).sort()) === JSON.stringify(Object.entries(c.schemas).sort()), 'Bundle schema targets differ.');
  requireValue(i.format === 1 && /^\d{1,4}\.\d{1,4}\.\d{1,4}$/.test(i.packageVersion) && typeof i.privateUnsigned === 'boolean' && Number.isInteger(i.unsignedPayloadPeCount), 'Invalid installer receipt.');
  const payload = files(path.join(installer, 'payload'));
  requireValue(payload.length > 0 && payload.length === Object.keys(i.payloadFiles).length && payload.every(f => i.payloadFiles[f.path]?.toLowerCase() === f.sha256), 'Installer payload inventory changed.');
  requireValue(!payload.some(f => /(^|\/)(appsettings.*\.json|tokens\.bin|pos-state\.db)$|\.pdb$/i.test(f.path)), 'Installer contains forbidden configuration or state.');
  const setup = path.join(installer, `installer/NexaConnect-POS-${i.packageVersion}-win-x64-setup.exe`);
  requireValue(digest(setup) === i.setupSha256.toLowerCase(), 'Installer executable changed.');
  const runtime = read(path.join(installer, 'payload/NexaConnect.POS.runtimeconfig.json')).runtimeOptions;
  requireValue(runtime?.includedFrameworks?.some(f => f.name === 'Microsoft.WindowsDesktop.App') && payload.some(f => f.path === 'coreclr.dll'), 'Installer is not a self-contained Windows Desktop payload.');
  const proof = { bundleManifestSha256: digest(path.join(bundle, 'manifest.json')), installerReceiptSha256: digest(path.join(installer, 'installer-candidate.json')), setupSha256: digest(setup), packageVersion: i.packageVersion, privateUnsigned: i.privateUnsigned, unsignedPayloadPeCount: i.unsignedPayloadPeCount };
  return { ...proof, digest: sha(JSON.stringify(proof)) };
}
export function bind(dir, bundle, installer) {
  const c = candidate(dir); ensureCurrent(c);
  const proof = inspectArtifacts(c, bundle, installer);
  write(path.join(dir, 'artifacts.json'), { version: 1, candidateHash: c.idHash, bundle: path.resolve(bundle), installer: path.resolve(installer), proof }); return proof;
}
function artifactBinding(dir, c) {
  const a = read(path.join(dir, 'artifacts.json')); requireValue(a.candidateHash === c.idHash, 'Artifact candidate changed.');
  const proof = inspectArtifacts(c, a.bundle, a.installer); requireValue(JSON.stringify(proof) === JSON.stringify(a.proof), 'Bound artifacts changed.'); return a;
}
function configProof(plan, secure) {
  noLinks(plan); noLinks(secure); const p = read(plan); validateInputs(p, secure);
  return { planSha256: digest(plan), settings: Object.fromEntries(inventory.map(s => [s.id, Object.fromEntries(['appsettings.Production.json', 'tls.pfx', 'data-protection.pfx'].map(f => [f, digest(path.join(secure, s.id, f))]))])), proxy: ['proxy.crt', 'proxy.key'].map(f => digest(path.join(secure, f))) };
}
export function configure(dir, plan, secure) {
  const c = candidate(dir); ensureCurrent(c); artifactBinding(dir, c);
  write(path.join(dir, 'configuration.json'), { version: 1, candidateHash: c.idHash, plan: path.resolve(plan), secure: path.resolve(secure), proof: configProof(plan, secure) });
}
function configurationBinding(dir, c) {
  const b = read(path.join(dir, 'configuration.json')); requireValue(b.candidateHash === c.idHash, 'Configuration candidate changed.');
  requireValue(JSON.stringify(configProof(b.plan, b.secure)) === JSON.stringify(b.proof), 'Bound configuration/certificates changed.'); return b;
}
export function context(dir) {
  const c = candidate(dir), a = artifactBinding(dir, c), b = configurationBinding(dir, c);
  return { candidateHash: c.idHash, artifactsDigest: a.proof.digest, configurationDigest: sha(JSON.stringify(b.proof)) };
}
export function template(dir, out) {
  const c = candidate(dir); ensureCurrent(c);
  write(out, { version: 1, kind: 'operator-attested', ...context(dir), environmentId: randomUUID(), cases: Object.fromEntries(observationIds.map(id => [id, { status: 'pending', evidenceId: null, observedAtUtc: null }])) });
}
export function validateObservations(value, binding, created) {
  requireValue(value.version === 1 && value.kind === 'operator-attested' && uuid(value.environmentId), 'Invalid operator evidence kind/environment.');
  for (const k of ['candidateHash', 'artifactsDigest', 'configurationDigest']) requireValue(value[k] === binding[k], 'Operator evidence binding differs.');
  requireValue(Object.keys(value.cases ?? {}).sort().join() === [...observationIds].sort().join(), 'Operator case inventory differs.');
  for (const id of observationIds) {
    const v = value.cases[id]; requireValue(['pending', 'passed', 'failed', 'blocked'].includes(v.status), 'Invalid observation status.');
    if (v.status !== 'pending') requireValue(uuid(v.evidenceId) && time(v.observedAtUtc, created), 'Observation requires current bound evidence/time.');
    else requireValue(v.evidenceId === null && v.observedAtUtc === null, 'Pending observations cannot carry arbitrary evidence.');
  }
  // Allow-list: arbitrary notes, filenames, credentials, links and personal data are never persisted.
  return { version: 1, kind: value.kind, ...binding, environmentId: value.environmentId, cases: Object.fromEntries(observationIds.map(id => [id, { status: value.cases[id].status, evidenceId: value.cases[id].evidenceId, observedAtUtc: value.cases[id].observedAtUtc }])) };
}
export function attest(dir, file) {
  const c = candidate(dir); ensureCurrent(c);
  const observation = validateObservations(read(file), context(dir), c.createdAtUtc);
  write(path.join(dir, 'observations.json'), observation);
}

export function addVerification(dir, file) {
  const c = candidate(dir); ensureCurrent(c); const v = read(file);
  requireValue(v.version === 1 && uuid(v.runId) && sameSource(v.source, c.source) && v.passed === true && time(v.completedAtUtc, c.createdAtUtc), 'Verification is failed, stale or unbound.');
  requireValue(['Source', 'Joined'].includes(v.profile), 'Invalid verification profile.');
  validateVerification(v, c, v.profile === 'Joined');
  const target = path.join(dir, `verification-${v.profile.toLowerCase()}.json`);
  write(target, { version: 1, candidateHash: c.idHash, file: path.resolve(file), sha256: digest(file) });
}

export function validateCi(run, jobs, revision, minimum) {
  requireValue(run.head_sha === revision && run.status === 'completed' && run.conclusion === 'success' && run.event === 'workflow_dispatch'
    && run.path === '.github/workflows/next-iteration-release-verification.yml' && time(run.updated_at, minimum), 'CI must be the completed exact-source dispatch of the release workflow.');
  const required = ['Windows source and POS', 'Linux source and portals', 'Joined financial recovery'];
  requireValue(jobs.total_count === jobs.jobs?.length && required.every(n => jobs.jobs.some(j => j.name === n && j.status === 'completed' && j.conclusion === 'success'))
    && jobs.jobs.every(j => j.status === 'completed' && j.conclusion === 'success'), 'CI has missing, skipped, incomplete or failed jobs.');
  return { kind: 'github-api-verified', headSha: revision, runId: run.id, attempt: run.run_attempt, completedAtUtc: run.updated_at, jobs: required };
}
function fetchCi(c, runId) {
  requireValue(!c.source.dirty && /^\d+$/.test(String(runId)), 'CI verification requires a clean candidate and numeric run ID.');
  const remote = execute('git', ['remote', 'get-url', 'origin']).trim();
  const match = remote.match(/^(?:https:\/\/github\.com\/|git@github\.com:)([A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+?)(?:\.git)?$/);
  requireValue(!!match, 'Use the configured GitHub origin for CI verification.'); const repo = match[1];
  const run = JSON.parse(execute('gh', ['api', `repos/${repo}/actions/runs/${runId}`]));
  const jobs = JSON.parse(execute('gh', ['api', `repos/${repo}/actions/runs/${runId}/attempts/${run.run_attempt}/jobs?per_page=100`]));
  requireValue(run.repository?.full_name === repo && Number(run.id) === Number(runId), 'CI repository/run differs.');
  return { version: 1, candidateHash: c.idHash, ...validateCi(run, jobs, c.source.revision, c.createdAtUtc) };
}
export function ci(dir, runId) {
  const c = candidate(dir); ensureCurrent(c); write(path.join(dir, 'ci.json'), fetchCi(c, runId));
}
function confirmCi(dir, c) {
  const recorded = read(path.join(dir, 'ci.json')), live = fetchCi(c, recorded.runId);
  requireValue(live.attempt === recorded.attempt && live.completedAtUtc === recorded.completedAtUtc, 'CI run was rerun/changed; replace the candidate evidence.');
}
function evidenceDigest(dir) {
  const names = ['observations.json', 'ci.json', fs.existsSync(path.join(dir, 'verification-joined.json')) ? 'verification-joined.json' : 'verification-source.json'];
  return sha(JSON.stringify(names.map(n => [n, digest(path.join(dir, n))])));
}

export const sourceChecks = ['unit', 'architecture', 'http-boundaries', 'frontend-check', 'frontend-unit', 'frontend-build', 'audit-production', 'audit-all', 'hosting-guards', 'release-guards', 'identity-guards', 'installer-guards', 'kitchen-browser', 'gallery-browser', 'ui-browser'];
export function validateVerification(v, c, joined = false) {
  requireValue(v.version === 1 && sameSource(v.source, c.source) && v.passed === true && time(v.completedAtUtc, c.createdAtUtc), 'Verification no longer matches candidate.');
  requireValue(sourceChecks.every(k => v.checks?.[k]?.passed === true && Number.isInteger(v.checks[k].count) && v.checks[k].count > 0), 'Verification has skipped or missing source gates.');
  requireValue(v.checks.unit.count >= 937 && v.checks.architecture.count >= 6 && v.checks['http-boundaries'].count >= 12 && v.checks['frontend-unit'].count >= 76 && v.checks['kitchen-browser'].count >= 19 && v.checks['gallery-browser'].count >= 7 && v.checks['ui-browser'].count >= 10 && v.checks['hosting-guards'].count >= 46 && v.checks['release-guards'].count >= 41 && v.checks['identity-guards'].count >= 10 && v.checks['installer-guards'].count >= 15, 'Verification test inventory is incomplete.');
  if (joined) requireValue(v.profile === 'Joined' && ['checkout-restore', 'settlement-reporting', 'settlement-replay', 'historical-cutoff', 'gallery-persistence'].every(k => v.checks[k]?.passed === true && v.checks[k].cleanupVerified === true), 'Complete joined financial/recovery evidence is required.');
}
export function assess(snapshot) {
  const blockers = [];
  if (!snapshot.current) blockers.push('source-changed');
  if (snapshot.dirty) blockers.push('clean-source-required');
  if (!snapshot.artifacts) blockers.push('artifacts-unbound-or-changed');
  else if (snapshot.privateUnsigned || snapshot.unsignedPayloadPeCount !== 0 || !snapshot.signatures) blockers.push('trusted-installer-signing-required');
  if (!snapshot.configuration) blockers.push('hosted-configuration-unbound-or-changed');
  if (!snapshot.sourceTests) blockers.push('source-verification-required');
  if (!snapshot.joinedTests) blockers.push('joined-financial-recovery-required');
  if (!snapshot.ci) blockers.push('exact-clean-source-ci-required');
  for (const id of observationIds) if (snapshot.observations?.[id]?.status !== 'passed') blockers.push(`observation:${id}`);
  return { readyToFreeze: blockers.length === 0, blockers, acceptance: 'pending-user-test' };
}
export function status(dir) {
  const c = candidate(dir), s = { dirty: c.source.dirty, current: sameSource(c.source, source()) && JSON.stringify(c.schemas) === JSON.stringify(catalog()) };
  let a;
  try { a = artifactBinding(dir, c); s.artifacts = true; s.privateUnsigned = a.proof.privateUnsigned; s.unsignedPayloadPeCount = a.proof.unsignedPayloadPeCount; } catch { s.artifacts = false; }
  try { configurationBinding(dir, c); s.configuration = true; } catch { s.configuration = false; }
  for (const profile of ['source', 'joined']) {
    try { const b = read(path.join(dir, `verification-${profile}.json`)); requireValue(b.candidateHash === c.idHash && digest(b.file) === b.sha256, 'Verification receipt changed.'); validateVerification(read(b.file), c, profile === 'joined'); s[profile === 'source' ? 'sourceTests' : 'joinedTests'] = true; } catch { /* Pending is a blocker, never silently passed. */ }
  }
  if (s.joinedTests) s.sourceTests = true;
  try { const proof = read(path.join(dir, 'ci.json')); s.ci = proof.candidateHash === c.idHash && proof.kind === 'github-api-verified' && proof.headSha === c.source.revision && time(proof.completedAtUtc, c.createdAtUtc); } catch {}
  if (a && !s.privateUnsigned && s.unsignedPayloadPeCount === 0 && process.platform === 'win32') {
    try { execute('pwsh', ['-NoProfile', '-File', 'scripts/release/verify-signatures.ps1', '-InstallerDirectory', a.installer]); s.signatures = true; } catch {}
  }
  try { const o = validateObservations(read(path.join(dir, 'observations.json')), context(dir), c.createdAtUtc); s.observations = o.cases; } catch {}
  return { version: 1, releaseId: c.id, candidateHash: c.idHash, revision: c.source.revision, inputHash: c.source.inputHash, ...assess(s) };
}
export function freeze(dir) {
  const c = candidate(dir), report = status(dir); requireValue(report.readyToFreeze, 'Release freeze blocked; run status for the explicit prerequisites.');
  confirmCi(dir, c);
  const a = artifactBinding(dir, c), b = configurationBinding(dir, c);
  const manifest = { version: 1, releaseId: c.id, candidateHash: c.idHash, source: c.source, schemas: c.schemas, artifacts: a.proof, configurationDigest: sha(JSON.stringify(b.proof)), evidenceDigest: evidenceDigest(dir), frozenAtUtc: new Date().toISOString(), acceptance: 'pending-user-test' };
  write(path.join(dir, 'frozen.json'), manifest); return manifest;
}
export function validateFrozenBinding(frozen, c, artifacts, configurationDigest, evidence) {
  requireValue(frozen.version === 1 && frozen.releaseId === c.id && frozen.candidateHash === c.idHash
    && sameSource(frozen.source, c.source) && JSON.stringify(frozen.schemas) === JSON.stringify(c.schemas)
    && JSON.stringify(frozen.artifacts) === JSON.stringify(artifacts) && frozen.configurationDigest === configurationDigest
    && frozen.evidenceDigest === evidence && frozen.acceptance === 'pending-user-test' && time(frozen.frozenAtUtc, c.createdAtUtc), 'Frozen manifest binding changed.');
}
export function prepareUat(dir, out) {
  const c = candidate(dir), frozen = read(path.join(dir, 'frozen.json')); requireValue(status(dir).readyToFreeze, 'Frozen candidate is absent or no longer valid.');
  validateFrozenBinding(frozen, c, artifactBinding(dir, c).proof, context(dir).configurationDigest, evidenceDigest(dir));
  confirmCi(dir, c);
  fresh(out);
  if (process.platform === 'win32') execute('pwsh', ['-NoProfile', '-File', 'scripts/release/protect.ps1', '-Directory', path.resolve(out)]);
  const record = { version: 1, id: randomUUID(), releaseId: c.id, frozenManifestSha256: digest(path.join(dir, 'frozen.json')), candidateHash: c.idHash, setupSha256: frozen.artifacts.setupSha256, acceptance: 'pending-user-decision', cases: Object.fromEntries(uatIds.map(id => [id, { status: 'pending', kind: 'operator-attested', evidenceId: null }])) };
  write(path.join(out, 'second-test.json'), record);
  fs.writeFileSync(path.join(out, 'second-test.md'), '# Consolidated second test\n\nCandidate: ' + c.id + '\n\nUse docs/Deployment/Next-Iteration-Phase-8.md. Record actual device/PDF and Singapore next-day observations; never substitute fixtures or accept this record automatically.\n\n' + uatIds.map(id => `- [ ] ${id}`).join('\n') + '\n', { flag: 'wx', mode: 0o600 }); return record;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [cmd, ...a] = process.argv.slice(2); let result;
    if (cmd === 'init' && a.length === 1) result = create(path.resolve(a[0]));
    else if (cmd === 'bind' && a.length === 3) result = bind(...a);
    else if (cmd === 'configure' && a.length === 3) configure(...a);
    else if (cmd === 'template' && a.length === 2) template(...a);
    else if (cmd === 'attest' && a.length === 2) attest(...a);
    else if (cmd === 'add-verification' && a.length === 2) addVerification(...a);
    else if (cmd === 'ci' && a.length === 2) ci(...a);
    else if (cmd === 'status' && a.length === 1) result = status(...a);
    else if (cmd === 'freeze' && a.length === 1) result = freeze(...a);
    else if (cmd === 'prepare-uat' && a.length === 2) result = prepareUat(...a);
    else throw new Invalid('Usage: next-release.mjs init|bind|configure|template|attest|add-verification|ci|status|freeze|prepare-uat (see release README).');
    process.stdout.write(JSON.stringify(result ? (cmd === 'init' ? { releaseId: result.id, source: result.source } : result) : { completed: true, deployed: false }) + '\n');
  } catch (e) { process.stderr.write((e instanceof Invalid ? e.message : 'Release operation failed; inspect private inputs locally. No raw configuration was emitted.') + '\n'); process.exitCode = 1; }
}
