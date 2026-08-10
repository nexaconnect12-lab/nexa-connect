# NexaConnect Customer BFF

This is the Customer Portal's server-side session and tenant-context boundary. It uses the existing `nexaconnect-web-bff` client during the migration period; a future client rename must be explicit and use separate cookies and secrets.

Endpoints:

- `GET /bff/customer/login` starts the confidential Authorization Code + PKCE flow.
- `GET /bff/customer/logout` clears the session and signs out remotely.
- `GET /bff/customer/me` returns the authenticated subject summary.
- `GET /bff/customer/access` queries Platform Directory for active organizations and enabled product access.
- `POST /bff/customer/tenant` selects a tenant only after validating it against current Platform Directory access.
- `GET /bff/customer/tenant` revalidates the protected tenant selection on every read.

The selected tenant is stored in an encrypted, HTTP-only cookie. Product APIs must still enforce organization and product authorization; the BFF selection is context, not a permission grant. The browser never receives an access token and never calls Platform Directory or product databases directly.

Authentication tickets and saved OIDC tokens are held in the server-side ticket store; the browser cookie contains only an opaque ticket key. Development currently uses `IDistributedCache` memory storage. Production must replace it with a shared Redis or equivalent distributed cache before running multiple BFF instances, and must not fall back to browser-held tokens.
