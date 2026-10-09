import {z} from "zod";
import {Preparation,readSealedPreparation} from "./dayClosePreparation";
const id=z.string().uuid(),version=z.number().int().nonnegative().safe();
export const approvalCommand=z.object({branchId:id,businessDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/),operationId:id,expectedApprovalVersion:version,reviewedSealVersion:version.positive(),reasonCode:z.enum(["review_complete","review_after_changes"])});
const identity=z.object({organizationId:id,restaurantId:id,branchId:id,businessDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/)});
const decision=z.object({approvalId:id,operationId:id,identity,approvalVersion:version.positive(),sealVersion:version.positive(),approverSubject:z.string().min(1).max(128),reasonCode:z.enum(["review_complete","review_after_changes"]),approvedAtUtc:z.string().datetime({offset:true}),sourceValidatedAtUtc:z.string().datetime({offset:true}),validationCheckId:id,snapshot:z.unknown()});
const view=z.object({identity,version,status:z.enum(["not_approved","approved","unverified","superseded"]),decision:decision.nullable(),history:z.array(decision).max(20),historyTruncated:z.boolean(),validatedAtUtc:z.string().datetime({offset:true}).nullable(),canApprove:z.boolean(),sealVersion:version.positive().nullable(),sealSnapshot:z.unknown().nullable(),reason:z.string().max(100),operationDecision:decision.nullable().optional()});
export type Approval=Omit<z.infer<typeof view>,"sealSnapshot">&{sealSnapshot:Preparation["snapshot"]};
export type ApprovalCommand=z.infer<typeof approvalCommand>;
export function readApproval(value:unknown,organization:string,branch:string,date:string):Approval{
 const result=view.parse(value);
 const same=(i:z.infer<typeof identity>)=>i.organizationId.toLowerCase()===organization.toLowerCase()&&i.branchId.toLowerCase()===branch.toLowerCase()&&i.businessDate===date&&i.restaurantId.toLowerCase()===result.identity.restaurantId.toLowerCase();
 if(!same(result.identity))throw new Error("Approval scope mismatch");
 function validateSnapshot(snapshot:unknown,sealVersion:number,validatedAtUtc:string){
  return readSealedPreparation({identity:result.identity,version:sealVersion,status:"ready_for_review",snapshot,blockers:[],validatedAtUtc,canPrepare:false,pendingCommand:null,pendingSealChanges:0},organization,branch,date);
 }
 for(const d of [...result.history,...(result.decision?[result.decision]:[]),...(result.operationDecision?[result.operationDecision]:[])]){
  if(!same(d.identity)||d.approvalVersion>result.version||Date.parse(d.sourceValidatedAtUtc)>Date.parse(d.approvedAtUtc))throw new Error("Approval decision invalid");
  validateSnapshot(d.snapshot,d.sealVersion,d.sourceValidatedAtUtc);
 }
 if(result.status==="not_approved"&&result.decision || result.status!=="not_approved"&&!result.decision)throw new Error("Approval state invalid");
 let candidate:Preparation["snapshot"]=null;
 if(result.sealVersion!=null){if(!result.validatedAtUtc||!result.sealSnapshot)throw new Error("Approval candidate unverified");candidate=validateSnapshot(result.sealSnapshot,result.sealVersion,result.validatedAtUtc).snapshot;}
 else if(result.sealSnapshot!=null)throw new Error("Approval candidate version missing");
 if(result.status==="approved"&&(!result.validatedAtUtc||result.decision!.sealVersion!==result.sealVersion||JSON.stringify((result.decision!.snapshot as {seals:unknown}).seals)!==JSON.stringify((result.sealSnapshot as {seals:unknown}).seals)))throw new Error("Approval not freshly validated");
 return {...result,sealSnapshot:candidate};
}
