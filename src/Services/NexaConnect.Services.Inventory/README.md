# Inventory Service

Owns branch stock balances and reservations. The current API exposes stock inspection, stock adjustment, and reservation endpoints under `/api/inventory/v1/branches/{branchId}`. The service uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Inventory` to enable the parameterized PostgreSQL reservation repository and durable inbox store.
# Inventory Service

Inventory uses the in-memory adapter by default. Set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Inventory` to use durable stock and transactional reservation updates.
