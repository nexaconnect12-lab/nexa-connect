import {it,expect} from "vitest";
import {readCutoffPreparation} from "./dayClosePreparation";
const org="33333333-3333-3333-3333-333333333333",branch="11111111-1111-1111-1111-111111111111",date="2026-09-01";
const ref={manifestId:branch,generation:1,evidenceVersion:"a".repeat(64)};
const fixture=()=>({identity:{organizationId:org,restaurantId:org,branchId:branch,businessDate:date},version:2,status:"ready_for_review",snapshot:{timeZone:"UTC",currency:"THB",fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",grossSales:100,completedRefunds:0,netSales:100,cashVariance:0,tenders:[],orderVersion:"a".repeat(64),paymentVersion:"b".repeat(64),posVersion:"c".repeat(64),unresolvedOrders:0,unresolvedPayments:0,unresolvedRefunds:0,openShifts:0,openCashSessions:0,pendingCashReviews:0,issues:[],observedAtUtc:"2026-09-03T00:00:00Z",cutoff:{order:ref,payment:ref,pos:ref,checkId:org,sourcesCurrent:true,financialGaps:0}},blockers:[],validatedAtUtc:"2026-09-03T00:00:01Z",canPrepare:true,pendingCommand:null});
it("requires current source manifests and zero financial gaps before showing cutoff readiness",()=>{
 const f=fixture();expect(readCutoffPreparation(f,org,branch,date).snapshot?.cutoff?.order.generation).toBe(1);
 for(const cutoff of [null,{...f.snapshot.cutoff,sourcesCurrent:false},{...f.snapshot.cutoff,financialGaps:1},{...f.snapshot.cutoff,order:{...ref,generation:0}}])
  expect(()=>readCutoffPreparation({...f,snapshot:{...f.snapshot,cutoff}},org,branch,date)).toThrow();
});
it("retains superseded generations only as blocked evidence",()=>{
 const f=fixture();expect(readCutoffPreparation({...f,status:"blocked",blockers:["cutoff_superseded"],snapshot:{...f.snapshot,cutoff:{...f.snapshot.cutoff,sourcesCurrent:false}}},org,branch,date).status).toBe("blocked");
 expect(()=>readCutoffPreparation(f,branch,branch,date)).toThrow();
});
