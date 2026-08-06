# Platform Directory API

## Organization Access

`GET /api/platform-directory/v1/organizations/{organizationId}/access` requires a valid `nexaconnect-api` bearer token. It uses the token `sub` claim and returns `200 OK` with `{ "organizationId": "<uuid>", "granted": true }` only when the caller has an active organization membership and the organization has enabled access to the active `nexa_connect` application. All other authenticated callers receive `403 Forbidden`; the endpoint does not disclose membership or organization state. Missing or invalid tokens receive the standard `401 Unauthorized` bearer challenge.

`GET /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}/access` provides the same check for platform administration and requires the `system-admin` realm role. Authorized administrators receive `200 OK` with `granted` set to either `true` or `false`; unauthenticated callers receive `401` and callers without `system-admin` receive `403`.

These are organization-level checks only. Restaurant, branch, shift, refund, and other product resource authorization remains product-owned and must not be inferred from realm roles.
