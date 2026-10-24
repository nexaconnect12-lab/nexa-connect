import {describe,it,expect} from "vitest";
import {dayRequest,readDayDraft} from "./endOfDay";
const org="33333333-3333-3333-3333-333333333333",branch="11111111-1111-1111-1111-111111111111",restaurant="55555555-5555-5555-5555-555555555555";
function fixture(){
  const window={organizationId:org,restaurantId:restaurant,branchId:branch,fromUtc:"2026-08-31T17:00:00Z",toUtc:"2026-09-01T17:00:00Z"};
  const source={window,observedAtUtc:"2026-09-02T00:00:00Z"};
  return {businessDate:"2026-09-01",status:"draft",window,branch:{organizationId:org,restaurantId:restaurant,branchId:branch,timeZone:"Asia/Bangkok",currency:"THB"},grossSales:100,completedRefunds:25,netSales:75,cashVariance:2,tenders:[{method:"cash",currency:"THB",amount:100}],order:{...source,completedOrders:1,unresolvedOrders:0,evidenceGaps:0},payment:{...source,unresolvedPayments:0,unresolvedRefunds:1,evidenceGaps:0},pos:{...source,openShifts:1,openCashSessions:0,pendingCashReviews:1},issues:["open_shifts"]};
}
describe("end-of-day draft boundary",()=>{
  it("requires a UUID and an actual calendar date",()=>{
    expect(dayRequest(branch,"2026-09-01")).toEqual({branchId:branch,businessDate:"2026-09-01"});
    for(const date of ["","2026-02-30","invalid-date"])expect(dayRequest(branch,date)).toBeUndefined();
    expect(dayRequest("","2026-09-01")).toBeUndefined();
  });
  it("accepts matching source scopes and rejects mismatched totals, sources, currency or certification",()=>{
    expect(readDayDraft(fixture(),org,branch,"2026-09-01").netSales).toBe(75);
    for(const change of [()=>{const v=fixture();v.netSales=80;return v;},()=>{const v=fixture();v.tenders[0]!.currency="USD";return v;},()=>({...fixture(),status:"settled"}),()=>({...fixture(),businessDate:"2026-09-02"}),()=>{const v=fixture();v.payment.window={...v.window,branchId:restaurant};return v;}])
      expect(()=>readDayDraft(change(),org,branch,"2026-09-01")).toThrow();
  });
});
