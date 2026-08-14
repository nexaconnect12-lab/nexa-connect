# Business Service API Slices

Restaurant customer branch management uses `restaurant.branch.read` and `restaurant.branch.manage`. Restaurant combines a coarse customer owner/admin realm role, active Platform Directory access, an Authorization decision, and organization-leading persistence predicates. The Customer BFF derives organization from its protected tenant selection.

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
| Restaurant | Branch management and `GET/PUT .../configuration/branches/{branchId}` | PostgreSQL hierarchy, typed branch configuration, and append-only audit |
| Reporting | `GET .../dashboard`, `GET .../reports/sales`, `GET .../activity` | PostgreSQL event projections only; activity uses cursor pagination |
| Media | list, upload-start/complete, signed download, delete | PostgreSQL metadata plus S3-compatible lifecycle; scanning and variants staged |

Customer Portal Catalog reads use `X-Nexa-Portal-Request: customer`, `X-Nexa-Organization-Id`, and `X-Nexa-Application-Code: nexa_connect` headers. The Catalog service validates organization access through Platform Directory using the forwarded customer bearer token, then checks the selected branch's Restaurant-owned authorization scope with the Catalog workload identity. A branch whose scope organization does not match the selected organization is rejected; the headers remain context, not a substitute for authorization.

The Customer BFF exposes explicit configuration, dashboard, sales-report, activity, and media routes whose organization is derived from the protected tenant selection. No capability remains on the availability placeholder route.

All implemented customer-tenant operations also obtain an Authorization service decision. Permission codes include the existing catalog, inventory, order, payment, and customer profile permissions plus `restaurant.branch.read/manage`, `restaurant.configuration.read/manage`, `reporting.dashboard.read`, `reporting.sales.read`, and `media.asset.read`. Only allow-listed service workload tokens identified by their validated `azp` claim may use internal paths without tenant context; ordinary authenticated users fail closed when context is absent or conflicting. Resource reads generally return `404` to avoid disclosure, authorization denials return `403`, and malformed Catalog context returns `400`.

`POST /api/authorization/v1/role-assignments` provisions the permission set for the selected product role. `tenant-admin` and `store-manager` receive the full implemented tenant-API set; cashier, inventory-controller, accountant, and report-viewer receive narrower role-appropriate sets. The operation requires `system-admin`, `platform-owner`, or `platform-admin` and an organization/restaurant/branch scope.

Platform owners and administrators provision Restaurant scope through `POST /api/restaurant/v1/restaurants` and `POST /api/restaurant/v1/restaurants/{restaurantId}/branches`, normally through the matching Platform Admin BFF routes. Restaurant and Authorization remain the persistence owners; the BFF performs no database writes.

See [Restaurant Provisioning](Restaurant-Provisioning.md) for request, response, validation, status, idempotency, and ownership details.

See [Customer product configuration, reporting, and media](Customer-Product-Configuration-and-Reporting.md) for exact range, currency, checkpoint, cap, concurrency, and failure contracts.

Inventory operations tagged as Customer Portal requests validate active organization access through Platform Directory, confirm the route branch belongs to that organization through Restaurant, and require the operation-specific permission. Internal service calls remain supported without customer portal headers.

Payment intent create and read operations tagged as Customer Portal requests validate active organization access, resolve the referenced Order with the Payment workload identity, require the Order organization and branch to match the protected tenant context, and confirm the Restaurant-owned restaurant/branch scope. Untagged service-to-service workflow calls continue through the authenticated internal boundary. These checks protect the currently exposed intent resource; refunds, capture permissions, and broader financial approval workflows remain future work.

Customer profile routes require the protected organization header to match the route organization and validate Platform Directory access plus `customer.profile.create` or `customer.profile.read`. PostgreSQL reads retain the organization predicate, so a resource identifier alone can never select another tenant's profile.

The Customer BFF exposes `POST /bff/customer/orders/branches/{branchId}/place`. It derives `OrganizationId` from the protected tenant selection and `BranchId` from the route, forwards the server-held bearer token and tenant headers to Order, and accepts only the customer order fields (`RestaurantId`, `Currency`, `PaymentMethod`, `IdempotencyKey`, `Lines`, with optional `OrderId` and `CorrelationId`). Browser-supplied organization or branch fields are not trusted.

Catalog, Inventory, Payment, and Notification use PostgreSQL adapters when `Persistence:Provider=PostgreSQL` and otherwise use their in-memory adapters. Order has a PostgreSQL aggregate repository, idempotency store, transactional outbox, and optional HTTP workflow adapters. No controller contains SQL or direct persistence access.

`POST /api/order/v1/workflows/place` accepts `RestaurantId`, `OrganizationId`, `BranchId`, `Currency`, `PaymentMethod`, a required `IdempotencyKey`, and one or more `{ ProductId, Quantity }` lines. It returns `200 OK` for a completed workflow, `409 Conflict` for rejected/payment-failed orders, `400 Bad Request` for invalid input, and `422 Unprocessable Entity` when a menu, inventory, kitchen, or payment step cannot be completed. In PostgreSQL mode, repeating the same restaurant/key returns the previously persisted order result without replaying the workflow. The HTTP adapters use the configured workload bearer-token provider and retry transient dependency failures; production deployments must configure their service URLs and workload credentials. Customer Portal calls through `/bff/customer/orders/branches/{branchId}/place` derive organization and branch context from the protected BFF tenant selection and route. Order independently revalidates organization access through Platform Directory and branch ownership through Restaurant before running the workflow.
