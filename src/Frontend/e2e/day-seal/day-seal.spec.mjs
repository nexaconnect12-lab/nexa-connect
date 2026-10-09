import {test,expect} from "@playwright/test";
const branch="11111111-1111-1111-1111-111111111111",org="33333333-3333-3333-3333-333333333333",restaurant="55555555-5555-5555-5555-555555555555";
const ref={manifestId:branch,generation:1,evidenceVersion:"a".repeat(64),revisionEpoch:org,sourceRevision:0};
const seal={sealId:org,manifestId:branch,revisionEpoch:org,sourceRevision:0};
const snapshot=()=>({timeZone:"UTC",currency:"THB",fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z",grossSales:100,completedRefunds:0,netSales:100,cashVariance:0,tenders:[],orderVersion:"a".repeat(64),paymentVersion:"a".repeat(64),posVersion:"a".repeat(64),unresolvedOrders:0,unresolvedPayments:0,unresolvedRefunds:0,openShifts:0,openCashSessions:0,pendingCashReviews:0,issues:[],observedAtUtc:"2026-09-03T00:00:00Z",cutoff:{order:ref,payment:ref,pos:ref,checkId:restaurant,sourcesCurrent:true,financialGaps:0,deliveryComplete:true,evidenceProtocolVersion:2}});
async function setup(page){
 const state={status:"not_prepared",version:0,canPrepare:true,pending:0,fail:false,bad:false,reviewed:true,posts:[],headers:[]};
 await page.route("**/bff/customer/**",async route=>{
  const request=route.request(),url=new URL(request.url()),path=url.pathname;let json;
  if(path.endsWith("/me"))json={subjectId:"manager"};
  else if(path.endsWith("/access"))json={subjectId:"manager",organizations:[{organizationId:org,organizationCode:"one",organizationName:"One",applicationCode:"nexa_connect"}]};
  else if(path.endsWith("/tenant"))json={subjectId:"manager",organizationId:org,applicationCode:"nexa_connect"};
  else if(path.endsWith("/day-close-cutoffs"))json={identity:{organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"},version:2,status:state.reviewed?"ready_for_review":"blocked",snapshot:snapshot(),blockers:state.reviewed?[]:["cutoff_superseded"],validatedAtUtc:"2026-09-03T00:00:00Z",canPrepare:state.canPrepare,pendingCommand:null};
  else if(path.endsWith("/day-close-seals/csrf"))json={requestToken:"csrf-only"};
  else if(path.endsWith("/day-close-seals")){
   if(request.method()==="POST"){
    state.posts.push(request.postDataJSON());state.headers.push(request.headers());
    if(state.fail)return route.fulfill({status:503,json:{title:"restricted-source-error"}});
    state.status="ready_for_review";state.version+=2;
   }
   const saved=state.version?{...snapshot(),seals:{order:seal,payment:seal,pos:seal,pendingChanges:0,journalComplete:!state.bad,deliveryComplete:true}}:null;
   json={identity:{organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"},version:state.version,status:state.status,snapshot:saved,blockers:state.status==="blocked"?["sealed_changes_pending"]:[],validatedAtUtc:state.status==="ready_for_review"?"2026-09-03T00:00:00Z":null,canPrepare:state.canPrepare,pendingCommand:null,pendingSealChanges:state.pending,latestSealComparison:state.comparison??null};
  }else if(path.endsWith("/day-close-approvals/csrf"))json={requestToken:"approval-csrf"};
  else if(path.endsWith("/day-close-approvals")){
   const identity={organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"};
   const saved={...snapshot(),seals:{order:seal,payment:seal,pos:seal,pendingChanges:0,journalComplete:true,deliveryComplete:true}};
   if(request.method()==="POST"){
    (state.approvalPosts??=[]).push(request.postDataJSON());(state.approvalHeaders??=[]).push(request.headers());
    if(state.approvalFail)return route.fulfill({status:503,json:{title:"private-upstream-body"}});
    state.approvalDecision={approvalId:restaurant,operationId:request.postDataJSON().operationId,identity,approvalVersion:1,sealVersion:2,approverSubject:"manager",reasonCode:"review_complete",approvedAtUtc:"2026-09-03T00:00:02Z",sourceValidatedAtUtc:"2026-09-03T00:00:00Z",validationCheckId:org,snapshot:saved};
   }
   const current=state.approvalStatus??(state.approvalDecision?"approved":"not_approved");
   const fresh=current==="not_approved"||current==="approved";
   json={identity,version:state.approvalDecision?1:0,status:current,decision:state.approvalDecision??null,history:state.approvalDecision?[state.approvalDecision]:[],historyTruncated:false,validatedAtUtc:fresh?new Date().toISOString():null,canApprove:state.approvalPermission!==false,sealVersion:fresh?2:null,sealSnapshot:fresh?saved:null,reason:"review_complete",operationDecision:request.method()==="POST"?state.approvalDecision:null};
  }else if(path.includes("/day-close-finalization-preparations")){
   if(path.endsWith("/csrf"))return route.fulfill({json:{requestToken:"finalization-csrf"}});
   const identity={organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"};
   if(request.method()==="POST"){
    (state.finalPosts??=[]).push(request.postDataJSON());(state.finalHeaders??=[]).push(request.headers());
    if(state.finalFail)return route.fulfill({status:503,json:{title:"private-finalization-body"}});
    if(path.endsWith("/cancel"))state.finalStatus="cancelled";
    else{state.finalCommand??=request.postDataJSON();state.finalStatus="prepared";state.finalExpires??=new Date(Date.now()+240_000).toISOString();}
   }
   const status=state.finalStatus??"not_prepared",pendingCommand=state.finalCommand??null,expiresAtUtc=state.finalExpires??null;
   const sources=pendingCommand?["Order","Payment","POS"].map(source=>({source,sealId:org,epoch:org,revision:0,expiresAtUtc,active:status==="prepared",cancelled:status==="cancelled"})):[];
   json={identity,version:pendingCommand?2:0,status,pendingCommand,approvalId:pendingCommand?.approvalId??null,reviewedApprovalVersion:pendingCommand?.reviewedApprovalVersion??null,sealVersion:pendingCommand?2:null,expiresAtUtc,validatedAtUtc:status==="prepared"?new Date().toISOString():null,sources,blockers:[],canPrepare:state.finalPermission!==false};
  }else if(path.endsWith("/day-close-settlements")){
   const identity={organizationId:org,restaurantId:restaurant,branchId:branch,businessDate:"2026-09-01"},command={branchId:branch,businessDate:"2026-09-01",operationId:branch,expectedPreparationVersion:2,preparationOperationId:org,approvalId:branch,reviewedApprovalVersion:1};
   const sources=["Order","Payment","POS"].map(source=>({source,proof:{command:{settlementId:org,operationId:branch,fence:{operationId:org,approvalId:branch,sealId:org,expiresAtUtc:"2026-09-03T00:04:00Z",window:{organizationId:org,restaurantId:restaurant,branchId:branch,fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z"}}},phase:"committed",decisionId:branch,changedAtUtc:"2026-09-03T00:00:00Z",lateWorkCount:1}}));
   json={settlement:{id:org,identity,command,status:"finalized",decisionId:branch,sources,receipt:{settlementId:org,identity,decisionId:branch,eventId:branch,correlationId:branch,settledAtUtc:"2026-09-03T00:00:00Z",approvalId:branch,sealVersion:2,snapshot:{currency:"THB",grossSales:100,completedRefunds:0,netSales:100,cashVariance:0,tenders:[]},sources:sources.map(x=>({source:x.source,sealId:org,epoch:org,revision:0}))}},canFinalize:false,sourceProofCurrent:true};
  }else if(path.includes("/day-close-late-work")){
   if(path.endsWith("/csrf"))return route.fulfill({json:{requestToken:"review-csrf"}});
   const scope={settlementId:org,window:{organizationId:org,restaurantId:restaurant,branchId:branch,fromUtc:"2026-09-01T00:00:00Z",toUtc:"2026-09-02T00:00:00Z"}};
   const item={workId:branch,eventType:"order.manual-tender-settled.v1",receivedAtUtc:"2026-09-03T00:00:00Z",occurredAtUtc:null,custodyReason:"late_delivery_for_settled_day",records:{orderid:org},version:state.reviewVersion??0,status:state.reviewStatus??"pending_review"};
   const detail=()=>({scope:state.reviewForged?{...scope,settlementId:branch}:scope,item:{...item,version:state.reviewVersion??0,status:state.reviewStatus??"pending_review"},settlementLinks:[org],history:state.reviewHistory??[],historyTruncated:false,canReview:state.reviewPermission!==false});
   if(request.method()==="POST"){
    (state.reviewPosts??=[]).push(request.postDataJSON());(state.reviewHeaders??=[]).push(request.headers());
    if(state.reviewFail)return route.fulfill({status:503,json:{title:"private-provider-body"}});if(state.reviewConflict)return route.fulfill({status:409,json:{title:"private-conflict"}});
    const c=request.postDataJSON().command;state.reviewVersion=c.expectedVersion+1;state.reviewStatus=c.decision==="investigate"?"investigating":c.decision==="require_correction"?"correction_required":"reviewed";
    const entry={version:state.reviewVersion,status:state.reviewStatus,decision:c.decision,reasonCode:c.reasonCode,reviewedAtUtc:"2026-09-03T00:01:00Z"};state.reviewHistory=[entry];json={operationId:c.operationId,operationDecision:entry,detail:detail()};
   }else json=path.endsWith(branch)?detail():{scope,items:[item],nextCursor:null,canReview:state.reviewPermission!==false};
  }else return route.fulfill({status:404});
  return route.fulfill({json});
 });
 await page.goto("/#end-of-day");await page.getByLabel("End-of-day branch ID").fill(branch);await page.getByLabel("End-of-day business date").fill("2026-09-01");return state;
}
async function load(page){await page.getByRole("button",{name:"Load cutoff evidence",exact:true}).click();await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();}
test("shows the sealed baseline beside historical source corrections",async({page})=>{
 const state=await setup(page);await load(page);await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();
 state.status="blocked";state.pending=2;state.comparison={grossSales:120,completedRefunds:5,netSales:115,cashVariance:-10,tenders:[{method:"cash",currency:"THB",amount:120}],unknownChanges:0,checkedAtUtc:"2026-09-03T01:00:00Z",changes:[{source:"Order",revision:1,reason:"sales_date",recordKind:"orders",recordId:org,parentId:org,beforeStatus:"completed",afterStatus:"completed",beforeFinancialVersion:1,afterFinancialVersion:2,beforeFinancialAtUtc:"2026-09-01T01:00:00Z",afterFinancialAtUtc:"2026-08-30T01:00:00Z"}],changesTruncated:true};
 await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();
 await expect(page.getByRole("columnheader",{name:"Sealed baseline",exact:true})).toBeVisible();
 await expect(page.getByRole("row").filter({has:page.getByText("Gross sales",{exact:true})})).toContainText("120");
 await expect(page.getByRole("row").filter({has:page.getByText("Cash variance",{exact:true})})).toContainText("-10");
 await expect(page.getByRole("row").filter({has:page.getByText("Tender: cash",{exact:true})})).toContainText("120");
 await expect(page.getByText(/Saved evidence: THB gross 100/)).toHaveCount(2);
 await expect(page.getByText(/Order: .*sales date/)).toBeVisible();
 await expect(page.getByText("The change list is limited. Pending counts include every relevant or unknown change.")).toBeVisible();
});
test("seals a reviewed version with CSRF and clears readiness when late changes are observed",async({page})=>{
 const state=await setup(page);await load(page);await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();
 expect(state.posts[0]).toMatchObject({expectedVersion:0,reviewedCutoffVersion:2,branchId:branch,businessDate:"2026-09-01"});expect(state.posts[0].organizationId).toBeUndefined();expect(state.headers[0]["x-nexa-csrf"]).toBe("csrf-only");
 state.status="blocked";state.pending=3;state.version=3;await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toHaveCount(0);await expect(page.getByText(/Pending source changes at the last check: 3/)).toBeVisible();
 await expect(page.getByRole("button",{name:/approve|finalize/i})).toHaveCount(0);
});
test("new sealing requires loaded ready cutoff evidence",async({page})=>{
 await setup(page);await page.getByRole("button",{name:"Load sealed evidence",exact:true}).click();await expect(page.getByRole("button",{name:"Seal reviewed evidence",exact:true})).toBeDisabled();
 await page.getByRole("button",{name:"Load cutoff evidence",exact:true}).click();await expect(page.getByRole("button",{name:"Seal reviewed evidence",exact:true})).toBeEnabled();
});
test("accountants have read-only seal controls",async({page})=>{
 const state=await setup(page);state.canPrepare=false;await load(page);await expect(page.getByRole("button",{name:"Seal reviewed evidence",exact:true})).toHaveCount(0);expect(state.posts).toHaveLength(0);
});
test("missing journal proof cannot display sealed readiness",async({page})=>{
 const state=await setup(page);state.version=2;state.status="ready_for_review";state.bad=true;await load(page);
 await expect(page.getByText("sealed evidence",{exact:true})).toHaveCount(0);await expect(page.getByText(/Saved evidence:/)).toHaveCount(1);
});
test("an uncertain response retains the exact sealing command",async({page})=>{
 const state=await setup(page);await load(page);state.fail=true;await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();
 await expect(page.getByRole("status")).toContainText("unavailable");await expect(page.getByRole("button",{name:"Resume sealing",exact:true})).toBeVisible();state.fail=false;await page.getByRole("button",{name:"Resume sealing",exact:true}).click();
 await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();expect(state.posts[1]).toEqual(state.posts[0]);await expect(page.getByText("restricted-source-error")).toHaveCount(0);
});
test("filter changes clear saved sealing state and a blocked cutoff disables new seals",async({page})=>{
 const state=await setup(page);await load(page);await page.getByRole("button",{name:"Seal reviewed evidence",exact:true}).click();await expect(page.getByText("sealed evidence",{exact:true})).toBeVisible();
 await page.getByLabel("End-of-day business date").fill("2026-09-02");await expect(page.getByText("sealed evidence",{exact:true})).toHaveCount(0);
 await page.getByLabel("End-of-day business date").fill("2026-09-01");state.reviewed=false;await load(page);await expect(page.getByRole("button",{name:"Reseal reviewed evidence",exact:true})).toBeDisabled();
});
test("approval requires the explicitly loaded matching seal and sends CSRF with reviewed versions",async({page})=>{
 const state=await setup(page);state.status="ready_for_review";state.version=2;
 await page.getByRole("button",{name:"Load approval",exact:true}).click();await expect(page.getByRole("button",{name:"Approve reviewed seal",exact:true})).toBeDisabled();
 await load(page);await page.getByRole("button",{name:"Approve reviewed seal",exact:true}).click();
 await expect(page.getByText("approved",{exact:true})).toBeVisible();
 expect(state.approvalPosts[0]).toMatchObject({branchId:branch,businessDate:"2026-09-01",expectedApprovalVersion:0,reviewedSealVersion:2});
 expect(state.approvalHeaders[0]["x-nexa-csrf"]).toBe("approval-csrf");expect(state.approvalPosts[0].organizationId).toBeUndefined();
});
test("an uncertain approval retries the exact operation without revealing dependency bodies",async({page})=>{
 const state=await setup(page);state.status="ready_for_review";state.version=2;await load(page);await page.getByRole("button",{name:"Load approval",exact:true}).click();
 state.approvalFail=true;await page.getByRole("button",{name:"Approve reviewed seal",exact:true}).click();
 await expect(page.getByRole("status")).toContainText("Approval unavailable");await expect(page.getByRole("button",{name:"Retry same approval",exact:true})).toBeEnabled();state.approvalFail=false;await page.getByRole("button",{name:"Retry same approval",exact:true}).click();
 await expect(page.getByText("approved",{exact:true})).toBeVisible();expect(state.approvalPosts[1]).toEqual(state.approvalPosts[0]);await expect(page.getByText("private-upstream-body")).toHaveCount(0);
});
test("read-only accountants cannot approve and changing filters clears approval state",async({page})=>{
 const state=await setup(page);state.approvalPermission=false;await page.getByRole("button",{name:"Load approval",exact:true}).click();await expect(page.getByRole("button",{name:"Approve reviewed seal",exact:true})).toHaveCount(0);
 await expect(page.getByText("not approved",{exact:true})).toBeVisible();await page.getByLabel("End-of-day business date").fill("2026-09-02");await expect(page.getByText("not approved",{exact:true})).toHaveCount(0);
});
test("superseded and unverified approval keep immutable history without showing current approval",async({page})=>{
 const state=await setup(page);state.status="ready_for_review";state.version=2;await load(page);await page.getByRole("button",{name:"Load approval",exact:true}).click();await page.getByRole("button",{name:"Approve reviewed seal",exact:true}).click();await expect(page.getByText("approved",{exact:true})).toBeVisible();
 for(const status of ["unverified","superseded"]){state.approvalStatus=status;await page.getByRole("button",{name:"Load approval",exact:true}).click();await expect(page.getByText(status,{exact:true})).toBeVisible();await expect(page.getByText("approved",{exact:true})).toHaveCount(0);await expect(page.getByText(/Seal version 2: review complete by manager/)).toBeVisible();}
});

test("finalization preparation requires a reviewed approval and cancellation uses CSRF",async({page})=>{
 const state=await setup(page);state.status="ready_for_review";state.version=2;
 await page.getByRole("button",{name:"Load finalization preparation",exact:true}).click();await expect(page.getByRole("button",{name:"Prepare finalization",exact:true})).toBeDisabled();
 await load(page);await page.getByRole("button",{name:"Load approval",exact:true}).click();await page.getByRole("button",{name:"Approve reviewed seal",exact:true}).click();await expect(page.getByText("approved",{exact:true})).toBeVisible();
 await page.getByRole("button",{name:"Prepare finalization",exact:true}).click();await expect(page.getByText("prepared",{exact:true})).toBeVisible();
 expect(state.finalPosts[0]).toMatchObject({expectedVersion:0,approvalId:restaurant,reviewedApprovalVersion:1});expect(state.finalHeaders[0]["x-nexa-csrf"]).toBe("finalization-csrf");
 await page.getByRole("button",{name:"Cancel finalization preparation",exact:true}).click();await expect(page.getByText("cancelled",{exact:true})).toBeVisible();expect(state.finalPosts[1].operationId).toBe(state.finalPosts[0].operationId);
});
test("uncertain preparation keeps the exact command for explicit retry",async({page})=>{
 const state=await setup(page);state.status="ready_for_review";state.version=2;await load(page);await page.getByRole("button",{name:"Load approval",exact:true}).click();await page.getByRole("button",{name:"Approve reviewed seal",exact:true}).click();await expect(page.getByText("approved",{exact:true})).toBeVisible();await page.getByRole("button",{name:"Load finalization preparation",exact:true}).click();
 state.finalFail=true;await page.getByRole("button",{name:"Prepare finalization",exact:true}).click();await expect(page.getByRole("status")).toContainText("Preparation unavailable");state.finalFail=false;await page.getByRole("button",{name:"Retry same preparation operation",exact:true}).click();await expect(page.getByText("prepared",{exact:true})).toBeVisible();expect(state.finalPosts[1]).toEqual(state.finalPosts[0]);await expect(page.getByText("private-finalization-body")).toHaveCount(0);
});
test("finalization accountant controls remain read only and filters clear progress",async({page})=>{
 const state=await setup(page);state.finalPermission=false;await page.getByRole("button",{name:"Load finalization preparation",exact:true}).click();await expect(page.getByRole("button",{name:"Prepare finalization",exact:true})).toHaveCount(0);await expect(page.getByText("not prepared",{exact:true})).toBeVisible();await page.getByLabel("End-of-day business date").fill("2026-09-02");await expect(page.getByText("not prepared",{exact:true})).toHaveCount(0);
});

async function reviewLoad(page){await page.getByRole("button",{name:"Load settlement progress",exact:true}).click();await page.getByRole("button",{name:"Load late work",exact:true}).click();await page.getByRole("button",{name:"View retained work",exact:true}).click();}
test("late-work manager decisions send exact scoped command and CSRF",async({page})=>{const state=await setup(page);await reviewLoad(page);await page.getByRole("button",{name:"Require correction",exact:true}).click();await expect(page.getByText(/Version 1: correction_required/)).toBeVisible();expect(state.reviewPosts[0].command).toMatchObject({workId:branch,expectedVersion:0,decision:"require_correction",reasonCode:"correction_needed"});expect(state.reviewPosts[0].organizationId).toBeUndefined();expect(state.reviewHeaders[0]["x-nexa-csrf"]).toBe("review-csrf");await expect(page.getByText("THB 100",{exact:true})).toHaveCount(2);});
test("uncertain review survives settlement reload and retries identical operation",async({page})=>{const state=await setup(page);await reviewLoad(page);state.reviewFail=true;await page.getByRole("button",{name:"Investigate delivery",exact:true}).click();await expect(page.getByRole("button",{name:"Retry same review decision"})).toBeEnabled();await page.getByRole("button",{name:"Load settlement progress",exact:true}).click();state.reviewFail=false;await page.getByRole("button",{name:"Retry same review decision"}).click();await expect(page.getByText(/Version 1: investigating/)).toBeVisible();expect(state.reviewPosts[1]).toEqual(state.reviewPosts[0]);await expect(page.getByText("private-provider-body")).toHaveCount(0);});
test("late-work accountant detail has no manager actions",async({page})=>{const state=await setup(page);state.reviewPermission=false;await reviewLoad(page);await expect(page.getByRole("button",{name:"Investigate delivery",exact:true})).toHaveCount(0);await expect(page.getByRole("button",{name:"Require correction",exact:true})).toHaveCount(0);expect(state.reviewPosts??[]).toHaveLength(0);});
test("review conflict requires reload and mismatched settlement detail is rejected",async({page})=>{const state=await setup(page);await reviewLoad(page);state.reviewConflict=true;await page.getByRole("button",{name:"Evidence checked",exact:true}).click();await expect(page.getByText("Review rejected. Load current work before deciding again.")).toBeVisible();await expect(page.getByRole("button",{name:"Retry same review decision"})).toHaveCount(0);await page.getByRole("button",{name:"Load late work",exact:true}).click();state.reviewForged=true;await page.getByRole("button",{name:"View retained work",exact:true}).click();await expect(page.getByRole("status")).toContainText("unavailable or changed");await expect(page.getByRole("button",{name:"Require correction",exact:true})).toHaveCount(0);});
