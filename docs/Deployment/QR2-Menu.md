# QR2 mobile menu and cart

QR3 now optionally retains reviewed quotes and adds pending submissions/private status
and staff rejection. The QR2 results below remain historical. Current QR persistence
launcher requires four cases, including owning Order requests and accepted Inventory/Kitchen
recovery. QR4 adds staff acceptance; see [QR3](QR3-Requests.md) and [QR4](QR4-Acceptance.md).


QR2 source implements online menu/cart previews after QR1 admission. Separately enabled
QR3 adds submission, QR4 adds staff acceptance/Kitchen, and cashier collection remains QR5.
Physical phones/public hosting,
server/device specifications, signing and exact release freeze remain pending. No service
was deployed and no printed QR/physical-phone acceptance is claimed by local tests.

## Isolated enablement

1. Follow [QR1 prerequisites](QR1-Entry.md): Restaurant migration 4, live staff scope,
   trusted HTTPS Customer origin and private downstream services. Keep Restaurant off
   the public edge. Preserve the existing dedicated-server and release prerequisites.
2. Use current Catalog and Order binaries with owning database configuration. Catalog
   requires its existing migration 4; QR2 alone requires Order migration 19. QR3 requires
   Order migrations20–21 with current QR readers, including when acceptance is off. QR2 adds no migration,
   schema/version change, seed, permission or guest identity. Catalog menu rows must have
   the owning organization/branch; legacy empty-owner rows are not guest-visible.
3. Configure direct HTTPS `Services__Restaurant`, `Services__Catalog` at Order and
   `Services__Order` at BFF. Existing Order `WorkloadIdentity` must issue exact
   `nexaconnect-order-service` with the API audience for private Catalog/pricing reads.
   No staff token forwarding or extra guest client secret.
4. Deliberately enable Restaurant `QrEntry__Enabled=true`, BFF `QrGuest__Enabled=true`
   with exact `QrGuest__PublicOrigin`, Order `QrMenu__Enabled=true`, and BFF
   `QrGuest__MenuEnabled=true`. All checked-in flags default false. These are separate
   host settings; see [example](qr-entry.env.example). QR1 can run with MenuEnabled=false.
5. Staff creates/reissues an authorized table QR. Scan/open its trusted `/order#…` link.
   Check branch/table/THB, available/unavailable menu flags, quantities and Review totals.
   With RequestsEnabled=false, the page states no submission yet and payment at cashier. Do not send real
   entry links to external QR tools, support logs or analytics.

These steps describe isolated service configuration. The Phase 6 template/validator
deliberately retains false QR flags and rejects enabled overrides; it does not yet render
an enabled hosted QR rollout. Hosted enablement/target ingress proof remains QR6. Generated
business-service edges now explicitly hide `/guest/` and `/internal/`; only the portal
origin forwards guest browser routes. Re-render current edge artifacts for that protection.

Cart limits: 50 distinct products, 99/item; menu 200 items. Oversized/invalid menus fail
closed. Menu text/price/availability is Catalog-owned; no product images or Inventory
stock guarantee. Current THB, Restaurant tax/service policy and Order rounding apply.
Totals last at most two minutes and never outlive the visit. The cart exists only in page
memory: refresh/end/expiry clears it. Menu refresh preserves quantities so guests can
remove unavailable products, but clears totals. Connection failure retains the cart and
shows retry feedback. Returning to the visible page clears totals and requires review again.
A failed/expired visit removes the whole cart. No offline execution.

With QR3 disabled, no new tables, persistent guest cart, order, reservation, Kitchen ticket,
payment, audit or outbox message is created. QR3 retains reviewed quotes and original
submission identity; the original visitor cookie recovers requests after refresh. A rescan
creates a new visitor and cannot recover the previous request. QR4 revalidates
the current price/availability and obtains review of changes before authorized acceptance.

## Verification

Run from repository root:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~QrMenuTests|FullyQualifiedName~OrderPricing'
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter 'FullyQualifiedName~QrMenuBoundaryTests|FullyQualifiedName~QrMenuTransportTests|FullyQualifiedName~QrGuestBoundaryTests|FullyQualifiedName~QrGatewayTransportTests'
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
node --test docker/pilot/hosting.test.mjs
```

The current persistence launcher creates only a uniquely named local PostgreSQL fixture,
runs QR1 Restaurant admission, QR2 Catalog owner/branch/bounds, QR3 Order requests and
QR4 accepted Inventory/Kitchen recovery. It requires four non-skipped cases and exact cleanup, then writes a protected `.runstate/qr-entry/<id>/verification.json`.
It never targets retained/live databases. Narrow production grants remain a deployment
gate; the generated fixture applies owning migrations with its disposable administrator.

From `src/Frontend`: `npm run check`, `npm test`, `npm run build`,
`npm run audit:production`, `npm run audit:all`, then
`npx playwright test --config playwright.qr-entry.config.mjs` with installed Chromium/WebKit.
Browser tests mock HTTP and emulate Pixel/iPhone/desktop; they do not prove physical
camera scans, public TLS, real service topology or payment. CI is updated but not remotely
executed by this implementation.

Local execution on 23 October 2026:

| Verification | Result |
| --- | --- |
| Full backend unit regression (explicit current-user DPAPI test enabled) | 969 passed, none skipped |
| Architecture boundaries | 6 passed |
| QR entry/menu/BFF/owning/adapter HTTP plus adjacent gallery/Kitchen boundaries | 25 passed, none skipped |
| Real PostgreSQL Restaurant admission and Catalog owner/branch/bounds | 2 passed; cleanupVerified=true |
| Frontend TypeScript and Vitest | Passed; 76 tests passed |
| Both production portal builds; final Customer rebuild | Passed; existing large-bundle advisory remains |
| Production and full npm audit | 0 vulnerabilities each |
| QR browser matrix (11 cases × Chromium phone/WebKit phone/desktop) | 33 passed, no retries/skips |
| Pilot hosting/edge/configuration source checks | 47 passed |

Persistence receipt: `.runstate/qr-entry/5eab694a4e4949e69efb3bf2fbbdb7a9/verification.json`.
Browser cases include quantity-only edits without false price warnings, changed prices,
stale asynchronous replies, expiry, return-to-page invalidation, offline state, unavailable
products, 401/409/503 failures, refresh/end, keyboard and populated 320px/landscape layouts.
These are local source/fixture checks, not a joined hosted financial flow or actual phones.

Documentation updated with this slice: this runbook and environment example; API QR-Menu,
QR-Entry and Order-Pricing; identity QR-Guest-Entry; database QR-Entry; deployment guide/Phase 6;
ADR-045; Restaurant architecture; both canonical overview/architecture summaries; root,
Catalog, Order, Restaurant, Customer BFF, frontend, Customer Portal and pilot READMEs;
QR and next-iteration roadmaps. Prior QR1 verification remains historical. No schema
catalog/design inventory update is needed because QR2 adds no tables or migration.

## Operations and rollback

Use shared telemetry/correlation queries in the [contract](../API/QR-Menu.md). Inspect
bounded QR denial/dependency events and private owning-service health; never log or paste
capabilities, cookies, financial payloads or menu bodies. No guest request propagates staff
headers. Private Order capability routes must not be independently exposed at the public
cashier edge. Existing same-origin BFF edge forwarding supports the new route suffixes;
HTTPS/Host/cookie/origin checks remain mandatory. No CORS/forwarded-header trust was added.

To disable previews, turn BFF MenuEnabled and Order QrMenu off; QR1 visits may remain for
context. No QR2 data migration or rollback cleanup. Default-off deployment means source
completion does not enable public traffic. New binaries require a fresh coherent candidate
and clean-source/hosted/actual-device evidence; the old Phase8 artifact binding does not
certify QR2. Deferred Phase6–9 work and QR6 joined acceptance remain unchanged.
