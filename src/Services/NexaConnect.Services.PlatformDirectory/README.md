# Platform Directory Service

This service owns cross-product organization membership and organization-level application access. It never reads Keycloak tables; it uses the validated `sub` claim as the stable identity identifier.

`GET /api/platform-directory/v1/organizations/{organizationId}/access` returns access only when the caller has an active membership and the organization has enabled `nexa_connect` access. Unauthorized callers receive `403` without membership details.

`GET /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}/access` is restricted to the `system-admin` realm role for platform administration.

Configure the service database through `ConnectionStrings__PlatformDirectory` using the restricted `platform_directory_app` runtime role.
