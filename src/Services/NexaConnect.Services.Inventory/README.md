# Inventory Service

Owns branch stock balances and reservations. The API exposes stock inspection, stock adjustment, and reservation endpoints under `/api/inventory/v1/branches/{branchId}`. It uses the in-memory adapter by default; set `Persistence:Provider=PostgreSQL` and `ConnectionStrings:Inventory` to use durable stock and transactional reservation updates.

Customer Portal stock and reservation calls include the shared tenant-context headers (`X-Nexa-Portal-Request: customer`, `X-Nexa-Organization-Id`, and `X-Nexa-Application-Code: nexa_connect`). Inventory validates active organization access through Platform Directory and branch ownership through Restaurant before reading or reserving stock. Configure `Services:PlatformDirectory`, `Services:Restaurant`, and the dedicated `nexaconnect-inventory-service` `WorkloadIdentity` credentials. Stock adjustment and release remain internal/service operations.
