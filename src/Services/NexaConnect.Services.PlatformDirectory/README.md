# Platform Directory Service

This service owns cross-product organization membership and organization-level application access. It never reads Keycloak tables; it uses the validated `sub` claim as the stable identity identifier.

`GET /api/platform-directory/v1/organizations/{organizationId}/access` returns access only when the caller has an active membership and the organization has enabled `nexa_connect` access. Unauthorized callers receive `403` without membership details.

`GET /api/platform-directory/v1/me/access` returns the authenticated subject's active organizations and enabled application access. It is the Customer Portal's starting tenant-context query; it does not replace product-specific authorization.

`GET /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}/access` requires `platform-owner` or `platform-admin`.

Control-plane management endpoints require `platform-owner` or `platform-admin`: organization create/update, membership status changes, product registration, and organization-product access changes. They use the authenticated `sub` as the audit actor and persist through the Application-owned management interface and Infrastructure PostgreSQL repository.

The support-elevation endpoints implement request, independent approval, effective-access lookup, audit inspection, and revocation. An elevation is scoped to one support subject, organization, and application; it lasts 5–240 minutes, cannot be self-approved, and is ineffective after expiry or revocation. Platform Directory stores elevation state and append-only request/approval/revocation audit rows through an Application-owned repository port and Infrastructure PostgreSQL adapter.

Configure the service database through `ConnectionStrings__PlatformDirectory` using the restricted `platform_directory_app` runtime role. Apply Platform Directory migration version 2 before enabling support elevation.

Phase 3 platform administration is exposed under `/api/platform-directory/v1/platform`. It lists, creates, enables/disables, and assigns allow-listed platform roles to Keycloak users through an Application port and Infrastructure Keycloak Admin API adapter. Configure `KeycloakAdmin__BaseUrl`, `KeycloakAdmin__Realm`, `KeycloakAdmin__ClientId`, and the secret-only `KeycloakAdmin__ClientSecret`; the confidential client requires only the Keycloak service-account permissions needed to view/manage users and realm-role mappings.

`GET /platform/roles` returns the fixed platform role/permission catalog, `GET /platform/audit` queries bounded append-only administrative records, and `GET /platform/summary` returns Platform Directory-owned ecosystem counts. Apply Platform Directory migration version 3 before enabling these endpoints. Detailed product business metrics are not queried from product databases and are intentionally absent.

The audit table currently records successful platform-user create, update, and role-change operations only; it is not a unified audit of every control-plane route. Keycloak mutation, role mapping, and PostgreSQL audit insertion are not atomic across systems. If role mapping or audit persistence fails after a Keycloak mutation, the API returns an error and operators must reconcile the identity state before retrying.

`GET /health` is an anonymous process-liveness endpoint. It does not currently assert database readiness.

Operational telemetry uses `NexaConnect.Observability`: structured JSON remains on stdout and OTLP export is optional through `Observability__OtlpEnabled` and `Observability__OtlpEndpoint`. Request logs include method, path, status, duration, trace ID, and a validated `X-Correlation-ID`; they exclude bodies, query strings, credentials, cookies, and arbitrary headers. These logs do not replace the append-only support-elevation audit history.
