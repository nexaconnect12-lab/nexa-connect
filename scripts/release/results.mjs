import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
function check(ok) { if (!ok) throw new Error('Incomplete, skipped, retried or failed test results.'); }
export function tap(text, minimum) {
  const value = name => { const all = [...text.matchAll(new RegExp(`^# ${name} (\\d+)\\s*$`, 'gm'))]; check(all.length > 0); return Number(all.at(-1)[1]); };
  const total = value('tests'); check(total >= minimum && value('pass') === total);
  for (const name of ['fail','cancelled','skipped','todo']) check(value(name) === 0); return total;
}
export function vitest(r, minimum) {
  check(r.success === true && Number.isInteger(r.numTotalTests) && r.numTotalTests >= minimum && r.numPassedTests === r.numTotalTests && r.numFailedTests === 0 && r.numPendingTests === 0 && (r.numTodoTests ?? 0) === 0); return r.numTotalTests;
}
export function playwright(r, minimum) {
  const tests = [];
  function visit(suite) { for (const spec of suite.specs ?? []) tests.push(...(spec.tests ?? [])); for (const s of suite.suites ?? []) visit(s); }
  visit(r); const n = r.stats?.expected;
  check(Number.isInteger(n) && n >= minimum && n === tests.length && r.stats.skipped === 0 && r.stats.flaky === 0 && r.stats.unexpected === 0 && (r.errors ?? []).length === 0);
  check(tests.every(t => t.expectedStatus === 'passed' && t.status === 'expected' && t.results?.length === 1 && t.results[0].status === 'passed' && t.results[0].retry === 0)); return n;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [kind, file, min] = process.argv.slice(2); check(['tap','vitest','playwright'].includes(kind) && Number(min) >= 1 && fs.statSync(file).size <= 32 * 1024 * 1024);
    const text = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''); const input = kind === 'tap' ? text : JSON.parse(text);
    process.stdout.write(String(({ tap, vitest, playwright })[kind](input, Number(min))) + '\n');
  } catch { process.stderr.write('Test result validation failed; no raw report emitted.\n'); process.exitCode = 1; }
}
