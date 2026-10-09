import {z} from "zod";

const id=z.string().uuid(),version=z.number().int().positive().safe(),time=z.string().datetime({offset:true});
const identity=z.object({organizationId:id,restaurantId:id,branchId:id,businessDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/)});
export const settlementCommand=z.object({branchId:id,businessDate:z.string(),operationId:id,expectedPreparationVersion:version,
 preparationOperationId:id,approvalId:id,reviewedApprovalVersion:version});
const source=z.enum(["Order","Payment","POS"]);
const receipt=z.object({decisionId:id,eventId:id,correlationId:id,settledAtUtc:time,settlementId:id,identity,approvalId:id,sealVersion:version,
 snapshot:z.object({currency:z.literal("THB"),grossSales:z.number().finite(),completedRefunds:z.number().finite(),netSales:z.number().finite(),cashVariance:z.number().finite(),
  tenders:z.array(z.object({method:z.string().max(64),currency:z.literal("THB"),amount:z.number().finite()})).max(20)}),
 sources:z.array(z.object({source,sealId:id,epoch:id,revision:z.number().int().nonnegative().safe()})).length(3)});
const window=z.object({organizationId:id,restaurantId:id,branchId:id,fromUtc:time,toUtc:time});
const barrier=z.object({source,proof:z.object({command:z.object({settlementId:id,operationId:id,fence:z.object({operationId:id,window,approvalId:id,sealId:id,expiresAtUtc:time})}),
 phase:z.enum(["armed","committed","aborted"]),decisionId:id.nullable(),changedAtUtc:time,lateWorkCount:z.number().int().nonnegative().safe()})});
const state=z.object({id,identity,command:settlementCommand,status:z.enum(["arming","committing","finalized","aborting","aborted"]),decisionId:id.nullable(),receipt:receipt.nullable(),sources:z.array(barrier).max(3)});
const view=z.object({settlement:state.nullable(),canFinalize:z.boolean(),sourceProofCurrent:z.boolean()});
export type SettlementCommand=z.infer<typeof settlementCommand>;
export type SettlementView=z.infer<typeof view>;
export function readSettlement(input:unknown,organizationId:string,branchId:string,businessDate:string):SettlementView{
 const result=view.parse(input),s=result.settlement;
 if(!s)return result;
 const sameScope=(d:z.infer<typeof identity>)=>d.organizationId.toLowerCase()===organizationId.toLowerCase()&&d.branchId.toLowerCase()===branchId.toLowerCase()&&d.businessDate===businessDate;
 if(!sameScope(s.identity)||s.command.branchId!==s.identity.branchId||s.command.businessDate!==businessDate
  ||new Set(s.sources.map(x=>x.source)).size!==s.sources.length)throw new Error("Settlement scope mismatch");
 if((s.status==="arming")!==(s.decisionId===null)||(s.status==="committing"||s.status==="finalized")!==(s.receipt!==null))throw new Error("Settlement decision mismatch");
 for(const {proof} of s.sources){const c=proof.command,w=c.fence.window;
  if(c.settlementId!==s.id||c.operationId!==s.command.operationId||c.fence.operationId!==s.command.preparationOperationId||c.fence.approvalId!==s.command.approvalId
   ||w.organizationId!==s.identity.organizationId||w.restaurantId!==s.identity.restaurantId||w.branchId!==s.identity.branchId||Date.parse(w.toUtc)<=Date.parse(w.fromUtc)
   ||proof.phase==="armed"&&proof.decisionId!==null||proof.phase!=="armed"&&proof.decisionId!==s.decisionId)throw new Error("Source decision mismatch");
 }
 if(s.receipt){const r=s.receipt;
  if(!sameScope(r.identity)||r.identity.restaurantId!==s.identity.restaurantId||r.settlementId!==s.id||r.decisionId!==s.decisionId||r.approvalId!==s.command.approvalId
   ||new Set(r.sources.map(x=>x.source)).size!==3||r.sources.some(x=>s.sources.find(b=>b.source===x.source)?.proof.command.fence.sealId!==x.sealId))throw new Error("Receipt binding mismatch");
 }
 if(s.status==="finalized"&&(s.sources.length!==3||s.sources.some(x=>x.proof.phase!=="committed")))throw new Error("Finalization acknowledgements missing");
 if(s.status==="aborted"&&(s.sources.length!==3||s.sources.some(x=>x.proof.phase!=="aborted")))throw new Error("Abort acknowledgements missing");
 return result;
}
