import {chromium,request} from '@playwright/test';
import {readFile,writeFile} from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import {createServer} from 'node:http';
import {draftToken} from './draft-auth.mjs';

const env=process.env,run=env.NEXACONNECT_DRAFT_RUN,mode=env.NEXACONNECT_DRAFT_CHECKOUT_MODE;
if(!/^[a-f0-9]{32}$/.test(run)||!['baseline','interrupted','recovered','broker-paused','broker-resumed'].includes(mode))throw new Error('Invalid joined draft mode.');
const state=JSON.parse(await readFile(env.NEXACONNECT_DRAFT_STATE,'utf8')),ref=JSON.parse(await readFile(env.NEXACONNECT_DRAFT_REFERENCE,'utf8'));
if(state.runId!==run||ref.runId!==run||!ref.completed||state.realm!==`nexa-review-it-${run}`||state.authority!==`http://127.0.0.1:${state.infrastructure.keycloak}/realms/${state.realm}`)throw new Error('Owned draft required.');
for(const [name,value] of Object.entries(state.urls)){const url=new URL(value);if(url.hostname!=='127.0.0.1'||url.protocol!==(name==='Bff'?'https:':'http:'))throw new Error('Owned loopback endpoints required.');}
const journalPath=env.NEXACONNECT_DRAFT_REFERENCE+'.checkout.json';
let memo;try{memo=JSON.parse(await readFile(journalPath,'utf8'));}catch(error){if(error.code!=='ENOENT')throw error;memo={runId:run,orders:{},checks:{}};}
if(memo.runId!==run)throw new Error('Joined journal mismatch.');
const browser=await chromium.launch(),api=await request.newContext({ignoreHTTPSErrors:true,timeout:60000});
let stage='identity';const tokens={};const roles={'cashier':env.NEXACONNECT_DRAFT_CASHIER_PASSWORD,'kitchen-operator':env.NEXACONNECT_DRAFT_KITCHEN_PASSWORD,'store-manager':env.NEXACONNECT_DRAFT_MANAGER_PASSWORD,'accountant':env.NEXACONNECT_DRAFT_ACCOUNTANT_PASSWORD};
const assert=(condition,message)=>{if(!condition)throw new Error(message);};
const save=()=>writeFile(journalPath,JSON.stringify(memo));
function headers(role='cashier',organization=ref.organizationId){return {Authorization:'Bearer '+tokens[role],'X-Nexa-Organization-Id':organization,'X-Nexa-Application-Code':'nexa_connect','X-Nexa-Terminal-Id':ref.terminalId,'X-Correlation-ID':run};}
async function send(service,path,body,role='cashier',method=body?'POST':'GET',organization=ref.organizationId){
 const response=await api.fetch(new URL(path,state.urls[service]).href,{method,headers:headers(role,organization),...(body?{data:body}:{})});let data;try{data=await response.json();}catch{}
 return {status:response.status(),data};
}
async function poll(operation,message,timeout=60000){const until=Date.now()+timeout;while(Date.now()<until){if(await operation())return;await new Promise(resolve=>setTimeout(resolve,500));}throw new Error(message);}
async function stock(){const response=await send('Inventory',`/api/inventory/v1/branches/${ref.branchId}/stock`);assert(response.status===200,'Stock inspection failed');return Object.fromEntries(response.data.map(p=>[p.productId,p.availableQuantity]));}
const sameStock=(a,b)=>ref.products.every(id=>a[id]===b[id]);
async function queue(role='kitchen-operator'){const response=await send('Kitchen',`/api/kitchen/v1/branches/${ref.branchId}/tickets`,undefined,role);assert(response.status===200,'Queue denied');return response.data;}
async function tickets(order){return (await queue()).items.filter(t=>t.orderId===order);}
async function orderState(order){return (await send('Order',`/api/order/v1/orders/${order}`)).data?.status;}
async function quote(name,lines=[{productId:ref.products[0],quantity:1}],paymentMethod='cash_manual'){
 const id=randomUUID();const command={organizationId:ref.organizationId,restaurantId:ref.restaurantId,branchId:ref.branchId,currency:'THB',paymentMethod,orderId:id,idempotencyKey:id,correlationId:id,lines};
 const response=await send('Order','/api/order/v1/workflows/quote',command);assert(response.status===200,'Quote failed');command.pricingFingerprint=response.data.fingerprint;
 memo.orders[name]={command,amount:response.data.pricing.totalAmount};await save();return memo.orders[name];
}
async function place(value){return send('Order','/api/order/v1/workflows/place',value.command);}
async function accepted(value){await poll(async()=>await orderState(value.command.orderId)==='KitchenAccepted','Original order did not recover');}
async function transition(ticket,status,role='kitchen-operator'){return send('Kitchen',`/api/kitchen/v1/branches/${ref.branchId}/tickets/${ticket.ticketId}/transitions`,{targetStatus:status,expectedConcurrencyVersion:ticket.concurrencyVersion},role);}
async function complete(ticket){let current=ticket;for(const target of ['InProgress','Ready','Completed']){const response=await transition(current,target);assert(response.status===200,'Kitchen lifecycle failed');current=response.data;}return current;}
async function settle(value,method='cash',confirmed=true){
 value.settlement??={organizationId:ref.organizationId,branchId:ref.branchId,terminalId:ref.terminalId,idempotencyKey:randomUUID(),method,amount:value.amount,currency:'THB',receiptConfirmed:confirmed,...(method==='promptpay_manual'?{bankReference:'fictional-draft-verification'}:{})};await save();
 return send('Order',`/api/order/v1/orders/${value.command.orderId}/manual-settlement`,value.settlement);
}
async function cancel(value){value.cancellation??={organizationId:ref.organizationId,branchId:ref.branchId,operationId:randomUUID(),reason:'Fictional draft cancellation'};await save();return send('Order',`/api/order/v1/orders/${value.command.orderId}/cancellations`,value.cancellation);}
async function loseCommittedResponse(value){
 let forwarded=false;
 const proxy=createServer(async(req,res)=>{try{const result=await place(value);assert(result.status===200,'Placement upstream failed');forwarded=true;res.destroy();}catch{res.writeHead(503);res.end();}});
 await new Promise(resolve=>proxy.listen(0,'127.0.0.1',resolve));
 try{let lost=false;try{await api.post(`http://127.0.0.1:${proxy.address().port}/place`,{data:value.command,maxRetries:0});}catch{lost=true;}assert(lost&&forwarded,'Response-loss boundary was not executed');}
 finally{await new Promise(resolve=>proxy.close(resolve));}
}
try{
 for(const [role,password] of Object.entries(roles))tokens[role]=await draftToken(browser,api,state,ref,role,password);
 if(mode==='baseline'){
  stage='shift-and-drawer';const opened=await send('POS','/api/pos/v1/shifts/open',{branchId:ref.branchId,storeId:ref.storeId,terminalId:ref.terminalId,shiftNumber:'DRAFT-'+run.slice(0,8)});assert(opened.status===200,'Shift failed');memo.shiftId=opened.data.shiftId;
  const drawer=await send('POS','/api/pos/v1/cash-sessions/open',{shiftId:memo.shiftId,storeId:ref.storeId,currency:'THB',openingAmount:100});assert(drawer.status===200,'Drawer failed');memo.sessionId=drawer.data.cashSessionId;await save();
  stage='price-reconfirmation';const cash=await quote('cash',[{productId:ref.products[0],quantity:1},{productId:ref.products[2],quantity:1}]);
  const menu=await send('Catalog',`/api/catalog/v1/branches/${ref.branchId}/menu-items`,undefined,'store-manager');const product=menu.data.find(p=>p.productId===ref.products[0]);
  assert((await send('Catalog',`/api/catalog/v1/branches/${ref.branchId}/menu-items`,{...product,unitPrice:81},'store-manager')).status===201,'Price change failed');
  const changed=await place(cash);assert(changed.status===409&&changed.data.code==='pricing_changed','Stale quote was accepted');assert((await send('Order',`/api/order/v1/orders/${cash.command.orderId}`)).status===404,'Stale quote created an order');
  const fresh=await send('Order','/api/order/v1/workflows/quote',cash.command);cash.command.pricingFingerprint=fresh.data.fingerprint;cash.amount=fresh.data.pricing.totalAmount;await save();
  stage='response-loss-and-concurrent-placement';await loseCommittedResponse(cash);const repeats=await Promise.all([place(cash),place(cash)]);assert(repeats.every(r=>r.status===200&&r.data.orderId===cash.command.orderId),'Exact placement retry changed identity');await accepted(cash);
  let rows=await tickets(cash.command.orderId);assert(rows.length===2&&new Set(rows.map(t=>t.lines[0].preparationStation)).size===2,'Station ticket replay duplicated or lost work');
  let balance=await stock();assert(balance[ref.products[0]]===99&&balance[ref.products[2]]===99,'Stock deducted more than once');
  stage='kitchen-contention-and-read-only';const readOnly=await queue('accountant');assert(readOnly.canTransition===false,'Read-only role acquired transition access');assert((await transition(rows[0],'InProgress','accountant')).status===403,'Read-only transition accepted');
  const contention=await Promise.all([transition(rows[0],'InProgress'),transition(rows[0],'InProgress')]);assert(contention.map(r=>r.status).sort().join(',')==='200,409','Kitchen contention did not reject stale version');
  const winner=contention.find(r=>r.status===200).data;for(const target of ['Ready','Completed']){const reply=await transition(target==='Ready'?winner:cash.ready,target);assert(reply.status===200,'Kitchen advance failed');cash.ready=reply.data;}
  await complete(rows[1]);assert(await orderState(cash.command.orderId)==='KitchenAccepted','Kitchen completion falsely paid Order');
  stage='cash-settlement-and-receipt';const results=await Promise.all([settle(cash),settle(cash)]);assert(results.map(r=>r.status).sort().join(',')==='200,201','Concurrent exact settlement failed');assert(results[0].data.settlementId===results[1].data.settlementId,'Settlement identity changed');
  assert((await send('Order',`/api/order/v1/orders/${cash.command.orderId}/manual-settlement`,{...cash.settlement,amount:cash.amount+1})).status===409,'Changed settlement replay accepted');
  const receipt=await send('Order',`/api/order/v1/orders/${cash.command.orderId}/receipt?branchId=${ref.branchId}`);assert(receipt.status===200,'Paid receipt missing');cash.receipt=receipt.data;
  const reprint=await send('Order',`/api/order/v1/orders/${cash.command.orderId}/receipt?branchId=${ref.branchId}`);assert(JSON.stringify(reprint.data)===JSON.stringify(cash.receipt),'Receipt changed on read');
  stage='manual-promptpay';const prompt=await quote('promptpay',[{productId:ref.products[1],quantity:1}],'promptpay_manual');assert((await place(prompt)).status===200,'PromptPay placement failed');
  assert((await settle(prompt,'promptpay_manual',false)).status===400,'Unverified PromptPay accepted');prompt.settlement.receiptConfirmed=true;assert((await settle(prompt,'promptpay_manual')).status===201,'Verified fictional PromptPay failed');assert((await settle(prompt,'promptpay_manual')).status===200,'PromptPay exact retry failed');
  stage='shortage';const short=await quote('shortage',[{productId:ref.products[0],quantity:1000}]);assert((await place(short)).status===409,'Insufficient stock accepted');assert((await tickets(short.command.orderId)).length===0,'Rejected order reached Kitchen');
  stage='preparation-cancellation';const before=await stock();const cancelled=await quote('cancel',[{productId:ref.products[0],quantity:1},{productId:ref.products[2],quantity:1}]);assert((await place(cancelled)).status===200,'Cancellation order failed');rows=await tickets(cancelled.command.orderId);assert((await transition(rows[0],'InProgress')).status===200,'Preparation did not start');assert((await cancel(cancelled)).data.status==='completed','Active preparation did not cancel');assert((await cancel(cancelled)).data.replayed===true,'Cancellation replay failed');assert(sameStock(await stock(),before),'Cancellation released stock more than once or incompletely');
  stage='terminal-cancellation';const blocked=await quote('blocked',[{productId:ref.products[0],quantity:1},{productId:ref.products[2],quantity:1}]);assert((await place(blocked)).status===200,'Terminal cancellation order failed');rows=await tickets(blocked.command.orderId);await complete(rows[0]);const retained=await stock();const conflict=await cancel(blocked);assert(conflict.status===409&&conflict.data.status==='blocked','Completed food silently cancelled');assert(sameStock(await stock(),retained),'Completed food released stock');assert(await orderState(blocked.command.orderId)==='CancellationReview','Blocked order is not explicit review');assert((await tickets(blocked.command.orderId)).length===1,'Cancellation partially removed unfinished station work');
  stage='ordinary-declined-payment-compensation';const original=await stock();const compensation=await quote('compensation',undefined,'card');const failed=await place(compensation);assert(failed.status===409&&[5,'PaymentFailed'].includes(failed.data.status)&&['PaymentFailed','Rejected'].includes(await orderState(compensation.command.orderId)),'Disabled provider failure did not compensate');assert(sameStock(await stock(),original),'Ordinary compensation lost or double-released stock');assert((await tickets(compensation.command.orderId)).length===0,'Ordinary compensation retained active ticket');assert((await send('Order',`/api/order/v1/orders/${compensation.command.orderId}/receipt?branchId=${ref.branchId}`)).status===404,'Failed provider intent produced paid receipt');
  stage='tenant-and-role-denial';assert((await send('Order',`/api/order/v1/orders/${cash.command.orderId}/manual-settlement`,cash.settlement,'accountant')).status===403,'Accountant collected payment');assert((await send('Kitchen',`/api/kitchen/v1/branches/${ref.deniedBranchId}/tickets`,undefined,'kitchen-operator')).status===403,'Kitchen crossed branch');assert([403,404].includes((await send('Order',`/api/order/v1/orders/${cash.command.orderId}/receipt?branchId=${ref.branchId}`,undefined,'cashier','GET',ref.otherOrganizationId)).status),'Receipt crossed tenant');
  memo.checks.baseline=true;
 }else if(mode==='interrupted'){
  stage='kitchen-outage';const value=await quote('interrupted');let result;try{result=await place(value);}catch{result={status:503};}
  assert(result.status===503,'Kitchen outage was not surfaced');assert(['Submitted','InventoryReserved'].includes(await orderState(value.command.orderId)),'Missing durable interrupted order');memo.checks.kitchenOutage=true;
 }else if(mode==='recovered'){
  stage='order-and-kitchen-restart';const value=memo.orders.interrupted;await accepted(value);const retries=await Promise.all([place(value),place(value)]);assert(retries.every(r=>r.status===200),'Recovered exact checkout failed');assert((await tickets(value.command.orderId)).length===1,'Restart duplicated station ticket');
  assert((await stock())[ref.products[0]]===97,'Restart duplicated stock deduction');memo.checks.processRecovery=true;
 }else if(mode==='broker-paused'){
  stage='broker-outage-settlement';const value=await quote('broker');assert((await place(value)).status===200,'Broker outage blocked durable manual checkout');assert((await settle(value)).status===201,'Broker outage lost settlement');assert(await orderState(value.command.orderId)==='Paid','Broker outage lost Paid state');memo.checks.brokerDurableCommit=true;
 }else{
  stage='broker-delivery-recovery';const value=memo.orders.broker;assert((await settle(value)).status===200,'Broker recovery duplicated settlement');const receipt=await send('Order',`/api/order/v1/orders/${value.command.orderId}/receipt?branchId=${ref.branchId}`);assert(receipt.status===200,'Broker recovery lost receipt');
  await poll(async()=>{const response=await send('POS',`/api/pos/v1/cash-sessions/${memo.sessionId}/summary`);return response.data?.netMovementAmount===memo.orders.cash.amount+value.amount;},'Cash projection did not recover');memo.checks.brokerRecovery=true;
 }
 await save();await writeFile(journalPath+'.'+mode+'.verification.json',JSON.stringify({runId:run,mode,passed:true,checks:memo.checks,realIdentity:true,financialFixturesSeeded:false}));console.log('Joined draft '+mode+' verified.');
}catch(error){console.error('Joined draft failed at '+stage+' ('+error.constructor.name+').');process.exitCode=1;}
finally{for(const role of Object.keys(tokens))delete tokens[role];await api.dispose();await browser.close();}
