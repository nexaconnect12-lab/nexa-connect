# NexaConnect Keycloak

The development realm includes a dedicated `nexaconnect-media-service` confidential workload client used for Catalog owner validation. Set `NEXACONNECT_MEDIA_SERVICE_CLIENT_SECRET` from `.env`; never reuse a browser or another service's secret.

The local realm includes the confidential `platform-directory-admin` service account used by Platform Directory's Infrastructure identity adapter. Its realm-management assignments are limited to `view-users`, `manage-users`, and `view-realm`. Configure `PLATFORM_DIRECTORY_ADMIN_CLIENT_SECRET`; changing the JSON does not update an already-persisted realm, so recreate only disposable local identity state or apply an explicit reviewed realm migration.

The checked-in realm is a reproducible environment-driven baseline. It contains no human users or literal secrets; its only user entry is the generated `platform-directory-admin` service account. Local Docker Compose supplies development values and imports it into the `nexa-dev` realm on first startup. Production bootstrap supplies production realm, URI, SMTP, MFA, and secret values. If the realm already exists, Keycloak intentionally skips the import.

The realm defines separate service-account clients for POS, Catalog, Order, Inventory, and Payment, plus separate confidential browser clients including the Platform Admin BFF. The Customer BFF and public POS clients explicitly emit the stable Keycloak subject and `nexaconnect-api` audience in access tokens. POS, Catalog and Order workload clients explicitly emit the API audience required by downstream services. Supply every secret and redirect/origin placeholder listed in `.env.example`; workload credentials must not be shared between services.

The Phase 2 role fixture separates platform roles (`platform-owner`, `platform-admin`, `platform-support`, `platform-auditor`), customer roles (`customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, `customer-viewer`), and product roles such as `store-manager` or `cashier`. These roles are mapped into the API `roles` claim. The legacy `system-admin` role remains only for compatibility with older endpoints; new Product Owner Portal assignments use the platform roles.

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

After adding or changing the POS subject mapper in an existing realm, apply that mapper through Keycloak administration and make the cashier sign in again. Provision Platform Directory membership and product authorization against the resulting stable `sub`; `preferred_username` is not a durable authorization identifier.

Do not commit users, passwords, client secrets, signing keys, sessions, or exports from a real environment. Keycloak realm export is not a database backup strategy.

## Production image

`Containerfile` performs Keycloak's build step ahead of startup and enables health and metrics support. A production deployment must additionally supply TLS or trusted reverse-proxy configuration, explicit public and administrative hostnames, managed secrets, database backups, resource limits, and an availability topology appropriate to its recovery objectives.

See the [production runbook](../../docs/Identity/Production-Runbook.md) and `docker-compose.production.yml` for the supported deployment contract and preflight checks.

The dedicated confidential `nexaconnect-reporting-service` client uses `NEXACONNECT_REPORTING_SERVICE_CLIENT_SECRET` and emits the `nexaconnect-api` audience for Restaurant authorization-scope lookup. It is accepted only by the endpoint-specific `BranchScopeReader` policy, not generic workload APIs. Configure Reporting's `Services__Restaurant`, `WorkloadIdentity__Authority`, `WorkloadIdentity__ClientId=nexaconnect-reporting-service` and secret-managed `WorkloadIdentity__ClientSecret`. Existing persisted realms require explicit reviewed client/audience provisioning; restarting Keycloak does not re-import this addition. Never grant a browser client these workload credentials.

Payment's confidential workload client now explicitly emits the nexaconnect-api audience for Order/Restaurant ownership lookup during refunds. Existing persisted realms require a reviewed audience-mapper update; restarting Keycloak does not re-import this change. The client remains credentials-only, with its existing distinct secret and unchanged endpoint authorization policies.
