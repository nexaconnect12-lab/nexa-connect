# Catalog Service

Owns branch menu items and price/preparation snapshots. The current API exposes `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` through Application-owned `IMenuCatalog` and an Infrastructure adapter. Catalog uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Catalog` to use the durable menu-item repository.

Customer Portal requests must include the validated `nexa_connect` tenant context headers. Catalog verifies organization access through Platform Directory using the forwarded bearer token, rejects malformed or unauthorized portal context before reading branch menu data, and leaves branch ownership and product-specific authorization to the product boundary.
