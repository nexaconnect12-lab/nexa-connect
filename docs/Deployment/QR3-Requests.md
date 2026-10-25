# QR3 durable submissions and staff review

QR4 now implements staff acceptance/price review and one original unpaid Order handoff.
Current binaries require Order22; the shared [QR6 persistence launcher](QR6-Integration.md)
now requires five cases and generated PostgreSQL/RabbitMQ infrastructure. QR3 evidence below is historical and QR3-alone behavior is distinguished from
enabled QR4. See [current QR4 guide](QR4-Acceptance.md).


QR3 submission implements pending proposals. QR4 implements staff acceptance/Inventory/
Kitchen integration; [QR5](QR5-Collection.md) implements cashier collection/payment. Public hosted rollout, clean-source CI, actual
iOS/Android camera scans and retained Phase6–9 gates remain pending. No server was deployed.

## Isolated setup and usage

1. Follow [QR1](QR1-Entry.md) and [QR2](QR2-Menu.md) trusted HTTPS/private service setup.
   Configure actual owning Catalog/Restaurant menu/pricing and current Order workload.
2. Apply Order migrations20–22 under the migration identity, compatibility0.38.0, then verify
   [runtime grants](../Database/QR-Requests.md). This optional capability adds three tables;
   existing financial versions/permissions are unchanged. Use PostgreSQL; no in-memory
   durability fallback. Order refuses startup if QR3 enabled without PostgreSQL/QR menu.
3. Enable Order `QrRequests__Enabled=true` and BFF `QrGuest__RequestsEnabled=true` with
   existing menu/entry flags. All checked-in flags default false. Templates/validated pilot
   rendering continue to require disabled QR flags; these steps configure isolated hosts,
   not an enabled hosted pilot. Keep Order guest capability routes behind the BFF.
4. Open a table QR, review totals, then **Submit for staff review**. Exact reviewed quote
   UUID/fingerprint is retained before submission. Only one pending request per visit.
   A pending proposal is unpaid, unreserved and has not reached Kitchen.
5. **Check request status** or refresh with the same cookie to recover its request. Status
   also polls every15seconds while visible and focused under [QR8](QR8-Usability.md).
   On response loss, keep the visit open and **Retry original
   request**, or recover status; never generate a replacement submission identity. Cart
   inputs freeze while submission outcome is unknown. Reload can recover a committed
   request; an unsent cart remains page memory only.
6. Staff enters Customer Portal **QR requests**, selects an authorized branch, and reviews
   pending/rejected/expired requests. `order.read` discovers/reads; `order.place` rejects.
   Rejections use fixed categories and expected version/CSRF. Queue shows10 oldest pending
   proposals and explicit more indicator. QR8 refreshes this visible/focused queue every
   15 seconds; explicit reload remains available. Resolve requests to see more. With QR4 acceptance
   disabled, no acceptance button is offered; enabled QR4 adds price_review/accepted tabs
   and versioned acceptance.

Pending requests expire after15minutes or visitor expiry. Server database time applies
expiry on scoped access. Revocation/expiry denies guest submission/replay/status; it does
not silently cancel requests. Ending/rescanning creates no cancellation and a new scan
cannot recover the previous visitor's work. The page warns to consult staff. Reviewed
prices are stored exactly; QR4 revalidates and obtains consent for changes before
acceptance. No price, stock, payment or Kitchen execution is promised by a pending request.

Caps/retention and errors are specified in [API](../API/QR-Requests.md) and
[database policy](../Database/QR-Requests.md). Quotas persist across process restart;
transport counters remain per-process. Cleanup is access-driven; stale inactive branches
can retain eligible data until next access. Durable audit remains immutable operational
history. Capacity, backup/restore and public-internet abuse verification are future gates.

## Verification and operations

Local execution on23October2026:

| Check | Result |
| --- | --- |
| Full backend unit regression, current-user DPAPI fixture explicitly enabled | 979 passed, none skipped |
| QR guest/staff/owning HTTP and transport plus adjacent gallery/Kitchen boundaries | 27 passed, none skipped |
| Architecture boundaries | 6 passed |
| Owning PostgreSQL QR1/QR2/QR3 cases | 3 passed; exact cleanup verified |
| Frontend TypeScript and unit regression | Passed;76 tests passed |
| Full QR browser matrix,15 cases ×3 projects | 45 passed, no retries/skips |
| Both production portal builds | Passed; existing large-bundle advisory remains |
| Full npm dependency audit | 0 vulnerabilities (includes production dependencies) |
| Pilot configuration/edge source tests | 47 passed |

Final persistence receipt:
`.runstate/qr-entry/50e4641079a146199c434ea39da697ca/verification.json`.
The PostgreSQL request case also verifies empty migration20 downgrade/reapply, competing
staff rejection, ten concurrent table admissions plus overflow, and retention preserving
immutable audit. Earlier QR3 receipts precede these extra checks and are historical.
Local browser fixtures caught and corrected double JSON serialization in the staff
rejection call; final tests assert the actual request body/version/CSRF contract.

Documentation updated: ADR046; API QR-Requests/QR-Menu/QR-Entry/Operational-Branches;
database QR-Requests/QR-Entry/Database-Design/Database-Guidelines; identity QR-Guest-Entry;
this runbook, QR2 runbook, deployment guide and QR environment example; QR and next-iteration
roadmaps; root, Order, Restaurant, Customer BFF, frontend, Customer Portal and migration
READMEs. Project overview and both project/Restaurant architecture documents were updated
for ownership, current capabilities and deferred acceptance. ADR045 records its QR2-alone
scope and links the QR3 retention decision. ADR005 remains applicable without a change.

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~QrRequest|FullyQualifiedName~QrMenu|FullyQualifiedName~OrderPricing|FullyQualifiedName~MigrationRunnerTests'
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter 'FullyQualifiedName~QrMenuBoundaryTests|FullyQualifiedName~QrGuestBoundaryTests|FullyQualifiedName~QrGatewayTransportTests'
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
```

The current launcher creates uniquely named loopback PostgreSQL/RabbitMQ infrastructure,
applies owning Restaurant/Catalog/Order/Inventory/Kitchen/POS/Reporting scripts to generated
schemas, requires five non-skipped cases and exact cleanup, then retains private logs/TRX
and `.runstate/qr-entry/<id>/verification.json`. Prepare both cached fixture images; see
[QR6 prerequisites and joined scope](QR6-Integration.md).
No retained/live database is reset. The QR3-only owning Order case exercises concurrent retries,
fresh repository reads, guessed visitor/owner denial, pending limits, expired quote replay,
expiry, rejection/version checks, immutable audit, outbox rollback, quota contention and
no executable Orders or non-audit integration events.

Frontend: `npm run check`, `npm test`, `npm run build`, both dependency audits, then
`npx playwright test --config playwright.qr-entry.config.mjs`. The QR suite includes
mocked response-loss/retry/reload/status recovery and CSRF/versioned staff rejection on
desktop Chromium and emulated Android Chromium/iPhone WebKit. These are source fixtures,
not real phones, actual public TLS or the joined QR4/QR5 financial workflow.

Shared JSON/OTLP and validated correlation remain on owning services/BFF. Query QR denial/
dependency events by service and CorrelationId; never expose quotes, visitor capabilities,
cookies, actors or financial bodies in diagnostics. Outbox consumers must preserve generic
audit separation; QR3 proposal transitions produce no sale/workflow event. QR4 acceptance
additionally commits the original OrderSubmittedV1 event. New candidate/artifact/schema bindings
are required before release; prior Phase8/QR2 receipts are historical, not QR3 acceptance.

Disable BFF RequestsEnabled and Order QrRequests to stop QR3 while retaining QR1/QR2.
Retained requests remain in owning storage. Disabling Order QrRequests also disables staff
queue/rejection and access-driven expiry through those routes; resolve pending work or
record an operational recovery plan before disabling. There is no guest cancellation or automatic staff acceptance.
Do not downgrade after request/audit history. No commit/push/deployment is implied.
