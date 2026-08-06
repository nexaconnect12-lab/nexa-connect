# Inventory Service

Owns branch stock balances and reservations. The current API exposes stock inspection, stock adjustment, and reservation endpoints under `/api/inventory/v1/branches/{branchId}`. Application contracts are implemented by an in-memory Infrastructure adapter until the PostgreSQL reservation repository is wired; state is not durable across restarts.
