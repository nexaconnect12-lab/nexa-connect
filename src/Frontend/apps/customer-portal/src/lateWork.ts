import {z} from "zod";
const id=z.string().uuid(),time=z.string().datetime({offset:true}),version=z.number().int().nonnegative().safe();
const status=z.enum(["pending_review","investigating","correction_required","reviewed"]);
const scope=z.object({settlementId:id,window:z.object({organizationId:id,restaurantId:id,branchId:id,fromUtc:time,toUtc:time})});
const item=z.object({workId:id,eventType:z.string().max(128),receivedAtUtc:time,occurredAtUtc:time.nullable(),custodyReason:z.literal("late_delivery_for_settled_day"),records:z.record(z.string(),id),version,status});
const entry=z.object({version:version.refine(v=>v>0),status,decision:z.enum(["investigate","require_correction","acknowledge"]),reasonCode:z.enum(["investigate_delivery","correction_needed","evidence_checked"]),reviewedAtUtc:time});
const page=z.object({scope,items:z.array(item).max(50),nextCursor:z.string().max(200).nullable(),canReview:z.boolean()});
const detail=z.object({scope,item,settlementLinks:z.array(id).max(20),history:z.array(entry).max(20),historyTruncated:z.boolean(),canReview:z.boolean()});
const result=z.object({operationId:id,operationDecision:entry,detail});
export type LatePage=z.infer<typeof page>;
export type LateDetail=z.infer<typeof detail>;
export type LateScope=z.infer<typeof scope>;
export type ReviewCommand={workId:string;operationId:string;expectedVersion:number;decision:string;reasonCode:string};
function validate(s:LateScope,expected:LateScope){if(JSON.stringify(s)!==JSON.stringify(scope.parse(expected)))throw new Error("Review scope mismatch");}
export function readLatePage(raw:unknown,expected:LateScope){const p=page.parse(raw);validate(p.scope,expected);if(new Set(p.items.map(x=>x.workId)).size!==p.items.length)throw new Error("Repeated case");return p;}
export function readLateDetail(raw:unknown,expected:LateScope,workId:string){const d=detail.parse(raw);validate(d.scope,expected);if(d.item.workId!==workId||!d.settlementLinks.includes(expected.settlementId))throw new Error("Review case mismatch");for(const h of d.history){const pair=h.decision==="investigate"?["investigate_delivery","investigating"]:h.decision==="require_correction"?["correction_needed","correction_required"]:["evidence_checked","reviewed"];if(h.reasonCode!==pair[0]||h.status!==pair[1])throw new Error("Review decision mismatch");}return d;}
export function readLateResult(raw:unknown,expected:LateScope,command:ReviewCommand){const r=result.parse(raw);if(r.operationId!==command.operationId||r.operationDecision.decision!==command.decision||r.operationDecision.reasonCode!==command.reasonCode||r.operationDecision.version!==command.expectedVersion+1)throw new Error("Review operation mismatch");return readLateDetail(r.detail,expected,command.workId);}
