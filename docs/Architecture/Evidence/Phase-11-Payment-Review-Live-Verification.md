# Phase 11 payment-review live verification

The operator UI increment adds five history/permission HTTP cases, so the current runner requires 13 passes. The historical eight-case sign-off below does not certify this expanded matrix or the new browser workflow. The full 13-case live run was not authorized in this implementation turn because the configured named Order/Reporting databases were not established as disposable; do not rerun against them until their intended targets and destructive-acceptance scope are explicitly confirmed. Synthetic browser coverage and isolated HTTP tests do not replace joined live OIDC/BFF/Order acceptance.

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
