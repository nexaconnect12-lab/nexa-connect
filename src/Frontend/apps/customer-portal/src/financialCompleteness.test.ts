import { describe, expect, it } from "vitest";
import { readCompleteness, reportWindow } from "./financialCompleteness";
const organizationId="33333333-3333-3333-3333-333333333333",branchId="11111111-1111-1111-1111-111111111111";
const window={branchId,fromUtc:"2026-09-01T00:00:00.000Z",toUtc:"2026-09-02T00:00:00.000Z"};
const counts={expected:1,matched:1,missing:0,conflicting:0,unexpected:0,gaps:0};
function complete(){return {status:"observed_complete",observation:{status:"observed_complete",checkId:"55555555-5555-5555-5555-555555555555",
  range:{organizationId,...window},checkedAtUtc:window.toUtc,orderObservedAtUtc:window.toUtc,refundObservedAtUtc:window.toUtc,
  saleEvidenceGaps:0,refundEvidenceGaps:0,unretainedSales:0,unretainedRefunds:0,sales:{...counts},payments:{...counts},refunds:{...counts}}};}
describe("financial completeness evidence",()=>{
  it("rejects a completion claim with source gaps, inconsistent counts or unknown status",()=>{
    const result=complete();expect(readCompleteness(result,organizationId,window).status).toBe("observed_complete");
    result.observation.saleEvidenceGaps=1;expect(()=>readCompleteness(result,organizationId,window)).toThrow();
    result.observation.saleEvidenceGaps=0;result.observation.sales.missing=1;expect(()=>readCompleteness(result,organizationId,window)).toThrow();
    expect(()=>readCompleteness({...result,status:"certified"},organizationId,window)).toThrow();
  });
  it("does not accept a different branch, organization or time window",()=>{
    expect(()=>readCompleteness(complete(),branchId,window)).toThrow();
    expect(()=>readCompleteness(complete(),organizationId,{...window,branchId:organizationId})).toThrow();
    expect(()=>readCompleteness(complete(),organizationId,{...window,toUtc:"2026-09-03T00:00:00Z"})).toThrow();
    expect(readCompleteness({status:"not_checked",observation:null},organizationId,window).status).toBe("not_checked");
    expect(()=>readCompleteness({status:"not_checked",observation:complete().observation},organizationId,window)).toThrow();
  });
  it("requires a nonempty branch and a closed positive window up to31days",()=>{
    const now=Date.parse("2026-10-01T00:00:00Z");
    expect(reportWindow(branchId,"2026-09-01T00:00","2026-09-02T00:00",now)).toEqual(window);
    expect(reportWindow(branchId,"2026-09-02T00:00","2026-09-01T00:00",now)).toBeUndefined();
    expect(reportWindow(branchId,"2026-08-01T00:00","2026-09-02T00:00",now)).toBeUndefined();
    expect(reportWindow(branchId,"2026-09-01T00:00","2026-10-02T00:00",now)).toBeUndefined();
    expect(reportWindow("00000000-0000-0000-0000-000000000000","2026-09-01T00:00","2026-09-02T00:00",now)).toBeUndefined();
  });
});
