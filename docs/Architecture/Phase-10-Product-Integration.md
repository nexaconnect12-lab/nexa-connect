# Phase 10 — Product service integration

Phase 10 is delivered as product-owned vertical slices. A service completes the phase only when its own Application ports, tenant/resource authorization, versioned API/BFF boundary, integration contracts, idempotency boundary, transactional publication, PostgreSQL persistence, reversible migration tests, and safe audit events are implemented where its workflows require them. “Not applicable” must be justified by the service's role; it is not permission to share another service's domain model or database.

Shared projects may contain immutable integration-event DTOs, tenant header names, permission-code constants, and low-level messaging infrastructure. They must not contain product aggregates, entities, repositories, or database models.

## Current integration matrix

| Product service | Current Phase 10 foundation | Remaining exit work |
| --- | --- | --- |
| Catalog | Product-owned menu interfaces and PostgreSQL repository; tenant/branch authorization; API and Customer BFF contract; `CatalogMenuItemChangedV1`; append-only product audit; transactional outbox; paired migration 4 scripts; opt-in live PostgreSQL atomicity, rollback, trigger, outbox-store retry-state, and downgrade/re-upgrade coverage | Capture RabbitMQ outage/recovery acceptance evidence before closing the slice; retain full Catalog clean-install as a production migration release gate |
| Customer | Product-owned profile interface/repository; organization authorization and API contract; migration baseline | Add profile lifecycle integration/audit events and transactional outbox |
| Inventory | Product-owned stock/reservation interfaces; branch authorization; PostgreSQL repository; durable inbox for consumed workflow messages | Complete transactional stock/reservation publication and product audit coverage |
| Kitchen | Product-owned ticket interface/store; authenticated workload API; PostgreSQL repository and durable inbox | Add product audit/publication for ticket transitions and tenant-aware operator contracts when exposed beyond trusted workloads |
| Media | Product-owned repositories/workers; tenant authorization; Customer BFF/API contracts; PostgreSQL lifecycle; outbox/audit activity publication; retry-safe claimed jobs | Maintain provider/component rollback and recovery evidence for each release |
| Notification | Product-owned interfaces; tenant permissions; direct API and Customer BFF read contract; `NotificationRequestedV1`/`NotificationQueuedV1`; inbox plus source-event deduplication; transactional notification/audit outbox; PostgreSQL migration 2 and rollback tests | Add approved producers after each producer can resolve recipient/preferences without copying Customer data; complete provider delivery receipt/reconciliation |
| Order | Domain aggregate and Application workflow ports; tenant/branch authorization; Customer BFF/API; versioned workflow events; idempotency; PostgreSQL aggregate and transactional outbox | Extend product audit coverage as return/refund workflows are implemented |
| Payment | Product-owned intent interface/repository; order/tenant/branch authorization; API contract; idempotent intent creation; PostgreSQL baseline | Add transactional payment integration/audit publication before capture/refund workflows are enabled |
| POS | Product-owned shift/cash/terminal interfaces and PostgreSQL stores; authorization scopes; offline operation contracts; outbox schema and audit behavior | Complete server-side offline synchronization consumers, replay evidence, and rollback drills |
| Reporting | Product-owned read/query ports; tenant authorization; Customer BFF/API; idempotent audit-event projection through durable inbox; PostgreSQL projections | Keep projection compatibility/rebuild and rollback evidence current as producers expand |
| Restaurant | Product-owned hierarchy/configuration ports and repositories; tenant/platform policies; Platform Admin and Customer BFF/API contracts; audit integration events and outbox; reversible migrations | Extend the same pattern to future restaurant workflows without weakening organization-leading predicates |

Notification is the first newly closed vertical slice in this phase. Catalog now has transactional publication plus an opt-in live PostgreSQL suite for atomic commit/rollback, append-only enforcement, outbox-store retry state, and migration 4 downgrade/re-upgrade. The Catalog product-integration slice remains open until RabbitMQ outage/recovery behavior is exercised in a release-like environment; independently, full Catalog clean-install evidence remains a production migration release gate. The table deliberately leaves the phase marked partial: it prevents a migration/table scaffold from being mistaken for complete cross-service integration and gives each remaining service a reviewable exit condition.

## Rollout order

1. Apply the owning service migration and any permission migration.
2. Deploy consumers disabled, then verify database connectivity, telemetry, and DLQ policy.
3. Enable consumers and prove duplicate delivery is harmless.
4. Deploy producers/outbox dispatchers only after consumers are compatible.
5. Enable BFF/UI routes last and run cross-tenant denial plus workflow acceptance tests.

Rollback reverses that order: disable the browser route and producers, drain/stop dispatchers and consumers, roll back application binaries, then execute reviewed database downgrade plans. Never downgrade while an incompatible producer is publishing.
