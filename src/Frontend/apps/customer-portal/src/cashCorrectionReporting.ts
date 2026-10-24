import {z} from "zod";
const id=z.string().uuid(),time=z.string().datetime({offset:true}),count=z.number().int().nonnegative().max(2000);
const money=z.string().regex(/^-?(?:0|[1-9]\d{0,17})(?:\.\d{1,4})?$/);
const item=z.object({eventId:id,correctionId:id,originalSettlementId:id,workId:id,orderId:id,tenderId:id,drawerId:id,
  postingDate:z.string().regex(/^\d{4}-\d{2}-\d{2}$/),postedAtUtc:time,currency:z.literal("THB"),adjustment:money,
  status:z.enum(["matched","missing","conflicting","unexpected"])});
const schema=z.object({organizationId:id,branchId:id,fromUtc:time,toUtc:time,sourceObservedAtUtc:time,comparedAtUtc:time,
  manifestHash:z.string().regex(/^[A-F0-9]{64}$/),status:z.enum(["matched","gaps"]),expected:count.max(1000),matched:count,missing:count,
  conflicting:count,unexpected:count,sourceAdjustment:money,projectedAdjustment:money,items:z.array(item).max(2000)});
export type CorrectionReport=z.infer<typeof schema>;
function units(value:string){const negative=value.startsWith("-");const [whole="0",fraction=""]=value.replace(/^-/,"").split(".");return (negative?-1n:1n)*(BigInt(whole)*10000n+BigInt(fraction.padEnd(4,"0")));}
export function correctionReportingRequest(branchId:string,from:string,to:string){
  if(!id.safeParse(branchId).success||!time.safeParse(from).success||!time.safeParse(to).success)return undefined;
  const start=Date.parse(from),end=Date.parse(to);
  if(start>=end||end> Date.now()||end-start>31*86400000)return undefined;
  return {branchId,fromUtc:new Date(start).toISOString(),toUtc:new Date(end).toISOString()};
}
export function readCorrectionReport(raw:unknown,organizationId:string,request:{branchId:string;fromUtc:string;toUtc:string}){
  const v=schema.parse(raw),counts={matched:0,missing:0,conflicting:0,unexpected:0};
  for(const i of v.items){counts[i.status]++;if(units(i.adjustment)>=0n||Date.parse(i.postedAtUtc)<Date.parse(v.fromUtc)||Date.parse(i.postedAtUtc)>=Date.parse(v.toUtc))throw new Error("Invalid correction evidence");}
  if(v.organizationId!==organizationId||v.branchId!==request.branchId||Date.parse(v.fromUtc)!==Date.parse(request.fromUtc)||Date.parse(v.toUtc)!==Date.parse(request.toUtc)
    ||Date.parse(v.sourceObservedAtUtc)<Date.parse(v.toUtc)||Date.parse(v.comparedAtUtc)<Date.parse(v.toUtc)
    ||units(v.sourceAdjustment)>0n||units(v.projectedAdjustment)>0n
    ||v.expected!==v.matched+v.missing+v.conflicting||Object.entries(counts).some(([k,n])=>v[k as keyof typeof counts]!==n)
    ||new Set(v.items.map(x=>x.eventId)).size!==v.items.length||v.status!==(v.missing+v.conflicting+v.unexpected===0?"matched":"gaps")
    ||v.items.filter(i=>i.status!=="unexpected").reduce((s,i)=>s+units(i.adjustment),0n)!==units(v.sourceAdjustment)
    ||v.status==="matched"&&units(v.projectedAdjustment)!==units(v.sourceAdjustment))throw new Error("Correction reporting scope or comparison mismatch");
  return v;
}
