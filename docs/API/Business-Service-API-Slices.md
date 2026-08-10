# Business Service API Slices

The former weather scaffold endpoints have been replaced with initial bounded-context APIs. All endpoints use the shared authenticated API policy.

| Service | Routes | Current persistence |
| --- | --- | --- |
| Catalog | `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory |
| Inventory | `GET /api/inventory/v1/branches/{branchId}/stock`, `PUT .../stock/{productId}`, `POST .../reservations` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory |
| Order | `POST` and `GET /api/order/v1/orders`; `POST /api/order/v1/workflows/place` | PostgreSQL aggregate, idempotency, and transactional outbox when `Persistence:Provider=PostgreSQL`; otherwise in-memory |
| Payment | `POST` and `GET /api/payment/v1/intents` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory, with restaurant/idempotency-key deduplication |
| Customer | `POST` and `GET /api/customer/v1/organizations/{organizationId}/customers` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory, with organization boundary checks |
| Notification | `POST` and `GET /api/notification/v1/notifications` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory |
| Platform Directory | `GET /api/platform-directory/v1/me/access`, `GET /api/platform-directory/v1/organizations/{organizationId}/access` | Organization membership and enabled product-access boundary; product authorization remains owned by each product |

Customer Portal Catalog reads use `X-Nexa-Portal-Request: customer`, `X-Nexa-Organization-Id`, and `X-Nexa-Application-Code: nexa_connect` headers. The Catalog service validates organization access through Platform Directory using the forwarded customer bearer token, then checks the selected branch's Restaurant-owned authorization scope with the Catalog workload identity. A branch whose scope organization does not match the selected organization is rejected; the headers remain context, not a substitute for authorization.

The Customer BFF exposes `POST /bff/customer/orders/branches/{branchId}/place`. It derives `OrganizationId` from the protected tenant selection and `BranchId` from the route, forwards the server-held bearer token and tenant headers to Order, and accepts only the customer order fields (`RestaurantId`, `Currency`, `PaymentMethod`, `IdempotencyKey`, `Lines`, with optional `OrderId` and `CorrelationId`). Browser-supplied organization or branch fields are not trusted.

Catalog and Notification still use in-memory adapters; their PostgreSQL/provider implementations remain next work. Inventory supports the PostgreSQL reservation repository when `Persistence:Provider=PostgreSQL`, and otherwise uses its in-memory adapter. Order has a PostgreSQL aggregate repository, idempotency store, transactional outbox, and optional HTTP workflow adapters. No controller contains SQL or direct persistence access.

`POST /api/order/v1/workflows/place` accepts `RestaurantId`, `OrganizationId`, `BranchId`, `Currency`, `PaymentMethod`, a required `IdempotencyKey`, and one or more `{ ProductId, Quantity }` lines. It returns `200 OK` for a completed workflow, `409 Conflict` for rejected/payment-failed orders, `400 Bad Request` for invalid input, and `422 Unprocessable Entity` when a menu, inventory, kitchen, or payment step cannot be completed. In PostgreSQL mode, repeating the same restaurant/key returns the previously persisted order result without replaying the workflow. The HTTP adapters use the configured workload bearer-token provider and retry transient dependency failures; production deployments must configure their service URLs and workload credentials.
