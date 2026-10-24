import {describe,it,expect} from "vitest";
import {correctionReportingRequest,readCorrectionReport} from "./cashCorrectionReporting";
const id="11111111-1111-4111-8111-111111111111",other="22222222-2222-4222-8222-222222222222";
const request={branchId:id,fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z"};
function report(){return {organizationId:id,...request,sourceObservedAtUtc:"2026-09-03T00:00:00Z",comparedAtUtc:"2026-09-03T00:00:00Z",manifestHash:"A".repeat(64),status:"matched",expected:1,matched:1,missing:0,conflicting:0,unexpected:0,sourceAdjustment:"-999999999999999.9999",projectedAdjustment:"-999999999999999.9999",items:[{eventId:id,correctionId:id,workId:id,originalSettlementId:id,orderId:id,tenderId:id,drawerId:id,postingDate:"2026-09-01",postedAtUtc:"2026-09-01T01:00:00Z",currency:"THB",adjustment:"-999999999999999.9999",status:"matched"}]};}
describe("correction financial comparison",()=>{
 it("preserves exact decimal review and closed windows",()=>{expect(readCorrectionReport(report(),id,request).sourceAdjustment).toBe("-999999999999999.9999");expect(correctionReportingRequest(id,request.fromUtc,request.toUtc)).toBeDefined();expect(correctionReportingRequest(id,request.toUtc,request.fromUtc)).toBeUndefined();});
 it("rejects tenant/time/count mismatches and numeric rounded money",()=>{for(const change of [{organizationId:other},{branchId:other},{matched:0},{toUtc:"2026-09-03T00:00:00Z"},{sourceAdjustment:-999999999999999.9999},{sourceAdjustment:"-999999999999999.9998"},{status:"gaps",projectedAdjustment:"10"}])expect(()=>readCorrectionReport({...report(),...change},id,request)).toThrow();});
 it("shows missing delivery without treating totals as proof",()=>{const r=report();r.status="gaps";r.missing=1;r.matched=0;r.projectedAdjustment="0";r.items[0]!.status="missing";expect(readCorrectionReport(r,id,request).status).toBe("gaps");r.status="matched";expect(()=>readCorrectionReport(r,id,request)).toThrow();});
});
