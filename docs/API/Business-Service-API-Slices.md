# Business Service API Slices

The former weather scaffold endpoints have been replaced with initial bounded-context APIs. All endpoints use the shared authenticated API policy.

| Service | Routes | Current persistence |
| --- | --- | --- |
| Catalog | `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` | In-memory Infrastructure adapter |
| Inventory | `GET /api/inventory/v1/branches/{branchId}/stock`, `PUT .../stock/{productId}`, `POST .../reservations` | In-memory Infrastructure adapter |
| Order | `POST` and `GET /api/order/v1/orders` | In-memory Infrastructure adapter; aggregate status starts at `Submitted` |
| Payment | `POST` and `GET /api/payment/v1/intents` | In-memory Infrastructure adapter with restaurant/idempotency-key deduplication |
| Customer | `POST` and `GET /api/customer/v1/organizations/{organizationId}/customers` | In-memory Infrastructure adapter with organization boundary checks |
| Notification | `POST` and `GET /api/notification/v1/notifications` | In-memory queued-message adapter |

These are functional API slices, not production persistence implementations. PostgreSQL repositories, provider clients, durable outbox delivery, and cross-service HTTP/broker adapters must be added before production deployment. No controller contains SQL or direct persistence access.
