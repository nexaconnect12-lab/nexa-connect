# Phase 11 payment-review live verification

The operator UI increment adds five history/permission HTTP cases, so the current runner requires 13 passes. The original UI turn could not authorize the named application databases as disposable. The follow-up isolated launcher closes that local matrix gate without touching those databases. Historical eight-case evidence remains separate, and neither matrix certifies joined live OIDC/BFF/Order acceptance.

## Isolated matrix sign-off — 2026-08-31

The joined live slice passed 7/7 locally on 2026-09-02 in generated project `nexa-review-joined-ea3d34c2c4354a30b6102be1a7890975`. Its sanitized summary records every migration, identity, fixture, application, proxy, process-cleanup, Compose-cleanup, and live-browser flag as true. The run used PostgreSQL, Keycloak, RabbitMQ, Toxiproxy, and seven actual application hosts; applied Platform Directory 3, Restaurant 3, Authorization 5, and Order 4; authenticated distinct reader/resolver sessions; and exercised concurrency, resume, void, Inventory transport outage/recovery, and lost-response behavior. The generated environment was destroyed after evidence capture. This closes the local joined-orchestration gate, not the production-environment gates below.

The consumer-readiness hardening was reverified on 2026-09-01: generated project `nexa-review-it-9c895c76a3a4423b8cebfdc35dad97ce` passed 13/13 with no skips, and its sanitized summary records `matrixPassed=true`, `cleanupPassed=true`, and `liveBrowserVerified=false`. Retained evidence is under `.runstate/payment-review-isolated/9c895c76a3a4423b8cebfdc35dad97ce/`. The Reporting hosted acceptance now waits until exchange, queue, audit/dead-letter bindings, QoS, and consumer registration all succeed before mandatory publication. This closes the test startup race in which observing a declared queue could precede its binding and RabbitMQ could reject a mandatory audit publication with `312 NO_ROUTE`.

`scripts/test-payment-review-isolated.ps1 -ConfirmDisposableInfrastructure` built the current integration project and passed 13/13 with zero skips in generated project `nexa-review-it-d87a152cccaf42d69e3802de09f0ede2`. This used a new PostgreSQL 17 cluster, RabbitMQ 4, and run-scoped Alertmanager/receiver with dynamically assigned loopback ports. Synthetic firing/resolved delivery passed; containers/network were removed and cleanup was verified. Existing application databases, root Compose resources and stored credentials were not used.

Retained local evidence:

- `.runstate/payment-review-isolated/d87a152cccaf42d69e3802de09f0ede2/operations/payment-review-live-verification.trx`
- `.runstate/payment-review-isolated/d87a152cccaf42d69e3802de09f0ede2/summary.json` (`matrixPassed=true`, `cleanupPassed=true`, `liveBrowserVerified=false`).

The live harness collects seven scenarios and includes run-scoped Order-to-Inventory proxy disable/restoration with a fifth fresh fixture. The final local run started every required application target and proxy route and passed Playwright without skips or retries. See the [live browser prerequisites](../../../src/Frontend/e2e/payment-review-live/README.md) and [release checklist](../../Deployment/Payment-Review-Release-Checklist.md). Inventory process loss, Kitchen/combined dependency-outage, provider/accounting, production paging, and live-traffic rollback evidence remain open.

## Scope

### Operator UI increment: isolated database evidence

On 2026-08-31, the generated-database-only Order migration/history acceptance passed 1/1 with no skips. Retained TRX: `tests/Integration/NexaConnect.IntegrationTests/TestResults/payment-review-ui-generated-db.trx` (local ignored test output). It verifies Order `0→4→3→4`, fenced resolution, committed history fields, and organization/order filtering using a validated generated `nexaconnect_order_clean_it_<guid>` database; that database was cleaned up afterward. Named application databases were not mutated by this run. This result does not constitute the full 13-case broker/Reporting/alert matrix or joined live operator acceptance.

### Historical eight-case sign-off

Local sign-off on 2026-08-31: the expanded matrix passed 8/8 with no skips against local disposable PostgreSQL/RabbitMQ resources, and isolated alert firing/resolved delivery and container cleanup completed. Final retained TRX (including the audit-requested empty dead-letter assertion): `.runstate/payment-review-operations/7a1f2d0631f341ecbeba8c8f03477bdf/payment-review-live-verification.trx`.

Four HTTP tests use the real Order HTTP pipeline/application service with controlled authentication, authorization, and repository doubles: cross-tenant read/mutation 404, denied mutation 403, stale version 409, and server-derived actor/Authorization decision attribution despite spoofed request fields. They do not certify live Keycloak or Authorization policy configuration. Separate live PostgreSQL assertions race two claims, permit one winner, reject a different-resolution expired takeover, fence old claim finalization/release, reject duplicate finalization, and retain exactly one attributed history row.

The Reporting duplicate test uses prefetch 1 and publishes a distinct marker after the original and duplicate on the same connection. Marker completion plus an empty dead-letter queue distinguish duplicate acknowledgement from rejection. The original event retains one projection and one completed inbox attempt. This replaces the weaker assertion that checked only for one projection before the duplicate necessarily completed.

The checked-in acceptance matrix provides local Payment Review verification without broadening the financial workflow. It covers Order migration `0→4→3→4`, transactional case resolution and downgrade refusal, Reporting migration-13 projection removal/replay, persisted Order outbox retry followed by confirmed persistent RabbitMQ publication over a new connection, and isolated Alertmanager firing/resolved delivery, alongside the HTTP and fencing assertions described above.

Run it only against disposable resources:

```powershell
.\scripts\test-payment-review-operations.ps1 -EvidenceLabel staging-disposable -ConfirmDisposableInfrastructure -ConfirmAlertDelivery -ConfirmDestructiveRollback
```

Inject `NEXACONNECT_ORDER_INTEGRATION_DB`, `NEXACONNECT_REPORTING_INTEGRATION_DB`, `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB`, and `NEXACONNECT_RABBITMQ_INTEGRATION_URI` through the process environment or secret mechanism. The script rejects missing or production-looking values, creates only generated acceptance resources, deletes its isolated broker resources and alert containers, restores opt-in environment variables, and retains a TRX under `.runstate/payment-review-operations/<run-id>/`. `EvidenceLabel` is descriptive metadata only; it does not select or validate infrastructure, so operators must independently record the target identity.

## Evidence boundary

The expanded RabbitMQ case creates the review-required case and resolution through `PostgresOrderRepository`, validates deserialized required/resolved/audit contracts including correlation and Authorization decision identity, records a hosted dispatcher transport failure, restarts the worker with the production RabbitMQ transport, and requires persistent confirmed publication plus database publication timestamps. A separate hosted Reporting consumer case accepts `order.audit.v1`, projects the safe audit, and suppresses duplicate delivery through its durable inbox. This proves worker restart, not a full broker-container restart. Reporting migration replay uses the original event identity after removing the incompatible projection and inbox marker. The alert exercise proves the configured Alertmanager route can deliver both states to the isolated receiver.

This does not certify production receiver authentication, escalation, paging, acknowledgement, concrete SLOs, or the operator UI. Those remain environment/product-owned release evidence. A successful environment run must record its date, infrastructure identity (without secrets), TRX path, and production alert-routing evidence in the release record.
