import {z} from "zod";
const id=z.string().uuid(),version=z.number().int().nonnegative().safe(),time=z.string().datetime({offset:true});
export const finalizationCommand=z.object({branchId:id,businessDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/),operationId:id,expectedVersion:version,approvalId:id,reviewedApprovalVersion:version.positive()});
const identity=z.object({organizationId:id,restaurantId:id,branchId:id,businessDate:z.string()});
const fence=z.object({source:z.enum(["Order","Payment","POS"]),sealId:id,epoch:id,revision:version,expiresAtUtc:time,active:z.boolean(),cancelled:z.boolean()});
const view=z.object({identity,version,status:z.enum(["not_prepared","preparing","prepared","blocked","cancelling","cancelled","expired"]),pendingCommand:finalizationCommand.nullable(),approvalId:id.nullable(),reviewedApprovalVersion:version.positive().nullable(),sealVersion:version.positive().nullable(),expiresAtUtc:time.nullable(),validatedAtUtc:time.nullable(),sources:z.array(fence).max(3),blockers:z.array(z.string().max(100)).max(20),canPrepare:z.boolean()});
export type Finalization=z.infer<typeof view>;
export type FinalizationCommand=z.infer<typeof finalizationCommand>;
export function readFinalization(value:unknown,organization:string,branch:string,date:string,now=Date.now()):Finalization{
 const result=view.parse(value),command=result.pendingCommand;
 if(result.identity.organizationId.toLowerCase()!==organization.toLowerCase()||result.identity.branchId.toLowerCase()!==branch.toLowerCase()||result.identity.businessDate!==date
  ||command&&(command.branchId.toLowerCase()!==branch.toLowerCase()||command.businessDate!==date||command.approvalId!==result.approvalId||command.reviewedApprovalVersion!==result.reviewedApprovalVersion))throw new Error("Finalization scope mismatch");
 if(result.status!=="not_prepared"&&(!command||!result.expiresAtUtc||!result.approvalId||!result.sealVersion))throw new Error("Preparation binding missing");
 if(new Set(result.sources.map(x=>x.source)).size!==result.sources.length||result.sources.some(x=>x.active&&x.cancelled||x.expiresAtUtc!==result.expiresAtUtc))throw new Error("Fence identity mismatch");
 if(result.status==="prepared"&&(!result.validatedAtUtc||Date.parse(result.validatedAtUtc)>now||now-Date.parse(result.validatedAtUtc)>60_000||!result.expiresAtUtc||Date.parse(result.expiresAtUtc)<=now+15_000||result.blockers.length||result.sources.length!==3||result.sources.some(x=>!x.active||x.cancelled)))throw new Error("Preparation proof unavailable");
 if(result.status!=="prepared"&&result.validatedAtUtc)throw new Error("Preparation not verified");
 if(result.status==="cancelled"&&(result.sources.length!==3||result.sources.some(x=>!x.cancelled||x.active)))throw new Error("Cancellation incomplete");
 return result;
}
