# Portal implementation phases

This document records the agreed Product Owner Portal and Customer Portal implementation sequence. Detailed trust boundaries remain canonical in [ADR-006](Decisions/ADR-006-portal-separation-and-tenant-isolation.md).

## Current status

| Phase | Scope | Status |
|---|---|---|
| 1 | Boundaries and contracts | Complete |
| 2 | Identity and authorization | Complete |
| 3 | Platform control-plane APIs | Complete (development validation) |
| 4 | Customer tenant APIs | Complete (implemented customer API surface) |
| 5 | BFF layer | Hardening implemented; broader integration validation continuous |
| 6 | Frontend foundations | Complete |
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

## Phase 3 implementation evidence

Platform Directory now exposes Application-owned use cases for organization and membership management, product registration, organization-level customer product access, Keycloak-backed platform user administration, an allow-listed platform role/permission catalog, append-only platform administration audit queries, and an ecosystem directory summary. Infrastructure owns all PostgreSQL and Keycloak Admin API calls; controllers and both portals remain database-free.

The current summary covers Platform Directory-owned counts only. The new audit query covers successful platform-user create, update, and role-change operations; it is not yet a unified history for organization, membership, product-access, or support activity. Product business metrics remain product-owned and require explicit versioned publication contracts before they can be added to the platform projection.

Development validation now includes a manually observed version-2 to version-3 Platform Directory migration and no-op version-3 re-plan, browser/BFF authorization checks for owner, support, and auditor roles, the checked-in least-privilege `platform-directory-admin` service account, and automated live Keycloak/PostgreSQL integration tests. The tests cover user lifecycle, role mapping, immutable audit persistence, generated-user/schema cleanup, and the explicitly reconcilable partial state when identity creation succeeds before audit persistence fails. Production identity migration, secrets, resilience, and operational reconciliation remain environment-owned deployment work.

## Next phase

Phase 4 is complete for the implemented customer API surface. Catalog, Inventory, Order, Payment, and Customer resolve active organization/product access from the authenticated bearer `sub`, reject conflicting tenant context, evaluate product-owned permission codes, and apply organization/resource ownership before use cases execute. Only allow-listed workload `azp` identities can use internal unscoped paths. Catalog and Inventory customer persistence paths include `organization_id`; Customer was already organization-keyed, while Order and Payment validate stored ownership. Automated coverage currently includes authorization helpers, migration catalogs, and Catalog/Order cross-tenant controller paths; equivalent PostgreSQL-backed controller tests for every service remain continuous Phase 11 hardening rather than Phase 4 behavior work.

Phase 5 hardening is implemented. Both BFFs keep tokens in server-side tickets, renew expiring access tokens, clear sessions when renewal fails, use direct HTTPS service addresses, and propagate safe correlation identifiers. Platform Admin forwards bodyless responses correctly and exposes narrow bootstrap proxy routes for Restaurant-owned hierarchy and Authorization-owned product-role provisioning without direct database access. Unit coverage demonstrates successful renewal, bodyless copying, route protection, and provisioning validation; rejected-refresh and PostgreSQL-backed proxy/controller scenarios remain continuous integration hardening.

Phase 6 frontend foundations are implemented as an npm workspace under `src/Frontend`. Independently versionable packages now provide the Ant Design theme, responsive portal layout/navigation, BFF API contracts, Zod form validation, localization, safe error presentation, presentation-only capability helpers, and redacted UI telemetry. The workspace has strict TypeScript project boundaries and unit coverage for contracts, authorization hints, validation, localization, error handling, and telemetry redaction.

These packages share components and contracts only. Each portal supplies its own capability evaluator, message catalogs, BFF base contract, and telemetry service name. The UI helpers never resolve roles or tenants and never authorize an operation; separate BFFs and owning services retain all runtime authorization decisions described by ADR-006.

The active roadmap step is Phase 7 Product Owner Portal. New customer endpoints must continue to meet the Phase 4 tenant contract as part of their initial implementation.
