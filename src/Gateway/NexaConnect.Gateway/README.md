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
