# Catalog Service

UX14 adds existing-item menu-details reads/edits under live menu.read/write. Catalog9 adds menu_version and immutable before/after audit; row-lock CAS, audit and two versioned outbox events commit atomically. Legacy upserts increment details versions on actual changes while preserving availability. Existing nexaconnect-catalog structured status/category diagnostics remain safe. See [API/debugging](../../../docs/API/Menu-Details.md) and [schema/rollback](../../../docs/Database/Menu-Details.md).

UX13 exposes customer branch menu-availability GET and per-product PUT under existing menu.read/write. Catalog8 adds availability_version and immutable audit; row-lock CAS/audit/outbox commit together. Metadata upsert preserves the flag/version. Existing correlated nexaconnect-catalog diagnostics use safe status/category only. See [API/debugging](../../../docs/API/Menu-Availability.md) and [schema/rollback](../../../docs/Database/Menu-Availability.md).

UX11 adds MenuOrganizationService and Catalog7 versioned branch layouts, Domain validation, parameterized locked persistence and atomic immutable audit/generic outbox. Existing live branch menu.read/write applies; internal read requires exact Order workload. No price event or financial DTO changes. Service nexaconnect-catalog logs safe Menu organization boundary events with shared correlation. See [API/debugging](../../../docs/API/Menu-Organization.md), [storage](../../../docs/Database/Menu-Organization.md) and [rollout](../../../docs/Deployment/POS-UX11-Menu-Organization.md).

Branch-scope authorization uses an absolute request URI derived from `Services:Restaurant`
without changing the shared client's BaseAddress after sending requests. Repeated and
concurrent live checks remain valid, including menu-photo read/preview/save/delivery
revalidation. Workload credentials, correlation propagation and live permission checks are
unchanged. `CatalogBranchScopeReuseTests` exercises the real adapter and the photo workflow.

Catalog6 adds approved branch/product menu-photo references with domain version/approval checks, owning menu-row locks and atomic local audit/generic outbox. Existing live menu.read/write applies; assignment/preview also requires Media read. Services:Media and existing Catalog workload credentials fetch bounded processed PNG; exact Order workload selected-photo read is private. Service nexaconnect-catalog propagates correlation and emits safe Menu photo boundary events. See [API/debugging](../../../docs/API/Menu-Photos.md), [storage](../../../docs/Database/Menu-Photos.md) and [deployment](../../../docs/Deployment/Menu-Photos.md).

UX6 adds migration 5 and customer barcode read/write/lookup routes. Domain owns code,
collision and version rules; Infrastructure commits tenant-wide registry, branch binding,
immutable detailed audit and generic audit outbox under one tenant-serialized transaction.
Existing live branch menu.read/write applies; workload bypass is absent. Same-product
cross-branch sharing is allowed, foreign-product collisions fail, and clearing releases
only unused codes. In-memory mode is non-durable. No financial/guest contract changes.
See [API and safe correlation queries](../../../docs/API/Product-Barcodes.md),
[schema/grants/rollback](../../../docs/Database/Product-Barcodes.md) and
[deployment/tests](../../../docs/Deployment/POS-UX6-Barcode-Scanning.md).

QR2 adds private `/api/catalog/v1/internal/organizations/{organization}/branches/{branch}/guest-menu`
for exact `nexaconnect-order-service` workload only. Application projects the owning menu,
bounded to 200 items, 200 characters/name and 64 characters/station; Infrastructure uses organization+branch parameters
and LIMIT 201 as an overflow sentinel. No guest browser or other workload access; no schema
change. Stations remain private to Order's fingerprint; public Order/BFF responses remove
them. No stock quantities or media assets. Query existing `nexaconnect-catalog` logs for
`QR` and validated CorrelationId; denial/bounds events omit tokens, bodies and prices.
See [contract](../../../docs/API/QR-Menu.md) and [tests/operations](../../../docs/Deployment/QR2-Menu.md).

[Draft provisioning](../../../docs/Deployment/First-Draft-Provisioning.md) uses the owning menu repository with synthetic actor/correlation for three reference menu items. Initial resume preserves existing products; completed reruns do not overwrite edited prices. Existing audit/outbox behavior applies. The portal station picker derives station codes from the live authorized menu.

Owns branch menu items and price/preparation snapshots. The current API exposes `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` through Application-owned `IMenuCatalog` and an Infrastructure adapter. Catalog uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Catalog` to use the durable menu-item repository.

In PostgreSQL mode, every menu-item upsert transactionally appends an immutable Catalog audit record and two outbox messages: `catalog.menu-item.changed.v1` carries the tenant, branch, product, price, availability, and preparation snapshot; `catalog.audit.v1` carries only scoped identifiers, action, actor, and outcome. These rows are always committed with the menu mutation. Enable dispatch with `Outbox:Enabled=true` and configure `Outbox:ConnectionString` with the RabbitMQ URI. The shared transport connects lazily on its first publication attempt and replaces a closed or failed owned connection. When dispatch is disabled or the broker is unavailable, committed rows remain for later retry. The in-memory adapter is intentionally non-durable and does not publish events.

Customer Portal requests must include the validated `nexa_connect` tenant context headers. Catalog verifies organization access through Platform Directory, validates the Restaurant branch scope, and requires `catalog.menu.read` or `catalog.menu.write` from Authorization. Customer queries and writes include `organization_id`; migration 3 makes organization, branch, and product the composite key. Browser-selected IDs are never authorization proof.

`GET /api/catalog/v1/internal/organizations/{organizationId}/products/{productId}/exists` is restricted to trusted workload identities and performs an organization-leading existence lookup for Media owner validation. It returns no product details, uses `404` for an absent or cross-tenant product, and emits a safe structured denial event for untrusted callers.

Migration 3 marks legacy rows with the empty organization UUID. Backfill those rows from Restaurant branch ownership before customer traffic or downgrade; verify legacy-key uniqueness before restoring version 2. Migration 1 owns the transactional outbox; migration 4 adds only append-only Catalog audit objects and its downgrade preserves the baseline outbox and undispatched events. Disable producers before downgrade because version-3 application code cannot create the new audit records.

The pre-production migration-4 scripts were corrected after full-sequence review exposed duplicate ownership of the migration-1 outbox. Any development database that recorded the earlier migration-4 checksum must be rebuilt from the corrected catalog or reconciled through an explicitly reviewed development-only procedure; the runner intentionally rejects silent checksum drift.

Configure `Services:Restaurant`, `WorkloadIdentity:Authority`, `WorkloadIdentity:ClientId`, and the secret `WorkloadIdentity:ClientSecret` in deployment configuration. The `nexaconnect-catalog-service` access token must explicitly contain the `nexaconnect-api` audience. The Restaurant scope endpoint accepts the catalog and POS service workload policies; customer access tokens are not forwarded to that endpoint. A token without the API audience produces `401` at Restaurant even when Keycloak accepted the client credentials.

Structured request and dependency logs use service name `nexaconnect-catalog`; validated correlation IDs propagate to Platform Directory, Restaurant, and Authorization.
JSON stdout is always enabled. Set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317` for Loki/Grafana; see the [observability guide](../../../docs/Deployment/Observability.md) for queries.
For mutation debugging, query `{service_name="nexaconnect-catalog"} | CorrelationId="<validated-id>"`. Operational logs never contain event payloads, menu prices, request bodies, tokens, cookies, or arbitrary headers.

Phase 11 component verification is in `CatalogPostgresIntegrationTests`. Set `NEXACONNECT_CATALOG_INTEGRATION_DB` and a Development/Test environment for PostgreSQL coverage. Add `NEXACONNECT_RABBITMQ_ACCEPTANCE=1` and `NEXACONNECT_RABBITMQ_INTEGRATION_URI` to verify an unreachable connection attempt, a Catalog commit while no broker connection exists, and later publication over a new real RabbitMQ connection. The production transport supplies publisher confirms; the test verifies both routing keys as persistent messages, isolated queue consumption, correlation payloads, and publication timestamps. It does not stop a running broker, prove automatic reconnection of an established dispatcher connection, or perform a full Catalog migration 1-4 clean install.

`CatalogMigrationRunnerAcceptanceTests` supplies the separate full-database 0→4→3→4 runner check. It requires an explicitly enabled Development/Test administrator connection with database create/drop permission and manages only validated, uniquely named disposable databases.
