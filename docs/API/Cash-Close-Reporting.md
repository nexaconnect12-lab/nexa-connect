# Single-store cash-close Reporting

The Customer Portal offers read-only, eventually consistent snapshots of closed POS cash sessions for one store. POS remains authoritative for cash, late settlements and supervisor decisions. Reporting owns a separate projection; it never reads the POS database. This development slice does not certify end-of-day settlement, completeness, or a synchronized store. [ADR-012](../Architecture/Decisions/ADR-012-cash-close-snapshot-reporting.md) records the snapshot and authorization boundaries.

## Publication and projection

With `CashClosePublication__Enabled=true`, POS scans at most 100 eligible closed sessions per batch, waits 15 seconds between batches, and cycles back through the IDs. It backfills sessions without a publication checkpoint and revisits sessions whose financial or review version advanced. These timings are polling intervals, not a delivery SLA. Restaurant resolves the authoritative organization/restaurant/branch before POS opens a transaction. An ownership mismatch prevents publication.

POS migration 6 adds `cash_close_publications` and the closed-session scan index. The publisher locks the cash-session row, reads current movements/review state, and commits one full `pos.cash-close.snapshot.v1` outbox event together with its financial/review checkpoint and monotonically increasing per-session snapshot version. Expected cash is opening cash plus sales, pay-ins and float adjustments minus refunds and pay-outs; variance is counted minus expected. Cashier close and review transactions are unchanged. Several changes between scans can coalesce into one snapshot; this is not a decision-history event stream.

`PosCashCloseSnapshotV1` contains event/correlation IDs, capture time (`OccurredAtUtc`), organization/restaurant/branch/store/session/shift IDs, closure time, currency, expected/counted/variance amounts, snapshot/financial/review versions and review status. It excludes cashier/reviewer subjects, reasons, movement detail and credentials. This is restricted financial integration data, not safe activity-audit data: restrict broker/database access and never log the payload or amounts. Retain original event IDs and payloads for controlled replay.

Review status is `balanced` for zero variance; otherwise it is `review_required` unless the review covers the current financial version, in which case it is `investigating` or `approved`. A late financial movement can supersede approval. A snapshot describes the state when captured, not necessarily the state when read.

Reporting migration 15 adds `cash_close_facts` and `cash_close_event_receipts`. Its separate optional consumer binds `pos.cash-close.snapshot.v1`; it does not feed the generic activity projection. Receipt hash and fact replacement commit in one transaction before acknowledgement. The hash covers the translated snapshot. Identical event replay is a no-op; conflicting identity/payload reuse, ownership changes, conflicting equal snapshot versions, or newer snapshots with regressing source versions are rejected. Older snapshots are receipted without replacing newer facts. Reporting records its own `projectedAtUtc`; source `OccurredAtUtc` becomes `capturedAtUtc`.

The consumer uses durable main/dead-letter queues and prefetch 16. Invalid envelopes (including bodies over 32 KiB), malformed data and domain conflicts are dead-lettered. Database/transport failures are requeued after a short delay; connection failures retry after five seconds. There is no automatic dead-letter replay or replay CLI. Monitor delivery and rejection logs and inspect sensitive payloads only through restricted operational procedures.

## Read API and authorization

| Surface | GET route |
| --- | --- |
| Reporting | `/api/reporting/v1/customer/organizations/{organizationId}/reports/cash-close` |
| Customer BFF | `/bff/customer/reports/cash-close` |

Both accept required `branchId`, `storeId`, `fromUtc`, `toUtc`, optional `limit` (1–100, default 50), and `cursor`. The range is UTC inclusive/exclusive `[fromUtc,toUtc)`, must be nonempty and span at most 31 days. Rows sort by closure time then session UUID, newest first. Return `nextCursor` unchanged with the same filters; it grants no access and pagination is not a snapshot across requests.

The response is `{ items: [{ snapshot, projectedAtUtc }], nextCursor }`. Each `snapshot` has `organizationId`, `restaurantId`, `branchId`, `storeId`, `sessionId`, `shiftId`, `closedAtUtc`, `currency`, `expectedAmount`, `countedAmount`, `varianceAmount`, `snapshotVersion`, `financialVersion`, `reviewVersion`, `reviewStatus`, and `capturedAtUtc`. No range totals, cross-currency aggregation, completeness watermark or current-state guarantee is provided. Empty rows do not prove that no sessions closed.

Reporting requires a bearer token with subject, matching `X-Nexa-Organization-Id` and `X-Nexa-Application-Code: nexa_connect`. Before every read it forwards the bearer token to POS `GET /api/pos/v1/cash-reviews/access` for the exact organization/branch/store. POS re-resolves Restaurant hierarchy, checks its store and evaluates `pos.cash-review.read`; Reporting does not grant access from cached facts or `reporting.sales.read`. Existing Authorization migration-7 grants apply, including accountant read access; no new permission, role, or workload bypass is introduced.

The BFF binds the protected tenant cookie to the session subject, revalidates current organization/product membership through Platform Directory, and derives the downstream organization itself. It forwards safe fixed-route parameters and suppresses downstream error bodies. This surface has no mutations or CSRF-token endpoint. Both APIs send no-store responses. Missing authentication/context returns `401` at the BFF; Reporting's missing/mismatched tenant context returns `403`. Denied permission/scope returns `403`, malformed filters `400`, and recognized database/transport/timeout/JSON failures `503`. POS access `401`/`403`/`404` fail closed as Reporting `403`; other non-success POS responses become `503`. A non-success BFF Directory access response fails closed as `403`.

The portal asks for administrator-supplied branch/store UUIDs and UTC dates, defaults to the preceding seven days and loads 50 rows at a time. It displays per-row currency, values, versions and capture/projection times, with no decision controls. Reload is explicit; there is no automatic polling. Changing filters clears rows, tenant changes remount the panel, and leaving it aborts reads. Failed reads clear the old report. Browser timeout is 25 seconds, BFF Reporting timeout 20 seconds, and Reporting POS-access timeout 15 seconds. Use POS Cash Review for authoritative details and decisions.

## Deployment, rollback and diagnostics

1. Keep POS `CashClosePublication__Enabled=false`, POS `Outbox__Enabled=false`, and Reporting `CashCloseConsumer__Enabled=false` while applying POS migrations through 6 and Reporting through 15 with service-owned migration credentials. Both new migrations require application version `0.15.0`. Existing Authorization 7 must be applied for cash-review grants.
2. Configure POS `ConnectionStrings__POS`, `Services__Restaurant`, existing POS workload credentials and shared `Outbox__ConnectionString`/`Outbox__Exchange`. Configure Reporting `ConnectionStrings__Reporting`, `Services__POS`, `CashCloseConsumer__ConnectionString`, `CashCloseConsumer__Exchange` (default `nexaconnect.events`), and `CashCloseConsumer__Queue` (default `nexaconnect.reporting.cash-close.v1`). Inject credentials from the deployment secret store.
3. Start the Reporting consumer and verify its `Cash-close consumer ready` log and actual broker binding before enabling POS outbox dispatch. Queue existence or HTTP liveness alone does not prove binding readiness. The dead-letter queue defaults to `nexaconnect.reporting.cash-close.v1.dead`.
4. Enable POS publication and then outbox dispatch; publication may collect durable pending events while dispatch is disabled. Configure BFF `Services__Reporting` and verify exact-store access through the portal. Publication and consumption are independently disabled by default. Existing development launchers do not enable this pipeline automatically. Reporting's Development POS address is `https://localhost:7120/`; override it when using the checkout launcher's local HTTP POS host (`http://localhost:5225/`, Development only).

Roll back readers before removing Reporting schema, stop publication/dispatch and the consumer as appropriate, and retain source outbox events. POS `6→5` refuses once any publication checkpoint or cash-close outbox event exists; use forward recovery to preserve snapshot versions. Reporting `15→14` deletes facts and receipts. Re-upgrade alone does not rebuild them: the POS checkpoint suppresses unchanged re-publication. Rebuilding requires controlled replay of retained original source events after bindings are ready; implementing and rehearsing that operational replay remains follow-up work. Do not delete POS checkpoints or fabricate new versions to force a rebuild.

Structured JSON/optional OTLP use `nexaconnect-pos`, `nexaconnect-reporting`, and `nexaconnect-customer-bff`. Query `{service_name="nexaconnect-pos"} |= "cash-close"`, `{service_name="nexaconnect-reporting"} |= "Cash-close"`, or `{service_name="nexaconnect-customer-bff"} |= "Cash-close"`. Publication creates a correlation UUID, propagates it to Restaurant and the event, and the consumer validates it before using it in telemetry. HTTP correlation flows BFF → Reporting → POS. Operational logs contain bounded outcomes, never financial values or event bodies. There are no dedicated backlog alerts or completeness metrics in this slice.

## Verification and release gates

From the repository root:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --filter "FullyQualifiedName~CashClose|FullyQualifiedName~MigrationRunner"
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~CashClose
```

From `src/Frontend`, run `npm run check`, `npm test`, `npm run test:e2e:cash-close`, and `npm run build --workspace @nexaconnect/customer-portal`. Browser cases use synthetic BFF responses; HTTP cases substitute authentication and ports. They do not establish live identity or broker acceptance.

`CashCloseProjectionPostgresTests` requires secret-injected `NEXACONNECT_POS_INTEGRATION_DB`, `NEXACONNECT_REPORTING_INTEGRATION_DB`, and `NEXACONNECT_ENVIRONMENT=Testing` against verified disposable databases. It creates/drops generated schemas and exercises real repositories, late-settlement review invalidation, checkpoint/outbox rollback, duplicate/stale projection, scope filtering and SQL downgrade/re-upgrade replay. This is not full migration-runner or hosted-broker acceptance. Without these settings it skips; skipped tests are not evidence.

Live broker pipeline acceptance is not implemented or executed for this slice. Before release, retain joined evidence for existing-session backfill, close/approve/late-settlement propagation, concurrent publishers, duplicate/out-of-order delivery, process and broker interruption, transactional rollback, permanent rejection/dead-letter handling, exact-store revoked/foreign access, and rebuild from retained source events. Export, multi-store summaries, complete review history, business-day settlement totals, retention policy/alerts and offline Reporting remain separate work.
