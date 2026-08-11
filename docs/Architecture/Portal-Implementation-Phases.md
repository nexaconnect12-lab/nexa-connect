# Portal implementation phases

This document records the agreed Product Owner Portal and Customer Portal implementation sequence. Detailed trust boundaries remain canonical in [ADR-006](Decisions/ADR-006-portal-separation-and-tenant-isolation.md).

## Current status

| Phase | Scope | Status |
|---|---|---|
| 1 | Boundaries and contracts | Complete |
| 2 | Identity and authorization | Complete |
| 3 | Platform control-plane APIs | In progress |
| 4 | Customer tenant APIs | Partially implemented |
| 5 | BFF layer | Foundations implemented |
| 6 | Frontend foundations | Planned |
| 7 | Product Owner Portal | Planned |
| 8 | Customer Portal | Planned |
| 9 | Media service | Schema scaffold only |
| 10 | Product service integration | Partially implemented |
| 11 | Testing | Continuous; partial coverage implemented |
| 12 | Deployment and operations | Development foundation; production hardening planned |

## Phase 2 completion evidence

Phase 2 is complete in the current development implementation:

- Product Owner, Customer Portal, and product administration use separate OIDC clients and BFF/session boundaries.
- Platform roles are `platform-owner`, `platform-admin`, `platform-support`, and `platform-auditor`.
- Customer roles are `customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, and `customer-viewer`.
- Product-specific roles remain a separate namespace and platform roles do not automatically grant product permissions.
- Portal cookies, server-side ticket stores, secrets, scopes, redirect URIs, and Data Protection applications are isolated.
- Support elevation is scoped to a support subject, organization, and application; requires a reason and independent approval; lasts 5–240 minutes; and becomes ineffective on expiry or revocation.
- Support request, approval, and revocation actions are transactionally recorded in audit history whose database trigger rejects updates and deletes.
- Tenant-authorization tests cover denial across organization boundaries for implemented customer-facing product slices.

Production realm/client configuration remains environment-owned. The checked-in Keycloak realm is a local fixture, and persistent realms require an explicit reviewed identity migration because startup import does not overwrite them.

## Next phase

The active roadmap step is Phase 3: complete Platform control-plane APIs for platform user administration, platform permissions, audit query contracts, and platform summary/reporting contracts. Existing organization, membership, product registration, product access, and support-elevation slices already follow Domain/Application/Infrastructure/API boundaries.
