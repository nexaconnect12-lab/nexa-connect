# Inventory Service

Owns branch stock balances and reservations. The API exposes stock inspection, stock adjustment, and reservation endpoints under `/api/inventory/v1/branches/{branchId}`. It uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Inventory` to use durable stock and transactional reservation updates.

Customer Portal calls include the shared tenant-context headers. Inventory validates Platform Directory access, Restaurant branch ownership, and the applicable `inventory.stock.*` or `inventory.reservation.*` permission before execution. Customer persistence paths include `organization_id`; migration 4 adds composite tenant keys and an active-reservation tenant index. Configure `Services:PlatformDirectory`, `Services:Restaurant`, `Services:Authorization`, and workload credentials.

Migration 4 marks legacy simplified stock/reservation rows with the empty organization UUID. Backfill them from Restaurant branch ownership before customer traffic or downgrade, and verify old-key uniqueness before restoring version 3.
