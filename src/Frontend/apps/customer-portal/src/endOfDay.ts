import { z } from "zod";

const money=z.number().finite();
const count=z.number().int().nonnegative();
const window=z.object({organizationId:z.string().uuid(),restaurantId:z.string().uuid(),branchId:z.string().uuid(),fromUtc:z.string().datetime({offset:true}),toUtc:z.string().datetime({offset:true})});
const source={window,observedAtUtc:z.string().datetime({offset:true})};
const schema=z.object({
  businessDate:z.string(),status:z.literal("draft"),window,
  branch:z.object({organizationId:z.string().uuid(),restaurantId:z.string().uuid(),branchId:z.string().uuid(),timeZone:z.string().min(1),currency:z.string().regex(/^[A-Z]{3}$/)}),
  grossSales:money,completedRefunds:money,netSales:money,cashVariance:money,
  tenders:z.array(z.object({method:z.string().min(1),currency:z.string(),amount:money.nonnegative()})),
  order:z.object({...source,completedOrders:count,unresolvedOrders:count,evidenceGaps:count}),
  payment:z.object({...source,unresolvedPayments:count,unresolvedRefunds:count,evidenceGaps:count}),
  pos:z.object({...source,openShifts:count,openCashSessions:count,pendingCashReviews:count,lateCashCorrectionAdjustment:money.nonpositive().default(0),lateCashCorrections:count.default(0)}),
  issues:z.array(z.string()),
});
export type DayDraft=z.infer<typeof schema>;
export function dayRequest(branchId:string,businessDate:string){
  const date=new Date(`${businessDate}T00:00:00Z`);
  if(!z.string().uuid().safeParse(branchId).success||!/^\d{4}-\d{2}-\d{2}$/.test(businessDate)
    ||!Number.isFinite(date.getTime())||date.toISOString().slice(0,10)!==businessDate)return undefined;
  return {branchId,businessDate};
}
export function readDayDraft(value:unknown,organizationId:string,branchId:string,businessDate:string):DayDraft{
  const report=schema.parse(value);
  const scope=report.window;
  if(report.businessDate!==businessDate||scope.organizationId!==organizationId||scope.branchId!==branchId
    ||report.branch.organizationId!==organizationId||report.branch.branchId!==branchId||report.branch.restaurantId!==scope.restaurantId
    ||Date.parse(scope.fromUtc)>=Date.parse(scope.toUtc)||Date.parse(scope.toUtc)>Date.now()
    ||[report.order,report.payment,report.pos].some(s=>JSON.stringify(s.window)!==JSON.stringify(scope)||Date.parse(s.observedAtUtc)<Date.parse(scope.toUtc))
    ||report.tenders.some(t=>t.currency!==report.branch.currency)
    ||Math.abs(report.netSales-report.grossSales+report.completedRefunds)>0.00005)throw new Error("Invalid day draft");
  return report;
}
