import {MenuPhoto} from "./MenuPhoto";
import {MenuOrganization,checkedOrganization,orderedMenu,categoryFor} from "./menuOrganization";
import {useEffect,useRef,useState} from "react";
import {Alert,Button,Tag,Typography} from "antd";
import "./qrMenuCart.css";

type Item={productId:string;name:string;unitPrice:number;currency:string;available:boolean};
type Line={productId:string;quantity:number};
export type Quote={quoteId?:string;fingerprint:string;currency:string;expiresAtUtc:string;lines:(Line&{name:string;unitPrice:number;lineTotal:number})[];
  menuAmount:number;subtotalAmount:number;serviceChargeAmount:number;taxAmount:number;totalAmount:number;
  taxPercent:number;taxInclusive:boolean;serviceChargePercent:number};
async function read<T>(path:string,body?:unknown):Promise<T>{
  const response=await fetch(`/bff/guest/qr/${path}`,{method:body?"POST":"GET",credentials:"same-origin",cache:"no-store",redirect:"error",
    ...(body?{headers:{"Content-Type":"application/json"},body:JSON.stringify(body)}:{})});
  if(!response.ok)throw new Error(String(response.status));return response.json() as Promise<T>;
}
export function QrMenuCart({currency,tableCode,onExpired,submit}:{currency:string;tableCode:string;onExpired:()=>void;submit?:(quote:Quote)=>void}){
  const [items,setItems]=useState<Item[]>([]),[cart,setCart]=useState<Line[]>([]),[quote,setQuote]=useState<Quote>(),
    [busy,setBusy]=useState(false),[loaded,setLoaded]=useState(false),[notice,setNotice]=useState<string>(),[changed,setChanged]=useState(false);
  const revision=useRef(0),previous=useRef<string>(),alive=useRef(true),callback=useRef(onExpired);callback.current=onExpired;
  const cartReview=useRef<HTMLElement>(null);
  const [organization,setOrganization]=useState<MenuOrganization>({categories:[],items:[]}),[category,setCategory]=useState("*"),[search,setSearch]=useState("");
  const money=(value:number)=>new Intl.NumberFormat(undefined,{style:"currency",currency}).format(value);
  const fail=(e:unknown)=>{
    if(e instanceof Error&&e.message==="401"){callback.current();return;}
    setNotice(e instanceof Error&&e.message==="409"?"An item is no longer available. Refresh the menu and adjust your cart.":
      e instanceof Error&&e.message==="429"?"Too many requests. Wait one minute, then try again.":"Menu or totals unavailable. Check your connection and try again. Your cart has not been submitted.");
  };
  const load=async()=>{
    const version=++revision.current;setQuote(undefined);setBusy(true);setNotice(undefined);
    try{const [menu,layout]=await Promise.all([read<{items:Item[]}>("menu"),read<unknown>("menu-organization").then(checkedOrganization).catch(()=>null)]);
      if(alive.current&&version===revision.current){setItems(menu.items);setOrganization(layout??{categories:[],items:[]});setCategory(current=>current==="*"||current===""||layout?.categories.some(c=>c.id===current)?current:"*");setLoaded(true);if(!layout)setNotice("Categories unavailable; showing menu items without category organization.");}}
    catch(e){if(alive.current&&version===revision.current)fail(e);}
    finally{if(alive.current&&version===revision.current)setBusy(false);}
  };
  useEffect(()=>{alive.current=true;void load();return()=>{alive.current=false;revision.current++;};},[]);
  useEffect(()=>{
    const offline=()=>{revision.current++;setBusy(false);setQuote(undefined);setNotice("You are offline. Reconnect, refresh the menu and review totals again.");};
    const resume=()=>{if(document.visibilityState==="visible"){revision.current++;setBusy(false);setQuote(undefined);setNotice("Review totals again after returning to this page.");}};
    window.addEventListener("offline",offline);document.addEventListener("visibilitychange",resume);
    return()=>{window.removeEventListener("offline",offline);document.removeEventListener("visibilitychange",resume);};
  },[]);
  useEffect(()=>{if(!quote)return;const timer=window.setTimeout(()=>{setQuote(undefined);setNotice("This quote has expired. Review totals again.");},Math.max(0,Date.parse(quote.expiresAtUtc)-Date.now()));return()=>window.clearTimeout(timer);},[quote]);
  const update=(id:string,quantity:number)=>{
    if(!Number.isInteger(quantity)||quantity<0||quantity>99)return;
    revision.current++;previous.current=undefined;setBusy(false);setQuote(undefined);setChanged(false);
    setCart(lines=>quantity===0?lines.filter(l=>l.productId!==id):lines.some(l=>l.productId===id)?lines.map(l=>l.productId===id?{...l,quantity}:l):
      lines.length<50?[...lines,{productId:id,quantity}]:lines);
  };
  const review=async()=>{
    const version=++revision.current;setBusy(true);setQuote(undefined);setNotice(undefined);setChanged(false);
    try{
      const value=await read<Quote>("quote",{lines:cart});
      if(!alive.current||version!==revision.current)return;
      if(value.currency!==currency||Date.parse(value.expiresAtUtc)<=Date.now())throw new Error("invalid_quote");
      setChanged((previous.current!==undefined&&previous.current!==value.fingerprint)||value.lines.some(l=>items.find(i=>i.productId===l.productId)?.unitPrice!==l.unitPrice));
      previous.current=value.fingerprint;setQuote(value);
    }catch(e){if(alive.current&&version===revision.current)fail(e);}
    finally{if(alive.current&&version===revision.current)setBusy(false);}
  };
  const matchingItems=orderedMenu(items,organization).filter(item=>
    (category==="*"||categoryFor(item.productId,organization)===(category||null))&&
    item.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  const quantity=cart.reduce((total,line)=>total+line.quantity,0);
  const openCart=()=>{
    cartReview.current?.focus({preventScroll:true});
    cartReview.current?.scrollIntoView({block:"start"});
  };
  return <section aria-label="Menu and cart" aria-busy={busy}>
    <Typography.Title level={3}>Menu</Typography.Title>
    <p>Prices in {currency}. Pay at the cashier. Your cart stays on this page; refreshing clears it.</p>
    <Button disabled={busy} onClick={()=>void load()} style={{minHeight:44}}>Refresh menu</Button>
    {busy&&<p role="status">Loading menu or totals…</p>}
    {notice&&<Alert role="status" type="warning" message={notice} style={{marginTop:12}}/>}
    {loaded&&items.length===0&&<p role="status">No menu items are available. Please ask staff.</p>}
    {loaded&&<div className="qr-menu-toolbar">
      <div className="qr-menu-filters">
        <label>Category <select aria-label="Menu category" value={category} onChange={e=>setCategory(e.target.value)}><option value="*">All categories</option>{organization.categories.map(c=><option key={c.id} value={c.id}>{c.name}</option>)}<option value="">Uncategorized</option></select></label>
        <label>Search menu <input aria-label="Search QR menu" type="search" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      </div>
      {quantity>0&&<div className="qr-cart-jump">
        <Typography.Text aria-live="polite">{quantity} {quantity===1?"item":"items"}{quote&&` · ${money(quote.totalAmount)} quoted`}</Typography.Text>
        <Button onClick={openCart} style={{minHeight:44}}>View cart</Button>
      </div>}
    </div>}
    {loaded&&items.length>0&&matchingItems.length===0&&<p role="status">No items match these filters. Your cart is unchanged.</p>}
    <ul style={{padding:0,listStyle:"none"}}>{matchingItems.map(item=><li key={item.productId} style={{borderBottom:"1px solid #ddd",padding:"16px 0",display:"flex",alignItems:"center",gap:12,flexWrap:"wrap"}}>
      <MenuPhoto src={`/bff/guest/qr/menu/${item.productId}/photo`} name={item.name}/><div style={{flex:"1 1 120px",overflowWrap:"anywhere"}}><strong>{item.name}</strong><div>{money(item.unitPrice)}</div>{!item.available&&<Tag color="volcano">Sold out</Tag>}</div>
      <Button aria-label={`Add ${item.name}`} style={{minHeight:44}} disabled={!item.available||busy||(cart.find(l=>l.productId===item.productId)?.quantity??0)>=99||(!cart.some(l=>l.productId===item.productId)&&cart.length>=50)}
        onClick={()=>update(item.productId,(cart.find(l=>l.productId===item.productId)?.quantity??0)+1)}>Add</Button>
    </li>)}</ul>
    <section className="qr-cart-review" ref={cartReview} tabIndex={-1} aria-labelledby="qr-cart-heading">
    <Typography.Title level={3} id="qr-cart-heading">Your cart</Typography.Title>
    <Typography.Paragraph>Table {tableCode}</Typography.Paragraph>
    {cart.length===0?<p>Your cart is empty.</p>:<ul style={{padding:0,listStyle:"none"}}>{cart.map(line=>{
      const item=items.find(i=>i.productId===line.productId);const name=item?.name??"Removed menu item";
      return <li key={line.productId} style={{padding:"12px 0",display:"flex",gap:12,alignItems:"center",flexWrap:"wrap"}}>
        <span style={{flex:"1 1 140px",overflowWrap:"anywhere"}}>{name}{(!item||!item.available)&&" — unavailable"}</span>
        <label>Quantity <input aria-label={`Quantity for ${name}`} type="number" inputMode="numeric" min={1} max={99} value={line.quantity}
          style={{width:64,minHeight:44,fontSize:16}} onChange={e=>update(line.productId,Number(e.target.value))}/></label>
        <Button style={{minHeight:44}} aria-label={`Remove ${name}`} onClick={()=>update(line.productId,0)}>Remove</Button>
      </li>;
    })}</ul>}
    <Button block type="primary" style={{minHeight:44}} disabled={busy||cart.length===0} onClick={()=>void review()}>Review totals</Button>
    {quote&&<section aria-label="Server totals" aria-live="polite">
      {changed&&<Alert type="warning" message="Prices or pricing policy changed. Review the updated totals carefully."/>}
      <ul>{quote.lines.map(l=><li key={l.productId} style={{overflowWrap:"anywhere"}}>{l.name} × {l.quantity}: {money(l.lineTotal)} ({money(l.unitPrice)} each)</li>)}</ul>
      <dl><dt>Menu amount {quote.taxInclusive?"(tax included)":"(before tax)"}</dt><dd>{money(quote.menuAmount)}</dd>
        <dt>Subtotal before tax</dt><dd>{money(quote.subtotalAmount)}</dd>
        <dt>Service charge ({quote.serviceChargePercent}%)</dt><dd>{money(quote.serviceChargeAmount)}</dd>
        <dt>Tax ({quote.taxPercent}%)</dt><dd>{money(quote.taxAmount)}</dd>
        <dt><strong>Total ({quote.currency})</strong></dt><dd><strong>{money(quote.totalAmount)}</strong></dd></dl>
      <p>Live price preview, valid for at most two minutes. No items are reserved.</p>
      {submit&&quote.quoteId&&<Button block style={{minHeight:44}} type="primary" disabled={busy} onClick={()=>submit(quote)}>Submit for staff review</Button>}
    </section>}
    <p role="note">{submit?"Submitting requests staff review only. Staff acceptance and cashier payment are still required.":"Order submission is not available yet. Please ask staff to place your order."}</p>
    </section>
  </section>;
}
