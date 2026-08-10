# Catalog Service

Owns branch menu items and price/preparation snapshots. The current API exposes `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` through Application-owned `IMenuCatalog` and an Infrastructure adapter. Catalog uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Catalog` to use the durable menu-item repository.

Customer Portal requests must include the validated `nexa_connect` tenant context headers. Catalog verifies organization access through Platform Directory using the forwarded customer bearer token, then uses its own `nexaconnect-catalog-service` workload identity to read the Restaurant branch scope. The selected branch must belong to the selected organization; malformed, unauthorized, or mismatched context is rejected before menu data is read. Browser-selected IDs are never treated as authorization proof.

Configure `Services:Restaurant`, `WorkloadIdentity:Authority`, `WorkloadIdentity:ClientId`, and the secret `WorkloadIdentity:ClientSecret` in deployment configuration. The Restaurant scope endpoint accepts the catalog and POS service workload policies; customer access tokens are not forwarded to that endpoint.
