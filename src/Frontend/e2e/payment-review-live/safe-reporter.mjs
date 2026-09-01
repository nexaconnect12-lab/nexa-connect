import {mkdirSync,writeFileSync} from "node:fs";
export function completeEvidence(status,results){return status==="passed"&&results.length===6&&new Set(results.map(x=>x.title)).size===6&&results.every(x=>x.status==="passed");}
// Do not serialize Playwright errors, HTTP bodies, settings, usernames or credentials.
export default class SafeReporter{
  constructor(options){this.runId=options.runId;this.results=[];}
  onTestEnd(test,result){this.results.push({title:test.title,status:result.status});process.stdout.write(`${result.status}: ${test.title}\n`);}
  onError(){process.stderr.write("Live acceptance failed outside a test; inspect the local stack using safe correlation logs.\n");}
  async onEnd(result){const passed=this.results.filter(x=>x.status==="passed").length;const verified=completeEvidence(result.status,this.results);const summary={runId:this.runId,status:verified?"passed":"failed",passed,total:this.results.length,verified,completedAtUtc:new Date().toISOString()};const path=`test-results/payment-review-live/${this.runId}`;mkdirSync(path,{recursive:true});writeFileSync(`${path}/summary.json`,JSON.stringify(summary,null,2));process.stdout.write(`Live acceptance: ${passed}/${this.results.length}; ${summary.status}. Sanitized summary: ${path}/summary.json\n`);return {status:summary.status};}
}
