# NexaConnect Customer Portal

The browser application authenticates through its ASP.NET Core BFF; it does not handle OAuth tokens directly.

Keycloak client: `nexaconnect-web-bff` (current Customer Portal client; a future rename to `nexaconnect-customer-bff` requires an explicit client migration)

- Confidential client using Authorization Code flow
- Exact callback: `<web-origin>/signin-oidc`
- Exact post-logout callback: `<web-origin>/signout-callback-oidc`
- Server-side client secret from the deployment secret manager
- Secure, HTTP-only, same-site BFF session cookie
- Access and refresh tokens retained only in the server-side session store
- Anti-forgery protection on state-changing BFF endpoints

The BFF must validate issuer, authorization response state and nonce, use the configured API audience, rotate its session on login, and implement remote plus local sign-out. The React application must never persist access or refresh tokens in browser storage.
