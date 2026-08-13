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

`GET /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}/access` provides the same check for platform administration and requires `platform-owner` or `platform-admin`. Authorized administrators receive `200 OK` with `granted` set to either `true` or `false`; unauthenticated callers receive `401` and callers without a permitted platform role receive `403`.

These are organization-level checks only. Restaurant, branch, shift, refund, and other product resource authorization remains product-owned and must not be inferred from realm roles.

## Phase 1 control-plane contracts

The shared contracts package defines the versioned boundary records for the next Platform Directory APIs:

- `OrganizationSummary` and `CreateOrganizationRequest` for organization lifecycle.
- `UpdateOrganizationRequest` for organization status, name, and time-zone changes.
- `OrganizationMembershipSummary` and `ChangeOrganizationMembershipRequest` for membership lifecycle.
- `ProductRegistration` and `RegisterProductRequest` for the ecosystem product registry.
- `OrganizationProductAccess` and `ChangeOrganizationProductAccessRequest` for enabling or suspending a product for an organization.
- `TenantContext` and `TenantContextResponse` for the server-derived context a BFF passes to a product application use case.

These contracts do not grant access by themselves. The platform resolves identity subject and organization membership; each product still evaluates its own resource authorization. `PlatformAuditEventV1` is the versioned event shape for recording administrative actions and outcomes.

## Platform control-plane operations

The `platform-owner` or `platform-admin` role protects the management endpoints:

- `GET /api/platform-directory/v1/organizations` returns organizations ordered by name and identifier for control-plane administration.
- `POST /api/platform-directory/v1/organizations` creates an organization.
- `PATCH /api/platform-directory/v1/organizations/{organizationId}` changes organization name, status, or time zone.
- `PUT /api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}` invites, activates, suspends, or removes a membership.
- `POST /api/platform-directory/v1/products` registers a product application.
- `PUT /api/platform-directory/v1/organizations/{organizationId}/products` enables, suspends, or disables product access.

These operations use the authenticated `sub` as the audit actor. Product registration and organization access are platform control-plane decisions; they do not grant product-specific operational permissions.

## Customer membership management

`GET /api/platform-directory/v1/customer/organizations/{organizationId}/members` returns non-removed memberships ordered by subject ID. Each item contains `organizationId`, `subjectId`, `status`, lifecycle timestamps, and `concurrencyVersion`. `PUT .../members/{subjectId}` accepts `{ "status": "active", "expectedVersion": 2 }` and returns the updated item with `200`; omit the version only when creating a new membership. Existing rows require the exact version. Both routes require a coarse `customer-owner` or `customer-admin` realm role plus active membership and enabled `nexa_connect` access in the organization. Cross-organization access returns `403`, invalid input `400`, self-mutation or concurrency conflicts `409`, and unauthenticated access `401`. Successful changes transactionally append `customer-membership.changed` audit records.

## Support elevation

Support elevation is scoped to one support subject, organization, and registered application. It never grants product access by realm role alone.

- `POST /api/platform-directory/v1/support-elevations` accepts `{ "organizationId": "<uuid>", "applicationCode": "nexa_connect", "reason": "Investigate failed synchronization", "durationMinutes": 60 }`. A `platform-support`, `platform-admin`, or `platform-owner` caller creates a pending request and receives `201 Created`.
- `POST /api/platform-directory/v1/support-elevations/{elevationId}/approve` requires `platform-admin` or `platform-owner`. The approver must differ from the support subject. Approval returns `200` with active status and an absolute expiry.
- `POST /api/platform-directory/v1/support-elevations/{elevationId}/revoke` requires `platform-admin` or `platform-owner` and makes pending or active elevation ineffective.
- `GET /api/platform-directory/v1/support-elevations/{elevationId}` allows `platform-owner`, `platform-admin`, or `platform-auditor` to inspect the audit-facing state.
- `GET /api/platform-directory/v1/support-elevations/effective?organizationId=<uuid>&applicationCode=<code>` lets an authenticated platform support subject resolve only its own currently active, unexpired elevation. Missing or expired elevation returns `404`.

Durations must be between 5 and 240 minutes and reasons must contain at least 10 non-whitespace characters. Invalid requests return `400`; invalid state transitions, self-approval, and concurrent transitions return `409`. Request, approval, and revocation are persisted transactionally with append-only audit rows.

## Phase 3 platform administration

The following routes use the `/api/platform-directory/v1/platform` prefix:

- `GET /users`, `POST /users`, `PATCH /users/{subjectId}`, and `PUT /users/{subjectId}/roles` require `platform-owner` or `platform-admin`. Creation accepts `{ "username": "operator", "email": "operator@example.test", "enabled": true, "roles": ["platform-auditor"] }` and returns `201`; updates and role replacement return `200`, while an unknown subject returns `404`. Keycloak remains the identity and credential owner, and listing follows all Keycloak pages.
- `GET /roles` allows any of the four platform roles and returns the fixed role code, description, and permission-code collection. Product roles such as `tenant-admin` are rejected.
- `GET /audit?fromUtc=&toUtc=&actorSubjectId=&action=&limit=` requires `platform-owner`, `platform-admin`, or `platform-auditor`. It returns newest-first successful platform-user and customer-membership change records. `limit` is 1–500 and defaults to 100; organization lifecycle, product-access, support, read, and failed-attempt history is outside this table.
- `GET /summary` allows any of the four platform roles and returns `{ "organizationCount": 0, "activeOrganizationCount": 0, "activeMembershipCount": 0, "registeredProductCount": 0, "enabledProductAccessCount": 0, "activeSupportElevationCount": 0, "asOfUtc": "..." }`.

Invalid roles, usernames, audit ranges, or limits return `400`. Missing users return `404`, and Keycloak authentication/network failures return `502`. Keycloak mutation, role assignment, and PostgreSQL audit insertion are not a distributed transaction; a late failure can require operator reconciliation before retry. User mutations record successful outcomes in `platform_audit_records`, whose trigger rejects update and delete operations. The summary reads only Platform Directory-owned tables; detailed product reporting remains behind product-owned APIs or future approved summary events.
