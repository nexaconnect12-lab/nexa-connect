import {it,expect} from "vitest";
import {readSealedPreparation} from "./dayClosePreparation";
const org="33333333-3333-3333-3333-333333333333",branch="11111111-1111-1111-1111-111111111111",date="2026-09-01";
const ref={manifestId:branch,generation:1,evidenceVersion:"a".repeat(64),revisionEpoch:org,sourceRevision:0};
const sealed={sealId:org,manifestId:branch,revisionEpoch:org,sourceRevision:0};
const fixture=()=>({identity:{organizationId:org,restaurantId:org,branchId:branch,businessDate:date},version:2,status:"ready_for_review",snapshot:{timeZone:"UTC",currency:"THB",fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",grossSales:100,completedRefunds:0,netSales:100,cashVariance:0,tenders:[],orderVersion:"a".repeat(64),paymentVersion:"b".repeat(64),posVersion:"c".repeat(64),unresolvedOrders:0,unresolvedPayments:0,unresolvedRefunds:0,openShifts:0,openCashSessions:0,pendingCashReviews:0,issues:[],observedAtUtc:"2026-09-03T00:00:00Z",cutoff:{order:ref,payment:ref,pos:ref,checkId:org,sourcesCurrent:true,financialGaps:0,deliveryComplete:true,evidenceProtocolVersion:2},seals:{order:sealed,payment:sealed,pos:sealed,pendingChanges:0,journalComplete:true,deliveryComplete:true}},blockers:[],validatedAtUtc:"2026-09-03T00:00:01Z",canPrepare:true,pendingCommand:null,pendingSealChanges:0});
it("requires matching immutable seals delivery and an unchanged journal before showing sealed readiness",()=>{
 const f=fixture();expect(readSealedPreparation(f,org,branch,date).pendingSealChanges).toBe(0);
 for(const seals of [null,{...f.snapshot.seals,pendingChanges:1},{...f.snapshot.seals,journalComplete:false},{...f.snapshot.seals,deliveryComplete:false},{...f.snapshot.seals,order:{...sealed,sourceRevision:1}}])
  expect(()=>readSealedPreparation({...f,snapshot:{...f.snapshot,seals}},org,branch,date)).toThrow();
 expect(()=>readSealedPreparation({...f,pendingSealChanges:2},org,branch,date)).toThrow();
});
it("retains the original seal snapshot while exposing pending changes from the last validation",()=>{
 const f=fixture();const read=readSealedPreparation({...f,status:"blocked",pendingSealChanges:4,blockers:["sealed_changes_pending"]},org,branch,date);
 expect(read.snapshot?.seals?.pendingChanges).toBe(0);expect(read.pendingSealChanges).toBe(4);
});
it("resumed commands must retain their reviewed cutoff version",()=>{
 const f=fixture(),pendingCommand={branchId:branch,businessDate:date,operationId:org,expectedVersion:0,reasonCode:"routine_close"};
 expect(()=>readSealedPreparation({...f,status:"preparing",pendingCommand},org,branch,date)).toThrow();
 expect(readSealedPreparation({...f,status:"preparing",pendingCommand:{...pendingCommand,reviewedCutoffVersion:2}},org,branch,date).pendingCommand?.reviewedCutoffVersion).toBe(2);
});
it("keeps the baseline and validates current reconciliation money and unattributed changes",()=>{
 const f=fixture(),latestSealComparison={grossSales:120,completedRefunds:5,netSales:115,cashVariance:-10,tenders:[{method:"cash",currency:"THB",amount:120}],unknownChanges:0,checkedAtUtc:"2026-09-03T00:00:01Z"};
 const result=readSealedPreparation({...f,status:"blocked",pendingSealChanges:1,latestSealComparison},org,branch,date);
 expect(result.snapshot?.grossSales).toBe(100);expect(result.latestSealComparison?.grossSales).toBe(120);
 expect(()=>readSealedPreparation({...f,latestSealComparison:{...latestSealComparison,unknownChanges:1}},org,branch,date)).toThrow();
 expect(()=>readSealedPreparation({...f,latestSealComparison:{...latestSealComparison,netSales:116}},org,branch,date)).toThrow();
 expect(()=>readSealedPreparation({...f,latestSealComparison:{...latestSealComparison,tenders:[{method:"cash",currency:"USD",amount:120}]}},org,branch,date)).toThrow();
});
