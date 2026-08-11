# Platform Directory Service

This service owns cross-product organization membership and organization-level application access. It never reads Keycloak tables; it uses the validated `sub` claim as the stable identity identifier.

`GET /api/platform-directory/v1/organizations/{organizationId}/access` returns access only when the caller has an active membership and the organization has enabled `nexa_connect` access. Unauthorized callers receive `403` without membership details.

`GET /api/platform-directory/v1/me/access` returns the authenticated subject's active organizations and enabled application access. It is the Customer Portal's starting tenant-context query; it does not replace product-specific authorization.

`GET /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}/access` requires `platform-owner` or `platform-admin`.

Control-plane management endpoints require `platform-owner` or `platform-admin`: organization create/update, membership status changes, product registration, and organization-product access changes. They use the authenticated `sub` as the audit actor and persist through the Application-owned management interface and Infrastructure PostgreSQL repository.

The support-elevation endpoints implement request, independent approval, effective-access lookup, audit inspection, and revocation. An elevation is scoped to one support subject, organization, and application; it lasts 5–240 minutes, cannot be self-approved, and is ineffective after expiry or revocation. Platform Directory stores elevation state and append-only request/approval/revocation audit rows through an Application-owned repository port and Infrastructure PostgreSQL adapter.

Configure the service database through `ConnectionStrings__PlatformDirectory` using the restricted `platform_directory_app` runtime role. Apply Platform Directory migration version 2 before enabling support elevation.
