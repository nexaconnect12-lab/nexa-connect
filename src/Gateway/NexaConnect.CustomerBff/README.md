# NexaConnect Customer BFF

This is the Customer Portal's server-side session and tenant-context boundary. It uses the existing `nexaconnect-web-bff` client during the migration period; a future client rename must be explicit and use separate cookies and secrets.

The Phase 8 React portal is in `src/Frontend/apps/customer-portal` and is built into this BFF during publish. It provides the requested navigation in implementation order. Features without an owning versioned API return an explicit tenant-bound `contract-pending` response from the allow-listed `/bff/customer/features/{feature}` route; this is capability status, not synthetic business data.

Endpoints:

- `GET /health/live` provides the unauthenticated process-liveness response; `/` is reserved for the Customer Portal SPA.
- `GET /bff/customer/login` starts the confidential Authorization Code + PKCE flow.
- `GET /bff/customer/logout` clears the session and signs out remotely.
- `GET /bff/customer/me` returns the authenticated subject summary.
- `GET /bff/customer/access` queries Platform Directory for active organizations and enabled product access.
- `POST /bff/customer/tenant` selects a tenant only after validating it against current Platform Directory access.
- `GET /bff/customer/tenant` revalidates the protected tenant selection on every read.
- `GET /bff/customer/catalog/branches/{branchId}/menu-items` forwards the current bearer token and validated tenant headers through the Catalog adapter.
- `GET /bff/customer/inventory/branches/{branchId}/stock` forwards the current bearer token and validated tenant headers through the Inventory adapter.
- `POST /bff/customer/orders/branches/{branchId}/place` submits the tenant-bound order workflow through the Order adapter; organization and branch IDs come from the protected tenant context and route, not the browser payload.
- `GET|PUT /bff/customer/configuration/branches/{branchId}` forwards typed Restaurant configuration. `GET /bff/customer/dashboard` and `/bff/customer/reports/sales` forward Reporting queries. Explicit Media routes cover list, upload start/completion, original download/delete, variant list, and `thumbnail`/`display` download. The BFF derives organization from the protected tenant cookie.
- `GET /bff/customer/activity` forwards safe, cursor-paginated Reporting activity for the protected tenant. No contract-pending feature routes remain.
- `GET /bff/customer/memberships` and `PUT /bff/customer/memberships/{subjectId}` derive the organization from the protected `nexa_connect` tenant selection and forward the server-held bearer token.
- `GET`/`POST /bff/customer/branches` and `PUT /bff/customer/branches/{branchId}` proxy only to Restaurant-owned branch management APIs.

The selected tenant is stored in an encrypted, HTTP-only cookie. Product APIs must still enforce organization and product authorization; the BFF selection is context, not a permission grant. The browser never receives an access token and never calls Platform Directory or product databases directly.

Authentication tickets and saved OIDC tokens are held in the server-side ticket store; the browser cookie contains only an opaque ticket key. Development/Test use an in-memory distributed cache. Outside those environments, startup requires `ConnectionStrings:BffSessionCache` and uses Redis; `BffSessionCache:InstanceName` optionally isolates keys. The BFF never falls back to browser-held tokens.

Unauthenticated or forbidden `/bff/customer/*` API requests return `401` or `403` without redirecting the fetch to Keycloak. The SPA responds to `401` with a top-level navigation to `/bff/customer/login`, preserving the same-origin Content Security Policy boundary.

Catalog, Inventory, and Order independently validate organization access and branch ownership through Platform Directory and Restaurant. Payment applies the same checks plus referenced-order ownership when handling customer-tagged intent operations. The BFF does not treat browser-selected organization or branch identifiers as permission grants, and it does not replace product-owned resource authorization.

Structured logs use service name `nexaconnect-customer-bff`. The BFF validates or creates `X-Correlation-ID`, returns it to the browser, and propagates it through registered product and Platform Directory clients for Grafana/Loki debugging.

Before downstream calls, the BFF refreshes an expiring access token and persists replacement tokens in the server-side ticket. Rejected or unavailable refresh clears the session and returns `401`. Development dependency URLs use direct HTTPS launch-profile endpoints.
JSON stdout is always enabled. Enable OTLP with `Observability__OtlpEnabled=true`; use the [observability guide](../../../docs/Deployment/Observability.md) for the endpoint and queries.
