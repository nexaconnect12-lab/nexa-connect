# Catalog Service

Owns branch menu items and price/preparation snapshots. The current API exposes `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` through Application-owned `IMenuCatalog` and an Infrastructure adapter. Catalog uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Catalog` to use the durable menu-item repository.

Customer Portal requests must include the validated `nexa_connect` tenant context headers. Catalog verifies organization access through Platform Directory, validates the Restaurant branch scope, and requires `catalog.menu.read` or `catalog.menu.write` from Authorization. Customer queries and writes include `organization_id`; migration 3 makes organization, branch, and product the composite key. Browser-selected IDs are never authorization proof.

Migration 3 marks legacy rows with the empty organization UUID. Backfill those rows from Restaurant branch ownership before customer traffic or downgrade; verify legacy-key uniqueness before restoring version 2.

Configure `Services:Restaurant`, `WorkloadIdentity:Authority`, `WorkloadIdentity:ClientId`, and the secret `WorkloadIdentity:ClientSecret` in deployment configuration. The Restaurant scope endpoint accepts the catalog and POS service workload policies; customer access tokens are not forwarded to that endpoint.

Structured request and dependency logs use service name `nexaconnect-catalog`; validated correlation IDs propagate to Platform Directory, Restaurant, and Authorization.
JSON stdout is always enabled. Set `Observability__OtlpEnabled=true` and `Observability__OtlpEndpoint=http://localhost:4317` for Loki/Grafana; see the [observability guide](../../../docs/Deployment/Observability.md) for queries.
