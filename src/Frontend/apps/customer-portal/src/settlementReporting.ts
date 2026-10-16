import {z} from "zod";
const id=z.string().uuid().refine(v=>v!=="00000000-0000-0000-0000-000000000000"),time=z.string().datetime({offset:true});
const date=z.string().regex(/^\d{4}-\d{2}-\d{2}$/).refine(v=>Number.isFinite(Date.parse(v+"T00:00:00Z"))&&new Date(v+"T00:00:00Z").toISOString().slice(0,10)===v);
const amount=z.string().regex(/^-?(?:0|[1-9]\d{0,17})(?:\.\d{1,4})?$/),count=z.number().int().nonnegative().safe();
const correction=z.object({manifestId:id,revisionEpoch:id,sourceRevision:count,manifestHash:z.string().regex(/^[A-Fa-f0-9]{64}$/),expected:count.max(1000),matched:count.max(1000),missing:z.literal(0),conflicting:z.literal(0),unexpected:z.literal(0),adjustment:amount,
 items:z.array(z.object({eventId:id,correctionId:id,originalSettlementId:id,workId:id,orderId:id,tenderId:id,drawerId:id,postingDate:date,postedAtUtc:time,adjustment:amount})).max(1000)});
const row=z.object({eventId:id,settlementId:id,businessDate:date,settledAtUtc:time,approvalId:id,sealVersion:count.positive(),currency:z.literal("THB"),grossSales:amount,completedRefunds:amount,netSales:amount,cashVariance:amount,
 contractVersion:z.union([z.literal(1),z.literal(2)]),correctionEvidence:z.enum(["verified","historical_unavailable"]),corrections:correction.nullable(),status:z.enum(["matched","missing","conflicting","unexpected"]),
 sources:z.array(z.object({source:z.enum(["Order","Payment","POS"]),sealId:id,epoch:id,revision:count})).length(3),decisionId:id.nullable(),fromUtc:time.nullable(),toUtc:time.nullable()});
const schema=z.object({organizationId:id,branchId:id,fromDate:date,toDate:date,sourceObservedAtUtc:time,comparedAtUtc:time,manifestHash:z.string().regex(/^[A-F0-9]{64}$/),status:z.enum(["matched","gaps"]),expected:count.max(31),matched:count.max(31),missing:count.max(31),conflicting:count.max(31),unexpected:count.max(62),items:z.array(row).max(62)});
export type SettlementLedger=z.infer<typeof schema>;
function units(v:string){const [whole="0",fraction=""]=v.replace(/^-/ ,"").split(".");return(v.startsWith("-")?-1n:1n)*(BigInt(whole)*10000n+BigInt(fraction.padEnd(4,"0")));}
export function settlementReportingRequest(branchId:string,fromDate:string,toDate:string){
 if(!id.safeParse(branchId).success||!date.safeParse(fromDate).success||!date.safeParse(toDate).success)return undefined;
 const length=Date.parse(toDate)-Date.parse(fromDate);
 if(length<=0||length>31*86400000||toDate>new Date(Date.now()+86400000).toISOString().slice(0,10))return undefined;
 return{branchId,fromDate,toDate};
}
export function readSettlementLedger(raw:unknown,organizationId:string,request:{branchId:string;fromDate:string;toDate:string}){
 const v=schema.parse(raw),counts={matched:0,missing:0,conflicting:0,unexpected:0};let corrections=0;
 for(const r of v.items){
  counts[r.status]++;
  if(r.businessDate<v.fromDate||r.businessDate>=v.toDate||units(r.grossSales)<0n||units(r.completedRefunds)<0n||units(r.netSales)!==units(r.grossSales)-units(r.completedRefunds)
   ||new Set(r.sources.map(s=>s.source)).size!==3||Date.parse(r.settledAtUtc)>Date.parse(v.sourceObservedAtUtc))throw Error("Invalid settlement evidence");
  if(r.contractVersion===1){if(r.correctionEvidence!=="historical_unavailable"||r.corrections!==null||r.decisionId!==null||r.fromUtc!==null||r.toUtc!==null)throw Error("Legacy proof was enriched");}
  else{
   const c=r.corrections,pos=r.sources.find(s=>s.source==="POS");
   if(!c||!pos||r.correctionEvidence!=="verified"||!r.decisionId||!r.fromUtc||!r.toUtc||Date.parse(r.fromUtc)>=Date.parse(r.toUtc)||Date.parse(r.toUtc)-Date.parse(r.fromUtc)>27*3600000
    ||Date.parse(r.settledAtUtc)<Date.parse(r.toUtc)||c.revisionEpoch!==pos.epoch||c.sourceRevision!==pos.revision||c.expected!==c.matched||c.expected!==c.items.length
    ||units(c.adjustment)>0n||c.items.reduce((sum,i)=>sum+units(i.adjustment),0n)!==units(c.adjustment)
    ||c.items.some(i=>units(i.adjustment)>=0n||Date.parse(i.postedAtUtc)<Date.parse(r.fromUtc!)||Date.parse(i.postedAtUtc)>=Date.parse(r.toUtc!))
    ||["eventId","correctionId","workId","orderId","tenderId"].some(key=>new Set(c.items.map(i=>i[key as keyof typeof i])).size!==c.items.length))throw Error("Invalid settled correction proof");
   corrections+=c.expected;
  }
 }
 if(v.organizationId!==organizationId||v.branchId!==request.branchId||v.fromDate!==request.fromDate||v.toDate!==request.toDate
  ||v.expected!==v.matched+v.missing+v.conflicting||Object.entries(counts).some(([key,n])=>v[key as keyof typeof counts]!==n)
  ||new Set(v.items.map(i=>i.eventId)).size!==v.items.length||corrections>2000
  ||v.status!==(v.missing+v.conflicting+v.unexpected===0?"matched":"gaps")||Date.parse(v.comparedAtUtc)<Date.parse(v.sourceObservedAtUtc))throw Error("Settlement report scope or counts mismatch");
 return v;
}
