import {describe,it,expect} from "vitest";
import {validLibraryPage,type LibraryPhoto} from "./MediaLibrary";
const photo:LibraryPhoto={id:"11111111-1111-1111-1111-111111111111",originalFileName:"Photo.png",ownerService:"catalog",ownerType:"product",contentType:"image/png",sizeBytes:68,processingStatus:"ready",uploadedAtUtc:"2026-11-03T00:00:00Z",concurrencyVersion:1};
describe("photo library projections",()=>{
 it("accepts bounded ready photos and empty pages",()=>{expect(validLibraryPage({items:[photo],hasMore:false,page:1},1,true)).toBe(true);expect(validLibraryPage({items:[],hasMore:false,page:2},2,false)).toBe(true);});
 it.each(["owner","status","page","count","duplicate","date","size","identity"])("rejects unsafe or inconsistent %s data",kind=>{
  let rows=[{...photo}],page=1;
  if(kind==="owner")rows[0]!.ownerType="promptpay_qr";
  if(kind==="status")rows[0]!.processingStatus="quarantined";
  if(kind==="page")page=2;
  if(kind==="count")rows=Array.from({length:25},()=>({...photo}));
  if(kind==="duplicate")rows=[photo,photo];
  if(kind==="date")rows[0]!.uploadedAtUtc="invalid";
  if(kind==="size")rows[0]!.sizeBytes=-1;
  if(kind==="identity")rows[0]!.id="../../private";
  expect(validLibraryPage({items:rows,hasMore:false,page},1,true)).toBe(false);
 });
});
