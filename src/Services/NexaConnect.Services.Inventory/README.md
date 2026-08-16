# Inventory Service

Owns branch stock balances and reservations. The API exposes stock inspection, stock adjustment, and reservation endpoints under `/api/inventory/v1/branches/{branchId}`. It uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Inventory` to use durable stock and transactional reservation updates.

Tenant-scoped PostgreSQL stock set, reservation create, and reservation release mutations atomically persist append-only audit plus `inventory.stock-set.v1`, `inventory.reservation-created.v1`, or `inventory.reservation-released.v1` outbox state. Active order retries do not decrement stock twice. Migration 1 owns the outbox; migration 5 owns only the audit table and append-only trigger. Enable dispatch with `Outbox:Enabled=true` and secret-managed `Outbox:ConnectionString`; the in-memory adapter is non-durable. Trusted calls without tenant context continue through the legacy unscoped repository methods and do not create product audit or outbox records.

Customer Portal calls include the shared tenant-context headers. Inventory validates Platform Directory access, Restaurant branch ownership, and the applicable `inventory.stock.*` or `inventory.reservation.*` permission before execution. Customer persistence paths include `organization_id`; migration 4 adds composite tenant keys and an active-reservation tenant index. Configure `Services:PlatformDirectory`, `Services:Restaurant`, `Services:Authorization`, and workload credentials.

Migration 4 marks legacy simplified stock/reservation rows with the empty organization UUID. Backfill them from Restaurant branch ownership before customer traffic or downgrade, and verify old-key uniqueness before restoring version 3.

Migration 5 downgrade removes audit history but preserves outbox state. Live PostgreSQL atomicity/concurrency/migration and RabbitMQ recovery acceptance remain Phase 11 work.

Structured logs use service name `nexaconnect-inventory`; correlation IDs propagate to Platform Directory, Restaurant, and Authorization.
JSON stdout is always enabled. Enable OTLP with `Observability__OtlpEnabled=true`; use the [observability guide](../../../docs/Deployment/Observability.md) for the endpoint and queries.
