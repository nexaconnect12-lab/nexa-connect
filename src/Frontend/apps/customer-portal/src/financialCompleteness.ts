export type FinancialCounts = { expected:number; matched:number; missing:number; conflicting:number; unexpected:number; gaps:number };
export type FinancialObservation = {
  checkId:string; status:"observed_complete"|"gaps_detected";
  range:{organizationId:string;branchId:string;fromUtc:string;toUtc:string};
  checkedAtUtc:string; orderObservedAtUtc:string; refundObservedAtUtc:string;
  saleEvidenceGaps:number; refundEvidenceGaps:number; unretainedSales:number; unretainedRefunds:number;
  sales:FinancialCounts; payments:FinancialCounts; refunds:FinancialCounts;
};
export type Completeness = { status:"not_checked"; observation:null } |
  { status:"observed_complete"|"gaps_detected"; observation:FinancialObservation };
export const branchUuid=/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function reportWindow(branch:string, from:string, to:string, now=Date.now()) {
  const start=Date.parse(from+"Z"),end=Date.parse(to+"Z");
  if(!branchUuid.test(branch)||/^0{8}(-0{4}){3}-0{12}$/.test(branch)||!Number.isFinite(start)||!Number.isFinite(end)
    ||end<=start||end-start>31*86400000||end>now)return undefined;
  return {branchId:branch,fromUtc:new Date(start).toISOString(),toUtc:new Date(end).toISOString()};
}

// Fail closed on unknown status, mismatched scope, or internally inconsistent financial evidence.
export function readCompleteness(value:unknown, organizationId:string, window:{branchId:string;fromUtc:string;toUtc:string}):Completeness {
  const fail=()=>{throw new Error("Financial observation is invalid.");};
  if(!value||typeof value!=="object")return fail();
  const result=value as Completeness;
  if(result.status==="not_checked")return result.observation===null?result:fail();
  if(result.status!=="observed_complete"&&result.status!=="gaps_detected")return fail();
  const item=result.observation;
  if(!item||item.status!==result.status||!item.range||item.range.organizationId.toLowerCase()!==organizationId.toLowerCase()
    ||item.range.branchId.toLowerCase()!==window.branchId.toLowerCase()
    ||Date.parse(item.range.fromUtc)!==Date.parse(window.fromUtc)||Date.parse(item.range.toUtc)!==Date.parse(window.toUtc)
    ||typeof item.checkId!=="string"||!branchUuid.test(item.checkId)
    ||![item.checkedAtUtc,item.orderObservedAtUtc,item.refundObservedAtUtc].every(time=>typeof time==="string"&&Number.isFinite(Date.parse(time))))return fail();
  const numbers=[item.saleEvidenceGaps,item.refundEvidenceGaps,item.unretainedSales,item.unretainedRefunds];
  let gaps=0;
  for(const counts of [item.sales,item.payments,item.refunds]){
    if(!counts||![counts.expected,counts.matched,counts.missing,counts.conflicting,counts.unexpected,counts.gaps].every(n=>Number.isSafeInteger(n)&&n>=0)
      ||counts.matched+counts.missing+counts.conflicting!==counts.expected||counts.gaps!==counts.missing+counts.conflicting+counts.unexpected)return fail();
    gaps+=counts.gaps;
  }
  if(!numbers.every(n=>Number.isSafeInteger(n)&&n>=0)||
    ((gaps+numbers.reduce((a,b)=>a+b,0)===0)!==(result.status==="observed_complete")))return fail();
  return result;
}
