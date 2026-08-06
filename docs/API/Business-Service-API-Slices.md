# Business Service API Slices

The former weather scaffold endpoints have been replaced with initial bounded-context APIs. All endpoints use the shared authenticated API policy.

| Service | Routes | Current persistence |
| --- | --- | --- |
| Catalog | `GET` and `POST /api/catalog/v1/branches/{branchId}/menu-items` | In-memory Infrastructure adapter |
| Inventory | `GET /api/inventory/v1/branches/{branchId}/stock`, `PUT .../stock/{productId}`, `POST .../reservations` | In-memory Infrastructure adapter |
| Order | `POST` and `GET /api/order/v1/orders`; `POST /api/order/v1/workflows/place` | PostgreSQL aggregate, idempotency, and transactional outbox when `Persistence:Provider=PostgreSQL`; otherwise in-memory |
| Payment | `POST` and `GET /api/payment/v1/intents` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory, with restaurant/idempotency-key deduplication |
| Customer | `POST` and `GET /api/customer/v1/organizations/{organizationId}/customers` | PostgreSQL adapter when `Persistence:Provider=PostgreSQL`; otherwise in-memory, with organization boundary checks |
| Notification | `POST` and `GET /api/notification/v1/notifications` | In-memory queued-message adapter |

Catalog, Inventory, and Notification still use in-memory adapters; their PostgreSQL/provider implementations remain next work. Order has a PostgreSQL aggregate repository, idempotency store, transactional outbox, and optional HTTP workflow adapters. No controller contains SQL or direct persistence access.

`POST /api/order/v1/workflows/place` accepts `RestaurantId`, `OrganizationId`, `BranchId`, `Currency`, `PaymentMethod`, a required `IdempotencyKey`, and one or more `{ ProductId, Quantity }` lines. It returns `200 OK` for a completed workflow, `409 Conflict` for rejected/payment-failed orders, `400 Bad Request` for invalid input, and `422 Unprocessable Entity` when a menu, inventory, kitchen, or payment step cannot be completed. In PostgreSQL mode, repeating the same restaurant/key returns the previously persisted order result without replaying the workflow. The optional HTTP adapters are scaffold-only until workload credential propagation is added.
