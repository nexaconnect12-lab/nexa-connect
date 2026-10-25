import {z} from "zod";
const id=z.guid(),count=z.number().int().nonnegative().safe().max(2000),time=z.string().datetime({offset:true});
const amount=z.string().regex(/^-?(?:0|[1-9]\d{0,17})(?:\.\d{1,4})?$/);
export const dayCorrectionSchema=z.object({manifestId:id,revisionEpoch:id,sourceRevision:z.number().int().nonnegative().safe(),manifestHash:z.string().regex(/^[a-fA-F0-9]{64}$/),adjustmentText:amount,
 expected:count.max(1000),matched:count,missing:count,conflicting:count,unexpected:count,
 items:z.array(z.object({eventId:id,correctionId:id,originalSettlementId:id,workId:id,orderId:id,tenderId:id,drawerId:id,postingDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/),postedAtUtc:time,adjustmentText:amount})).max(1000)});
export type DayCorrections=z.infer<typeof dayCorrectionSchema>;
function units(v:string){const negative=v.startsWith("-"),[whole="0",fraction=""]=v.replace(/^-/,"").split(".");return (negative?-1n:1n)*(BigInt(whole)*10000n+BigInt(fraction.padEnd(4,"0")));}
export function validateDayCorrections(c:DayCorrections,from:string,to:string,pos:{manifestId:string;revisionEpoch?:string|null;sourceRevision?:number|null},ready:boolean){
 if(c.manifestId!==pos.manifestId||c.revisionEpoch!==pos.revisionEpoch||c.sourceRevision!==pos.sourceRevision||c.expected!==c.items.length
  ||c.expected!==c.matched+c.missing+c.conflicting||units(c.adjustmentText)>0n||c.items.reduce((s,i)=>s+units(i.adjustmentText),0n)!==units(c.adjustmentText)
  ||c.items.some(i=>units(i.adjustmentText)>=0n||Date.parse(i.postedAtUtc)<Date.parse(from)||Date.parse(i.postedAtUtc)>=Date.parse(to))
  ||new Set(c.items.map(i=>i.eventId)).size!==c.items.length||new Set(c.items.map(i=>i.correctionId)).size!==c.items.length
  ||ready&&(c.missing+c.conflicting+c.unexpected>0||c.matched!==c.expected))throw new Error("Correction evidence is inconsistent or undelivered");
}
