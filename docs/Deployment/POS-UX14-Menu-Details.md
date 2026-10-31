# POS UX14 — menu item editor and safe price changes

UX15 consolidates the manager workflow below into **Menu management**: choose a branch,
**Load menu**, select an item, then open **Details**. The standalone workflow below is retained
for historical/compatibility deep links. See [current workflow and draft guards](POS-UX15-Menu-Management.md).

## Status and workflow

UX14 passes the complete source gate and is activated in the retained local pilot on
31 October 2026 with Catalog9 and the matching POS package. All twelve services are Ready,
and POS reopened with a responsive window. Actual operator/device acceptance remains separate.
The user reports UX13 working;
this does not complete unrelated acceptance cases.

1. Open Customer Portal **Menu item editor** in the NexaConnect workspace.
2. Select **Menu editor branch**, then **Load menu items**. Find an existing item by name.
3. Choose **Edit**. Change its name, THB unit price and/or preparation station. Existing
   station names are suggested; an authorized manager may enter a new station label.
4. Choose **Review menu changes**, inspect before/after values, then **Confirm menu changes**.
   Changing a reviewed field removes confirmation until reviewed again. Cancel sends nothing.
5. Refresh POS and phone menus. Recheck fresh quotes/totals for new orders. Accepted orders,
   existing Kitchen tickets, original collection, payments and receipts retain their snapshots.

Currency/product identity remain unchanged. Availability (including sold-out state), categories,
photos and barcodes are managed independently. Editor uses live branch menu.read/write, at
most200 items,120-character names,80-character station labels and the existing THB/two-decimal
priced-checkout limits. No item creation, modifiers, combos, discounts or stock edits are added.
Conflict, denial, timeout or uncertain save clears editable rows and requires reload rather
than automatically retrying. Branch/workspace changes cancel stale responses and discard drafts.
See [API and validation](../API/Menu-Details.md).

## Rollout and preservation

Back up retained databases and protected POS state. Close POS and stop all old Catalog writers,
apply **Catalog9 / compatibility0.44.0**, and deploy matching Catalog/BFF/frontend/POS. Older
upserts omit version increments; do not run them alongside the new editor. Matching legacy
upserts remain unversioned for callers but advance editor versions and retain their old audit/
event path. No other owning schema, role, client or secret changes. Existing Services:Catalog
and Services:PlatformDirectory are reused through new correlated redirect-disabled clients.

Preserve current scope, certificates, payment configuration, approvals, preferences, original
orders/shifts and protected financial recovery. Migration initializes only the new version to
zero; it does not edit existing menu values. Read-only live verification needs no metadata,
availability, financial or physical drawer command. Fresh source/pilot/package bindings and
operator packs remain separate. Retained history refuses [downgrade](../Database/Menu-Details.md).

## Verification and pending acceptance

Candidate `4023a3d6-ffd8-41ab-9d62-769c5946c653` passes the full source-bound gate: 1,390 unit,
seven architecture, 136 HTTP and 90 frontend unit tests; 75 QR, 22 portal gallery/category/
availability/editor and 45 financial browser cases; native input/session/shutdown/printing
fixtures; hosting/profile/evidence guards; thirteen owning database cases with broker recovery
and cleanup; and four drawer-authority cases with cleanup. Both dependency audits report zero
vulnerabilities. Matching source-bound POS package `2ede89a98d63402a9bd624d419af16c4` is
installed with Catalog9. No retained metadata, availability, financial or device commands
were issued during activation.

Activation completed at 07:07 Singapore time on 31 October. Fresh QR pilot
`9d30a4bf06b64c11a0bb21ca819b9af7` retains the original HTTPS origin and certificate bytes.
Eight protected local files remain byte-identical before launch; all eleven pilot databases
were backed up. Original columns/data compare unchanged in 29 Catalog, 54 POS, ten Restaurant,
31 Order and five Media business tables (migration/outbox/inbox metadata excluded). The new
menu_version column is separately verified as zero and the new details audit table is empty;
Catalog comparison projects out that added column rather than claiming identical whole
COPY sections. Existing availability versions/history, names/prices/stations, layout version1,
photo/barcode references and payment settings are preserved. Catalog9 runtime grants, safe
PNG delivery and access denial pass: workload layout/details/availability reads403; guest
layout without a cookie401.

Fresh operator pack `f69bc634f80f4e81aaffe4eddf168440` has a valid installed binding and all
17 cases pending. `qrCandidateFresh=true` does not change `currentSourceVerified=false`,
`operatorCasesComplete=false`, `releaseReady=false` or `productionVerified=false`.

Tests cover domain validation/CAS/no-op, permission recheck, concurrent metadata writers and
independent availability/version preservation,
legacy invalidation, actual BFF session/CSRF/tenant/response-size enforcement and bounded scoped
HTTP adapters. Browser cases cover review/confirmation, review invalidation, conflict and branch
clearing. Owning PostgreSQL covers audit/outbox atomicity, rollback, references, concurrent
writers, legacy writes and downgrade. Existing OrderPricingTests prove changed prices require
reconfirmation before reservation/order creation and accepted replay uses the original snapshot;
the existing QR owning journey verifies price review/confirmation and accepted-order recovery.

Run `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` for the full source-bound gate,
including native input/shutdown/printing, QR/financial browser cases, owning cleanup and broker
recovery. Automation is isolated/synthetic and does not certify retained merchant/device use.
After activation, change one item and verify portal/POS/iOS/Android refresh, a cart/quote made
before the change, pending guest price consent and an original accepted order/receipt. Verify
sold-out status and photo/category/barcode references remain. Record actual observations
separately. Physical drawer opening stays disabled; hosted/production readiness remains pending.
