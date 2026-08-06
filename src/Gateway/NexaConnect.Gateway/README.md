# NexaConnect Gateway

The gateway validates Keycloak-issued bearer access tokens before routing protected requests. Runtime configuration is read from `Authentication`:

- `Authority` must be the exact realm issuer URL.
- `Audience` is `nexaconnect-api`.
- `RequireHttpsMetadata` must remain `true` outside local development.

The gateway maps Keycloak's stable `sub` claim as the external identity identifier, `preferred_username` as the display name, and the deliberately configured top-level `roles` claim as the coarse role source. Resource-level restaurant authorization remains inside the owning NexaConnect service.

For a local smoke test, start Keycloak and request its discovery document:

```powershell
Invoke-RestMethod http://localhost:8080/realms/nexa-dev/.well-known/openid-configuration
```

`GET /api/identity/me` requires any valid access token. `GET /api/identity/report-access` demonstrates coarse role enforcement and requires `report-viewer`.

The gateway also provides the web BFF session boundary:

- `GET /bff/login` starts the `nexaconnect-web-bff` Authorization Code + PKCE flow. Its optional `returnUrl` is restricted to a local path; external URLs are replaced with `/`.
- `GET /bff/logout` clears the BFF cookie and signs out from Keycloak.
- `GET /bff/me` returns the authenticated BFF session summary.
- `POST /bff/pos/shifts/open` and `POST /bff/pos/shifts/{shiftId}/close` forward the BFF access token to the POS API.

The BFF forwards only authenticated session requests. Native Windows POS authentication is a separate client flow and does not use this cookie boundary.

The BFF requires `Bff:Authority`, `Bff:RequireHttpsMetadata`, and the confidential `Bff:ClientSecret` setting. Supply the client secret through environment or secret-manager configuration; do not commit it to `appsettings*.json`. The POS forwarding base address is configured by `Services:POS`.
