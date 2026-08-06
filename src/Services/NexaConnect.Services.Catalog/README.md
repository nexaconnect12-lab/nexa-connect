# Catalog Service

Owns branch menu items and price/preparation snapshots. The current API exposes `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` through Application-owned `IMenuCatalog` and an Infrastructure adapter. The adapter is in-memory until the Catalog PostgreSQL repository is wired; it is not durable across process restarts.
# Catalog Service

Catalog uses the in-memory adapter by default. Set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Catalog` to use the durable menu-item repository.
