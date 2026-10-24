import {expect,it} from "vitest";
import {readSettlementLedger,settlementReportingRequest} from "./settlementReporting";
const id="11111111-1111-4111-8111-111111111111",other="22222222-2222-4222-8222-222222222222";
const request={branchId:id,fromDate:"2026-09-01",toDate:"2026-09-02"};
function report(){return{organizationId:id,...request,sourceObservedAtUtc:"2026-09-02T01:00:00Z",comparedAtUtc:"2026-09-02T02:00:00Z",manifestHash:"A".repeat(64),status:"matched",expected:1,matched:1,missing:0,conflicting:0,unexpected:0,
 items:[{eventId:id,settlementId:id,businessDate:request.fromDate,settledAtUtc:"2026-09-02T00:00:00Z",approvalId:id,sealVersion:1,currency:"THB",grossSales:"999999999999999.9999",completedRefunds:"0",netSales:"999999999999999.9999",cashVariance:"-10",
  contractVersion:2,correctionEvidence:"verified",status:"matched",sources:["Order","Payment","POS"].map(source=>({source,sealId:id,epoch:id,revision:1})),decisionId:id,fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",
  corrections:{manifestId:id,revisionEpoch:id,sourceRevision:1,manifestHash:"a".repeat(64),expected:1,matched:1,missing:0,conflicting:0,unexpected:0,adjustment:"-10",items:[{eventId:id,correctionId:id,originalSettlementId:id,workId:id,orderId:id,tenderId:id,drawerId:id,postingDate:"2026-09-01",postedAtUtc:"2026-09-01T01:00:00Z",adjustment:"-10"}]}}]};}
it("keeps large amounts exact and corrections separate",()=>{const v=readSettlementLedger(report(),id,request);expect(v.items[0]?.grossSales).toBe("999999999999999.9999");expect(v.items[0]?.corrections?.adjustment).toBe("-10");});
it("clearly preserves unavailable legacy proof",()=>{const v=report();Object.assign(v.items[0]!,{contractVersion:1,correctionEvidence:"historical_unavailable",corrections:null,decisionId:null,fromUtc:null,toUtc:null});expect(()=>readSettlementLedger(v,id,request)).not.toThrow();Object.assign(v.items[0]!,{correctionEvidence:"verified"});expect(()=>readSettlementLedger(v,id,request)).toThrow();});
it("rejects tenant mismatch forged counts rounded totals and incomplete proof",()=>{
 for(const change of [{organizationId:other},{matched:0},{status:"gaps"}])expect(()=>readSettlementLedger({...report(),...change},id,request)).toThrow();
 for(const change of [{netSales:"999999999999999.9998"},{correctionEvidence:"historical_unavailable"},{corrections:null}]){const v=report();Object.assign(v.items[0]!,change);expect(()=>readSettlementLedger(v,id,request)).toThrow();}
 const v=report();v.items[0]!.corrections.sourceRevision=2;expect(()=>readSettlementLedger(v,id,request)).toThrow();
});
it("keeps missing delivery visible and rejects invalid date ranges",()=>{const v=report();v.status="gaps";v.matched=0;v.missing=1;v.items[0]!.status="missing";expect(()=>readSettlementLedger(v,id,request)).not.toThrow();expect(settlementReportingRequest(id,"2026-02-30","2026-03-02")).toBeUndefined();expect(settlementReportingRequest(id,"2026-09-01","2026-10-03")).toBeUndefined();});
