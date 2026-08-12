# NexaConnect Product Administration Portal

The administration browser application uses an independently deployed ASP.NET Core BFF and session boundary.

Keycloak client: `nexaconnect-admin-bff`

- Confidential client using Authorization Code flow
- Exact callback: `<admin-origin>/signin-oidc`
- Exact post-logout callback: `<admin-origin>/signout-callback-oidc`
- Independent client secret and session cookie from the Web BFF
- Secure, HTTP-only cookie and server-side token storage
- Anti-forgery protection on state-changing BFF endpoints

Platform administration is not implemented by this client. `platform-admin-bff` belongs to the shared-platform repository and must use separate scopes, audiences, cookies, secrets, APIs, and deployment controls.

This portal is product-scoped. It must not be used as the ecosystem-wide Product Owner Portal or as the Customer Portal. Customer access remains tenant-scoped through `NexaConnect.Web` and its BFF boundary.

Frontend implementation should consume the shared packages in [`src/Frontend`](../../Frontend/README.md). The portal must supply its own product-administration capability evaluator, localization catalogs, and the telemetry service name `nexaconnect-admin-portal`. It must not import customer or platform runtime authorization state; UI visibility is only a usability hint and every operation remains protected by this portal's BFF and the owning service.
