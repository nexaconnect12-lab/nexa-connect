import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
export const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
export const sha = value => createHash('sha256').update(value).digest('hex');
function git(root, args, input) {
  const r = spawnSync('git', args, { cwd: root, input, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  if (r.status !== 0) throw new Error('Source inventory failed.');
  return r.stdout;
}
export function source(root = repository) {
  const revision = git(root, ['rev-parse', 'HEAD']).trim();
  const names = [...new Set(git(root, ['ls-files', '-co', '--exclude-standard', '-z']).split('\0').filter(f => f && !f.endsWith('.md')))].sort();
  if (!/^[a-f0-9]{40}$/.test(revision) || !names.length || names.some(f => /[\r\n]/.test(f) || !fs.existsSync(path.join(root, f)))) throw new Error('Invalid source inventory.');
  // Git applies repository clean filters/attributes: identities survive checkout EOL differences.
  const hashes = git(root, ['hash-object', '--stdin-paths'], names.join('\n') + '\n').trim().split(/\r?\n/);
  if (hashes.length !== names.length || hashes.some(v => !/^[a-f0-9]{40}$/.test(v))) throw new Error('Invalid source hashes.');
  return { version: 1, revision, dirty: git(root, ['status', '--porcelain']).length > 0, inputHash: sha(JSON.stringify(names.map((p, i) => [p, hashes[i]]))), fileCount: names.length };
}
export function sameSource(a, b) {
  return !!a && !!b && a.version === 1 && b.version === 1 && a.revision === b.revision && a.inputHash === b.inputHash && a.dirty === b.dirty && typeof a.dirty === 'boolean';
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { process.stdout.write(JSON.stringify(source()) + '\n'); }
  catch { process.stderr.write('Source inventory failed; no configuration was emitted.\n'); process.exitCode = 1; }
}
