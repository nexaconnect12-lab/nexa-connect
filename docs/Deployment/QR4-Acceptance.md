# QR4 staff acceptance into Inventory and Kitchen

QR4 source implements staff admission and price-change consent before existing durable
Order recovery. [QR5](QR5-Collection.md) implements cashier lookup/collection/payment. No server deployment, actual
phone test, hosted broker certification, clean-source CI or release freeze is claimed.
Deferred Phase6–9 specifications and acceptance gates remain unchanged.

## Isolated enablement and use

1. Follow [QR3 setup](QR3-Requests.md), preserving trusted HTTPS origin, separate cookies,
   private services and existing exact Order workload identity/API audience.
2. Apply through Order migration22 before current binaries; QR4 introduced migration21. Verify
   [grants, retention and downgrade limits](../Database/QR-Acceptance.md). Existing
   Restaurant4/Catalog4/Inventory5/Kitchen3 and current Order financial schemas apply.
   No new identity or permission grant is needed. Confirm actual owning schema catalogs.
3. Order requires QrRequests enabled, PostgreSQL, `Workflow__UseHttpAdapters=true` and
   `WorkflowRecovery__Enabled=true`. Existing direct HTTPS Inventory/Kitchen/Catalog/
   Restaurant addresses, workload credentials and outbox/consumer topology must be ready.
   Set `QrAcceptance__Enabled=true` and BFF `QrGuest__AcceptanceEnabled=true` deliberately;
   both defaultfalse. Pilot templates still reject enabled QR flags; this configures
   isolated hosts, not an enabled rendered/hosted pilot. Direct guest routes stay hidden.
4. Staff opens **QR requests**, selects pending and clicks **Accept original request**.
   Current branch/visitor/entry, dine-in policy, availability and prices are checked.
   Missing/revoked/ended visitor, disabled dine-in or unavailable product blocks admission.
5. A changed price/policy moves it to **price_review**. Original guest sees the full updated
   line/tax/service/total breakdown and explicitly confirms. Staff reloads pending and
   accepts that new version. If the two-minute review expires, staff selects price_review
   and uses **Refresh guest price review** on the original request. This only refreshes guest
   review; it never bypasses consent. Original request identity/deadline remains unchanged.
6. Accepted tab shows the original Order ID/state. Existing recovery advances Submitted
   to InventoryReserved then KitchenAccepted. It stops unpaid at manual tender. Wait for
   actual status; accepted alone is not proof of Kitchen completion. Collect via [QR5](QR5-Collection.md);
   do not recreate it in POS as a fresh order.

On timeout, reload the original request; a successful acceptance retry returns its same
Order. Do not create replacement requests/Orders. Recovery retains original scope,
commercial snapshot, manual tender and idempotency identity, even after session expiry.
Inventory/Kitchen response loss retries their owning idempotent operations. Definite
stock exhaustion rejects the original Order and creates no Kitchen ticket. Existing
authorized cancellation remains separate; no guest cancellation or automatic payment.

Admission is one transaction: Order/lines, request version, immutable provenance and
audit/outbox. A crash cannot leave a QR acceptance without its original Order. Existing
financial day fences can block the transaction/recovery and must be resolved through
their owning operational workflow; acceptance does not bypass day-close controls.
No immediate broker availability is required for committing the outbox; actual hosted
broker-loss/restart/delivery testing remains QR6. Pending work in SQL is not a delivered
ticket or projected financial report.

## Verification

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~Qr|FullyQualifiedName~OrderPricing|FullyQualifiedName~MigrationRunnerTests'
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter 'FullyQualifiedName~QrMenuBoundaryTests|FullyQualifiedName~QrGuestBoundaryTests|FullyQualifiedName~QrRestaurantBoundaryTests|FullyQualifiedName~QrMenuTransportTests|FullyQualifiedName~QrGatewayTransportTests'
./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure
```

The current [QR6 PostgreSQL/RabbitMQ runner](QR6-Integration.md) requires five non-skipped
cases and cleanup of both generated containers. The comprehensive QR4/QR5 case applies
full Order/Inventory/Kitchen/POS/Reporting migrations to separate generated schemas, using real
owning repositories/recovery and synthetic scoped Catalog/Restaurant/Authorization ports.
It tests exact changed-quote review/consent, live permission/dine-in denial,12 competing
accepts, one original Order, immutable provenance, rollback after outbox failure, restart
reads, rejected proposals and committed Inventory/Kitchen response loss. Its recovery
clock advances one minute only to cross the existing initial retry delay; it is not a
real hosted clock/outage/device observation. No retained/live database is targeted.

Frontend gates and `npx playwright test --config playwright.qr-entry.config.mjs` cover
staff acceptance/version/CSRF and guest changed-price review in Chromium/WebKit phone
emulation plus desktop. Mocked browser HTTP is separate from actual mobile-camera/TLS
and QR5 cashier/receipt acceptance. See [API](../API/QR-Acceptance.md) for correlation-safe
service queries and limits. Production bundle size advisory remains a separate UI concern.

Disabling new acceptance stops new accepts/guest confirmations but leaves existing Order
recovery running. Do not stop recovery merely to hide UI; committed commands must finish
or enter owning authorized recovery/cancellation. Accepted provenance has no automatic
archive/deletion path. New binaries/schema/config require a fresh candidate binding and
later target evidence; earlier QR3/Phase8 receipts remain historical.

## Local verification record — 2026-10-23

The current working-tree implementation passed these local gates:

| Gate | Result |
| --- | --- |
| Full .NET unit suite, with current-user DPAPI enabled | 983 passed, none skipped |
| QR HTTP/transport and adjacent Gallery/Kitchen integration selection | 30 passed, none skipped |
| Architecture tests | 6 passed |
| Disposable PostgreSQL QR persistence runner | 4 passed; cleanup verified |
| Frontend type check and unit tests | Passed; 76 tests passed |
| QR browser matrix | 51 passed |
| Pilot hosting/configuration source checks | 47 passed |
| Order and Customer BFF builds | Passed, zero warnings/errors |
| Both production portal builds | Passed; existing bundle-size advisory remains |
| Full npm dependency audit | Zero vulnerabilities |

The final disposable persistence receipt is
`.runstate/qr-entry/357a93ada0a7460aaa15a9bf89913da9/verification.json`.
It records four cases, `passed=true`, `cleanupVerified=true` and
`productionVerified=false`. The acceptance case also verifies real stock exhaustion
closes the original Order without creating another Kitchen ticket. These are local
source/persistence checks with the synthetic ports described above; they do not certify
a hosted broker outage, physical phones, deployment, clean-source CI or a release.

Documentation was updated across QR API/database/identity/runbooks, the QR and next-iteration
roadmaps, ADR047 and related historical QR documents, affected component READMEs, root
README, database catalog and deployment configuration. Both
[project overview](../../AI/architecture/project_overview.md) and
[canonical architecture](../Architecture/Project-Architecture.md) changed to describe
the atomic acceptance, consent and recovery flow; Restaurant architecture was also aligned.
The documentation-maintainer audit checked the implementation against these documents.
