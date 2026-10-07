import {z} from "zod";
const uuid=z.string().uuid(), money=z.number().finite(), count=z.number().int().nonnegative();
export const preparationCommand=z.object({branchId:uuid,businessDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/),operationId:uuid,expectedVersion:count,reasonCode:z.enum(["routine_close","recheck"]),reviewedCutoffVersion:count.safe().positive().nullable().optional()});
const cutoffReference=z.object({manifestId:uuid,generation:count.positive(),evidenceVersion:z.string().regex(/^[a-f0-9]{64}$/),revisionEpoch:uuid.nullable().optional(),sourceRevision:count.safe().nullable().optional()});
const cutoff=z.object({order:cutoffReference,payment:cutoffReference,pos:cutoffReference,checkId:uuid,sourcesCurrent:z.boolean(),financialGaps:count,deliveryComplete:z.boolean().optional(),evidenceProtocolVersion:z.number().int().optional()});
const sealReference=z.object({sealId:uuid,manifestId:uuid,revisionEpoch:uuid,sourceRevision:count.safe()});
const seals=z.object({order:sealReference,payment:sealReference,pos:sealReference,pendingChanges:count.safe(),journalComplete:z.boolean(),deliveryComplete:z.boolean()});
const snapshot=z.object({timeZone:z.string().min(1),currency:z.string().regex(/^[A-Z]{3}$/),fromUtc:z.string().datetime({offset:true}),toUtc:z.string().datetime({offset:true}),grossSales:money.nonnegative(),completedRefunds:money.nonnegative(),netSales:money,cashVariance:money,tenders:z.array(z.object({method:z.string().min(1),currency:z.string(),amount:money.nonnegative()})),orderVersion:z.string().nullable(),paymentVersion:z.string().nullable(),posVersion:z.string().nullable(),unresolvedOrders:count,unresolvedPayments:count,unresolvedRefunds:count,openShifts:count,openCashSessions:count,pendingCashReviews:count,issues:z.array(z.string()),observedAtUtc:z.string().datetime({offset:true}),cutoff:cutoff.nullable().optional(),seals:seals.nullable().optional()});
const view=z.object({identity:z.object({organizationId:uuid,restaurantId:uuid,branchId:uuid,businessDate:z.string()}),version:count,status:z.enum(["not_prepared","preparing","blocked","ready_for_review"]),snapshot:snapshot.nullable(),blockers:z.array(z.string()),validatedAtUtc:z.string().datetime({offset:true}).nullable(),canPrepare:z.boolean(),pendingCommand:preparationCommand.nullable(),pendingSealChanges:count.safe().nullable().optional()});
export type Preparation=z.infer<typeof view>;
export type PreparationCommand=z.infer<typeof preparationCommand>;
export function readPreparation(value:unknown,organization:string,branch:string,date:string):Preparation{
  const result=view.parse(value);
  if(result.identity.organizationId.toLowerCase()!==organization.toLowerCase()||result.identity.branchId.toLowerCase()!==branch.toLowerCase()||result.identity.businessDate!==date)throw new Error("Preparation scope mismatch");
  const s=result.snapshot;
  if(s&&(Math.abs(s.netSales-(s.grossSales-s.completedRefunds))>Number.EPSILON*Math.max(1,Math.abs(s.netSales),s.grossSales,s.completedRefunds)*4||Date.parse(s.toUtc)<=Date.parse(s.fromUtc)||s.tenders.some(t=>t.currency!==s.currency)))throw new Error("Preparation evidence invalid");
  if(result.status==="ready_for_review"&&(!s||!result.validatedAtUtc||result.blockers.length||[s.orderVersion,s.paymentVersion,s.posVersion].some(x=>!x||!/^[a-f0-9]{64}$/.test(x))))throw new Error("Readiness not validated");
  if(result.pendingCommand&&(result.pendingCommand.branchId.toLowerCase()!==branch.toLowerCase()||result.pendingCommand.businessDate!==date||result.status!=="preparing"||!result.canPrepare))throw new Error("Pending command mismatch");
  return result;
}

export function readCutoffPreparation(value:unknown,organization:string,branch:string,date:string):Preparation{
  const result=readPreparation(value,organization,branch,date);
  if(result.snapshot&&!result.snapshot.cutoff)throw new Error("Retained cutoff references missing");
  if(result.status==="ready_for_review"&&(!result.snapshot?.cutoff?.sourcesCurrent||result.snapshot.cutoff.financialGaps!==0))throw new Error("Cutoff not reconciled");
  const cut=result.snapshot?.cutoff;
  if(result.status==="ready_for_review"&&(!cut||cut.evidenceProtocolVersion!==2||!cut.deliveryComplete||[cut.order,cut.payment,cut.pos].some(r=>!r.revisionEpoch||r.revisionEpoch==="00000000-0000-0000-0000-000000000000"||r.sourceRevision==null)))throw new Error("Cutoff revision or delivery evidence missing");
  return result;
}

export function readSealedPreparation(value:unknown,organization:string,branch:string,date:string):Preparation{
  const result=readCutoffPreparation(value,organization,branch,date),s=result.snapshot?.seals,c=result.snapshot?.cutoff;
  if(s&&c&&(["order","payment","pos"] as const).some(owner=>s[owner].manifestId!==c[owner].manifestId||s[owner].revisionEpoch!==c[owner].revisionEpoch||s[owner].sourceRevision!==c[owner].sourceRevision))throw new Error("Sealed evidence identity mismatch");
  if(result.status==="ready_for_review"&&(!s||!s.journalComplete||!s.deliveryComplete||s.pendingChanges!==0||result.pendingSealChanges!==0))throw new Error("Sealed evidence not validated");
  if(result.pendingCommand&&!result.pendingCommand.reviewedCutoffVersion)throw new Error("Reviewed cutoff version missing");
  return result;
}