import React from "react";
import {act} from "react-dom/test-utils";
import {createRoot,Root} from "react-dom/client";
import {afterEach,expect,it,vi} from "vitest";
const {request}=vi.hoisted(()=>({request:vi.fn()}));
vi.mock("@nexaconnect/api-client",()=>({createApiClient:()=>({request})}));
vi.mock("./OperationalBranchPicker",()=>({OperationalBranchPicker:({onChange,disabled}:{onChange:(s:string)=>void;disabled:boolean})=><select aria-label="Branch" disabled={disabled} onChange={e=>onChange(e.target.value)}><option value=""/><option value="branch">Branch</option></select>}));
vi.mock("antd",()=>({Alert:({message}:{message:string})=><div role="status">{message}</div>,Button:({onClick,disabled,children}:React.ButtonHTMLAttributes<HTMLButtonElement>)=><button disabled={disabled} onClick={onClick}>{children}</button>,Card:({children}:{children:React.ReactNode})=><div>{children}</div>,Space:({children}:{children:React.ReactNode})=><div>{children}</div>,Typography:{Paragraph:({children}:{children:React.ReactNode})=><p>{children}</p>},Input:(props:React.InputHTMLAttributes<HTMLInputElement>)=><input {...props}/>,Select:({options,value,onChange,...p}:{options:{value:string;label:string}[];value?:string;onChange:(s:string)=>void})=><select {...p} value={value??""} onChange={e=>onChange(e.target.value)}><option value=""/>{options.map(o=><option key={o.value} value={o.value}>{o.label}</option>)}</select>}));
import {ProductBarcodePanel} from "./ProductBarcodePanel";
let root:Root|undefined,host:HTMLDivElement;
afterEach(()=>{if(root)act(()=>root!.unmount());host?.remove();request.mockReset();});
async function action(fn:()=>void){await act(async()=>{fn();await Promise.resolve();});}
function button(text:string){return Array.from(host.querySelectorAll("button")).find(b=>b.textContent===text)!;}
async function setup(){
  request.mockImplementation(async(path:string,options?:{method?:string})=>options?.method==="PUT"?Promise.reject(new Error("conflict")):path.endsWith("csrf")?{requestToken:"csrf"}:path.endsWith("menu-items")?[{productId:"product",name:"Meal"}]:{organizationId:"org",branchId:"branch",productId:"product",barcode:"001",version:2});
  host=document.createElement("div");document.body.append(host);root=createRoot(host);await action(()=>root!.render(<ProductBarcodePanel/>));
  await action(()=>{const picker=host.querySelector<HTMLSelectElement>('[aria-label="Branch"]')!;picker.value="branch";picker.dispatchEvent(new Event("change",{bubbles:true}));});
  await action(()=>button("Load products").click());
  await action(()=>{const picker=host.querySelector<HTMLSelectElement>('[aria-label="Barcode product"]')!;picker.value="product";picker.dispatchEvent(new Event("change",{bubbles:true}));});
  await action(()=>button("Load barcode").click());
}
it("requires explicit save and clears editable proof after an uncertain/conflicting save without retry",async()=>{
  await setup();expect(request.mock.calls.filter(([,o])=>o?.method==="PUT")).toHaveLength(0);
  await action(()=>button("Save barcode").click());
  expect(request.mock.calls.filter(([,o])=>o?.method==="PUT")).toHaveLength(1);
  const [,options]=request.mock.calls.find(([,o])=>o?.method==="PUT")!;
  expect(options.headers).toEqual({"X-Nexa-CSRF":"csrf"});expect(options.body).toEqual({barcode:"001",expectedVersion:2});
  expect(host.textContent).toContain("reload before saving again");expect(host.querySelector('[aria-label="Product barcode"]')).toBeNull();
});
it("changing the product discards prior barcode/version so another product cannot receive stale edits",async()=>{
  await setup();expect(host.querySelector<HTMLInputElement>('[aria-label="Product barcode"]')!.value).toBe("001");
  await action(()=>{const picker=host.querySelector<HTMLSelectElement>('[aria-label="Barcode product"]')!;picker.value="";picker.dispatchEvent(new Event("change",{bubbles:true}));});
  expect(host.querySelector('[aria-label="Product barcode"]')).toBeNull();expect(request.mock.calls.filter(([,o])=>o?.method==="PUT")).toHaveLength(0);
});
