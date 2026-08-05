# NexaConnect Admin

The administration browser application uses an independently deployed ASP.NET Core BFF and session boundary.

Keycloak client: `nexaconnect-admin-bff`

- Confidential client using Authorization Code flow
- Exact callback: `<admin-origin>/signin-oidc`
- Exact post-logout callback: `<admin-origin>/signout-callback-oidc`
- Independent client secret and session cookie from the Web BFF
- Secure, HTTP-only cookie and server-side token storage
- Anti-forgery protection on state-changing BFF endpoints

Platform administration is not implemented by this client. `platform-admin-bff` belongs to the shared-platform repository and must use separate scopes, audiences, cookies, secrets, APIs, and deployment controls.
