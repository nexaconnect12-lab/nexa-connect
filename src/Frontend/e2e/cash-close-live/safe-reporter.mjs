import {mkdirSync,writeFileSync} from 'node:fs';
export function completeEvidence(status,results){return status==='passed'&&results.length===5&&new Set(results.map(x=>x.title)).size===5&&results.every(x=>x.status==='passed');}
export default class SafeReporter{
  constructor(options){this.runId=options.runId;this.results=[];}
  onTestEnd(test,result){this.results.push({title:test.title,status:result.status});const line=result.error?.stack?.match(/cash-close\.spec\.mjs:(\d+):\d+/)?.[1];process.stdout.write(`${result.status}: ${test.title}${line?` (scenario line ${line})`:''}\n`);}
  onError(){process.stderr.write('Cash-close acceptance failed; sensitive diagnostics suppressed.\n');}
  onEnd(result){const verified=completeEvidence(result.status,this.results),passed=this.results.filter(x=>x.status==='passed').length;
    const path=`test-results/cash-close-live/${this.runId}`;mkdirSync(path,{recursive:true});
    writeFileSync(`${path}/summary.json`,JSON.stringify({runId:this.runId,verified,passed,total:this.results.length,completedAtUtc:new Date().toISOString()},null,2));
    return {status:verified?'passed':'failed'};}
}
