# NexaConnect Keycloak

The checked-in realm is a reproducible environment-driven baseline. It contains no users or literal secrets. Local Docker Compose supplies development values and imports it into the `nexa-dev` realm on first startup. Production bootstrap supplies production realm, URI, SMTP, MFA, and secret values. If the realm already exists, Keycloak intentionally skips the import.

## Local use

1. Set all Keycloak values from `.env.example` in the ignored `.env` file.
2. Run `docker compose up -d keycloak`.
3. Wait for `docker compose ps` to report Keycloak as healthy.
4. Open `http://localhost:8080/admin/` and sign in with the bootstrap administrator.
5. Create development users and assign only the coarse realm roles they require.

Validate the realm template without displaying secret values:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-keycloak-realm.ps1
```

OIDC discovery is available at:

```text
http://localhost:8080/realms/nexa-dev/.well-known/openid-configuration
```

Health and metrics are bound locally on management port `9000`.

## Configuration lifecycle

Startup imports are intentionally non-destructive: an existing realm is not overwritten. Treat changes to this file as reviewed desired-state changes and apply them to persistent environments through an explicit administrative automation or migration process. Do not delete the Keycloak database volume merely to apply configuration changes unless loss of all local identity data is intended.

Do not commit users, passwords, client secrets, signing keys, sessions, or exports from a real environment. Keycloak realm export is not a database backup strategy.

## Production image

`Containerfile` performs Keycloak's build step ahead of startup and enables health and metrics support. A production deployment must additionally supply TLS or trusted reverse-proxy configuration, explicit public and administrative hostnames, managed secrets, database backups, resource limits, and an availability topology appropriate to its recovery objectives.

See the [production runbook](../../docs/Identity/Production-Runbook.md) and `docker-compose.production.yml` for the supported deployment contract and preflight checks.
