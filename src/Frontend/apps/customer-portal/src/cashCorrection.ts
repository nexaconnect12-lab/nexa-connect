import {z} from "zod";
import {LateScope} from "./lateWork";
const id=z.string().uuid(),time=z.string().datetime({offset:true}),version=z.number().int().positive().safe(),date=z.string().regex(/^\d{4}-\d{2}-\d{2}$/),fingerprint=z.string().regex(/^[0-9a-f]{64}$/);
const scope=z.object({settlementId:id,window:z.object({organizationId:id,restaurantId:id,branchId:id,fromUtc:time,toUtc:time})});
const money=z.string().regex(/^-(?:0|[1-9]\d{0,14})(?:\.\d{1,4})?$/).refine(v=>Number(v)<0);
const preview=z.object({scope,workId:id,reviewVersion:version,orderId:id,tenderId:id,drawerId:id,currency:z.literal("THB"),adjustment:money,postingDate:date,timeZone:z.string().min(1).max(128),postingFromUtc:time,postingToUtc:time,fingerprint});
const receipt=z.object({correctionId:id,operationId:id,scope,workId:id,reviewVersion:version,orderId:id,tenderId:id,drawerId:id,currency:z.literal("THB"),adjustment:money,postingDate:date,postedAtUtc:time,eventId:id});
const view=z.object({scope,workId:id,preview:preview.nullable(),receipt:receipt.nullable(),canPost:z.boolean(),blocker:z.enum(["unsupported_cash_evidence","correction_review_required"]).nullable()});
export type CorrectionView=z.infer<typeof view>;
export type CorrectionCommand={workId:string;operationId:string;expectedReviewVersion:number;previewFingerprint:string};
export function readCorrection(raw:unknown,expected:LateScope,workId:string,command?:CorrectionCommand){
 const v=view.parse(raw),same=(s:LateScope)=>JSON.stringify(scope.parse(s))===JSON.stringify(scope.parse(expected));
 if(!same(v.scope)||v.workId!==workId||!!v.preview&&!!v.receipt||v.blocker!==null&&(v.preview!==null||v.receipt!==null))throw new Error("Correction scope mismatch");
 if(v.preview){const p=v.preview;if(!same(p.scope)||p.workId!==workId||Date.parse(p.postingFromUtc)<Date.parse(expected.window.toUtc)||Date.parse(p.postingToUtc)<=Date.parse(p.postingFromUtc)||Date.parse(p.postingToUtc)-Date.parse(p.postingFromUtc)>27*60*60*1000)throw new Error("Correction preview mismatch");}
 if(v.receipt){const r=v.receipt,w=r.scope.window,e=expected.window;if(r.workId!==workId||w.organizationId!==e.organizationId||w.restaurantId!==e.restaurantId||w.branchId!==e.branchId)throw new Error("Correction receipt mismatch");}
 if(command&&(!v.receipt||v.receipt.operationId!==command.operationId||v.receipt.workId!==command.workId||v.receipt.reviewVersion!==command.expectedReviewVersion))throw new Error("Correction operation mismatch");return v;
}
