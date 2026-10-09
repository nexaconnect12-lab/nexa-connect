# Joined cashier-to-day-close acceptance

This opt-in gate joins real Keycloak authorization-code/PKCE cashier authentication, manager Customer BFF sessions, Catalog, Inventory, Kitchen, Order, Payment, POS, Reporting, Platform Directory and Authorization. It exercises online manual cash checkout through day-close preparation. It supplies no production provider, physical WPF/printing, offline workflow, or settlement approval/cutoff certification.

## Run

Requires PowerShell 7, .NET 10, Node 22.12+ on 22.x or 24+, installed Playwright Chromium and a local Docker socket. Restore the solution, then run from repository root:

```powershell
dotnet restore NexaConnect.sln
Push-Location src/Frontend
npm ci --ignore-scripts
npm run audit:production
npm run audit:all
npm run test:cashier-day-close:guards
npx playwright install chromium
Pop-Location
./scripts/test-cashier-day-close.ps1 -ConfirmDisposableInfrastructure
```

`-NoBuild` requires current Debug binaries and restored dependencies; the launcher still publishes the BFF/frontend and verifies Authorization persistence. Direct browser invocation requires the complete generated launcher environment. Missing settings fail rather than skip. The gate requires five distinct browser passes, all six Authorization persistence cases, and exact cleanup; retries and repeated cases cannot satisfy acceptance.

## Command and evidence boundaries

Reference provisioning uses owning Platform Directory/Restaurant/Authorization Application services for organizations, enabled product access, memberships, branches and roles. The fixture creates one empty POS store as deployment reference data. Terminal enrollment, menu creation and stock setup then use actual authorized APIs. No order, shift, drawer, movement, receipt, settlement, cash review, day-close record or Reporting fact is inserted by the fixture.

Three separate subjects have branch-scoped cashier or restaurant-scoped manager assignments. The first manager additionally receives an explicit branch-scoped `pos.terminal.enroll` reference grant; enrollment is not a default manager permission, and cashier enrollment remains denied. The disposable realm's existing public POS client receives one exact generated loopback callback; password grants remain disabled and S256 PKCE remains required. A run-owned HTTP callback listener receives the authorization code and closes after each sign-in. Code exchange retains access tokens only in the test process's memory. Portal requests continue to use BFF cookies and CSRF. The callback change is confined to the disposable realm and default role grants are unchanged. The shared realm template additionally supplies the required explicit Inventory workload API audience. Persisted realms need a reviewed mapper update and a fresh Inventory token; restarting Keycloak does not update an existing realm.

The test-only fixture executable hosts the actual Order and POS entry points with WebApplicationFactory/Kestrel, real JWT validation, real service adapters, PostgreSQL persistence and normal observability. Its clock is restricted to an exact run-owned `clock.json`. During commands it reports yesterday at 10:00 in the configured Asia/Bangkok day, then returns to the system clock before preparation. Order persistence and cash-session opening/closing accept injected TimeProvider values; normal deployments use the system clock and expose no clock option. The test never rewrites persisted timestamps or immutable evidence. Other service, database, broker and identity clocks remain real. This is deterministic historical command acceptance, not a real midnight/clock-skew rehearsal.

Actual Order outbox workers publish both receipt-backed sales and manual-tender events. POS consumes the latter, producing one Order-linked drawer sale; Reporting consumes sale/payment facts. The runner requires the actual three consumer bindings before browser execution. The existing privileged financial recovery CLI records an exact-window observation after publication; this is an explicit acceptance operator action, never a portal read side effect. Refund inventory is empty in this cash-only slice.

| Required scenario | Evidence |
| --- | --- |
| Cashier opens shift/cash and places order | Anonymous, branch and tenant denial; actual quote; Inventory/Kitchen workflow; placement replay |
| Manual settlement reaches POS/Reporting | Matching replay and changed-payload conflict; immutable receipt; one attributed movement and recorded complete inventory |
| Cash closes and variance is reviewed | Stale close rejection; cashier-owned closure; shift closure; manager-only review; stale/version/payload and tenant boundaries |
| Actual day becomes ready for review | Pending review blocks first preparation; owner totals/fingerprints; operation replay/conflict; two-manager race |
| POS restarts | Unavailable read, fresh Ready observation, unchanged saved evidence/version, settlement/receipt replay and durable private proof |

The read-only private proof requires one Paid receipt-backed order, settlement, closed drawer, sale movement, approved review, POS projection, Reporting sale and payment. These are singleton count assertions within the run-owned scopes, complemented by the browser's command/replay/amount checks. It validates real granted Authorization decision IDs for shift open/close, settlement, review and preparation without exporting actor identities or financial bodies. These independently owned reads are not a distributed transaction or completeness watermark.

## Operations and limits

The launcher supplies Order's existing outbound adapter settings (`Authentication:TokenEndpoint`, `ClientId`, and secret-managed `ClientSecret`) as well as its `WorkloadIdentity` settings; the read-only gate did not exercise all checkout adapters. Cashier-state command dispatch permits the recording operation and private proof, and rejects the older synthetic delivery/late-cash commands. The final scenario verifies these rejections before reading durable proof.

The dedicated [Docker topology](../../docker/cashier-day-close/README.md) has ten independently owned application databases plus Keycloak's database; all published ports are IPv4 loopback. Random run secrets, separate database-scoped runtime identities and exact retained child handles are used. Disposable runtime grants include broad owned-table CRUD and do not certify production least privilege. The existing bounded file-control channel permits only stop/start of the owned POS process, without PID/executable input. The launcher removes its exact Compose project/volumes and certificate, restores environment, and fails if cleanup cannot be verified.

Shared JSON/correlation telemetry remains on the actual services, including the clock-injected hosts. Query `{service_name="nexaconnect-order"} |= "Order"`, `{service_name="nexaconnect-pos"} |= "POS"` or `Day-close`; narrow by `CorrelationId`. Direct bearer API commands use `cashier-<run-id>` correlation. Browser-session BFF fetches use the BFF's normal generated/validated correlation; this gate does not assign them the direct-command identifier. Never upload local service logs, tokens, cookies or financial bodies. CI uploads only `.runstate/cashier-day-close/<run-id>/verification.json` and `src/Frontend/test-results/cashier-day-close-live/<run-id>/summary.json`, retained for 14 days.

See [execution evidence](../Architecture/Evidence/Cashier-Day-Close-Acceptance.md). Remote CI, branch protection and target-production acceptance remain separate. This gate does not upgrade Ready-for-review to settlement approval or a financial write fence.

Positive preparation uses the actual Portal Load preparation, Prepare day close and Refresh preparation controls, capturing the command sent by the portal. Negative, replay and two-manager contention checks additionally use browser-session JavaScript fetches to the actual BFF. Restart replay uses the winning actor's original command. Cashier commands use bearer requests from the test process, so no bearer is passed into portal JavaScript.
