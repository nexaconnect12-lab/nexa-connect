# Platform Directory API

## Organization Access

`GET /api/platform-directory/v1/me/access` requires a valid `nexaconnect-api` bearer token and resolves the caller from the token `sub` claim. It returns `200 OK` with the current tenant context:

```json
{
  "subjectId": "user-123",
  "organizations": [
    {
      "organizationId": "<uuid>",
      "organizationCode": "acme",
      "organizationName": "Acme Restaurants",
      "applicationCode": "nexa_connect"
    }
  ]
}
```

Only active memberships, organizations, applications, and enabled organization application access are returned. The endpoint does not replace product-specific authorization. Missing or invalid tokens receive the standard `401 Unauthorized` bearer challenge.

`GET /api/platform-directory/v1/organizations/{organizationId}/access` requires a valid `nexaconnect-api` bearer token. It uses the token `sub` claim and returns `200 OK` with `{ "organizationId": "<uuid>", "granted": true }` only when the caller has an active organization membership and the organization has enabled access to the active `nexa_connect` application. All other authenticated callers receive `403 Forbidden`; the endpoint does not disclose membership or organization state. Missing or invalid tokens receive the standard `401 Unauthorized` bearer challenge.

`GET /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}/access` provides the same check for platform administration and requires the `system-admin` realm role. Authorized administrators receive `200 OK` with `granted` set to either `true` or `false`; unauthenticated callers receive `401` and callers without `system-admin` receive `403`.

These are organization-level checks only. Restaurant, branch, shift, refund, and other product resource authorization remains product-owned and must not be inferred from realm roles.

## Phase 1 control-plane contracts

The shared contracts package defines the versioned boundary records for the next Platform Directory APIs:

- `OrganizationSummary` and `CreateOrganizationRequest` for organization lifecycle.
- `OrganizationMembershipSummary` and `ChangeOrganizationMembershipRequest` for membership lifecycle.
- `ProductRegistration` and `RegisterProductRequest` for the ecosystem product registry.
- `OrganizationProductAccess` and `ChangeOrganizationProductAccessRequest` for enabling or suspending a product for an organization.
- `TenantContext` and `TenantContextResponse` for the server-derived context a BFF passes to a product application use case.

These contracts do not grant access by themselves. The platform resolves identity subject and organization membership; each product still evaluates its own resource authorization. `PlatformAuditEventV1` is the versioned event shape for recording administrative actions and outcomes.
