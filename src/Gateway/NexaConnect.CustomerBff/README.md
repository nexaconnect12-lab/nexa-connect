# NexaConnect Customer BFF

This is the Customer Portal's server-side session and tenant-context boundary. It uses the existing `nexaconnect-web-bff` client during the migration period; a future client rename must be explicit and use separate cookies and secrets.

Endpoints:

- `GET /bff/customer/login` starts the confidential Authorization Code + PKCE flow.
- `GET /bff/customer/logout` clears the session and signs out remotely.
- `GET /bff/customer/me` returns the authenticated subject summary.
- `GET /bff/customer/access` queries Platform Directory for active organizations and enabled product access.
- `POST /bff/customer/tenant` selects a tenant only after validating it against current Platform Directory access.
- `GET /bff/customer/tenant` revalidates the protected tenant selection on every read.
- `GET /bff/customer/catalog/branches/{branchId}/menu-items` forwards the current bearer token and validated tenant headers through the Catalog adapter.
- `GET /bff/customer/inventory/branches/{branchId}/stock` forwards the current bearer token and validated tenant headers through the Inventory adapter.
- `POST /bff/customer/orders/branches/{branchId}/place` submits the tenant-bound order workflow through the Order adapter; organization and branch IDs come from the protected tenant context and route, not the browser payload.

The selected tenant is stored in an encrypted, HTTP-only cookie. Product APIs must still enforce organization and product authorization; the BFF selection is context, not a permission grant. The browser never receives an access token and never calls Platform Directory or product databases directly.

Authentication tickets and saved OIDC tokens are held in the server-side ticket store; the browser cookie contains only an opaque ticket key. Development currently uses `IDistributedCache` memory storage. Production must replace it with a shared Redis or equivalent distributed cache before running multiple BFF instances, and must not fall back to browser-held tokens.

Catalog validates organization access through Platform Directory and branch ownership through Restaurant; Inventory receives the same validated tenant context and remains responsible for its product-specific authorization. The BFF does not treat browser-selected organization or branch identifiers as permission grants.
