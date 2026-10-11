import {it,expect} from "vitest";
import {dayCorrectionSchema,validateDayCorrections} from "./dayCorrectionEvidence";
const id="11111111-1111-4111-8111-111111111111",other="22222222-2222-4222-8222-222222222222";
const pos={manifestId:id,revisionEpoch:id,sourceRevision:1},from="2026-09-01T00:00:00Z",to="2026-09-02T00:00:00Z";
const data=()=>({manifestId:id,revisionEpoch:id,sourceRevision:1,manifestHash:"a".repeat(64),adjustmentText:"-999999999999999.9999",expected:1,matched:1,missing:0,conflicting:0,unexpected:0,items:[{eventId:id,correctionId:id,originalSettlementId:id,workId:id,orderId:id,tenderId:id,drawerId:id,postingDate:"2026-09-01",postedAtUtc:"2026-09-01T01:00:00Z",adjustmentText:"-999999999999999.9999"}]});
it("validates exact original amount and source binding without browser rounding",()=>{const c=dayCorrectionSchema.parse(data());expect(()=>validateDayCorrections(c,from,to,pos,true)).not.toThrow();expect(c.adjustmentText).toBe("-999999999999999.9999");});
it("rejects equal-total identity mismatch, out-of-window evidence and unproven readiness",()=>{
 for(const change of [{manifestId:other},{revisionEpoch:other},{sourceRevision:2},{unexpected:1},{matched:0,missing:1},{adjustmentText:"-999999999999999.9998"}])expect(()=>validateDayCorrections(dayCorrectionSchema.parse({...data(),...change}),from,to,pos,true)).toThrow();
 const c=dayCorrectionSchema.parse(data());c.items[0]!.postedAtUtc=to;expect(()=>validateDayCorrections(c,from,to,pos,true)).toThrow();
});
it("retains blocked delivery evidence for display but never treats it as ready",()=>{const c=dayCorrectionSchema.parse({...data(),matched:0,missing:1});expect(()=>validateDayCorrections(c,from,to,pos,false)).not.toThrow();expect(()=>validateDayCorrections(c,from,to,pos,true)).toThrow();});
