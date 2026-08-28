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
| 7 | Product Owner Portal | Complete (compatibility implementation) |
| 8 | Customer Portal | Functional slices and joined browser harness implemented; environment-specific acceptance remains |
| 9 | Media service | Tenant-authorized upload/download/delete, scanning, quotas, expiry cleanup, and generated variants implemented |
| 10 | Product service integration | Partially implemented |
| 11 | Testing | Continuous; partial coverage implemented |
| 12 | Deployment and operations | Development foundation plus Payment readiness and local metrics/alert rules; production delivery hardening planned |

## Phase 2 completion evidence

Phase 2 is complete in the current development implementation:

- Product Owner, Customer Portal, and product administration use separate OIDC clients and BFF/session boundaries.
- Platform roles are `platform-owner`, `platform-admin`, `platform-support`, and `platform-auditor`.
- Customer roles are `customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, and `customer-viewer`.
- Product-specific roles remain a separate namespace and platform roles do not automatically grant product permissions.
- Portal cookies, server-side ticket stores, secrets, scopes, redirect URIs, and Data Protection applications are isolated.
- Support elevation is scoped to a support subject, organization, and application; requires a reason and independent approval; lasts 5â€“240 minutes; and becomes ineffective on expiry or revocation.
- Support request, approval, and revocation actions are transactionally recorded in audit history whose database trigger rejects updates and deletes.
- Tenant-authorization tests cover denial across organization boundaries for implemented customer-facing product slices.

Production realm/client configuration remains environment-owned. The checked-in Keycloak realm is a local fixture, and persistent realms require an explicit reviewed identity migration because startup import does not overwrite them.

## Phase 3 implementation evidence

Platform Directory now exposes Application-owned use cases for organization and membership management, product registration, organization-level customer product access, Keycloak-backed platform user administration, an allow-listed platform role/permission catalog, append-only platform administration audit queries, and an ecosystem directory summary. Infrastructure owns all PostgreSQL and Keycloak Admin API calls; controllers and both portals remain database-free.

The current summary covers Platform Directory-owned counts only. The new audit query covers successful platform-user create, update, and role-change operations; it is not yet a unified history for organization, membership, product-access, or support activity. Product business metrics remain product-owned and require explicit versioned publication contracts before they can be added to the platform projection.

Development validation now includes a manually observed version-2 to version-3 Platform Directory migration and no-op version-3 re-plan, browser/BFF authorization checks for owner, support, and auditor roles, the checked-in least-privilege `platform-directory-admin` service account, and automated live Keycloak/PostgreSQL integration tests. The tests cover user lifecycle, role mapping, immutable audit persistence, generated-user/schema cleanup, and the explicitly reconcilable partial state when identity creation succeeds before audit persistence fails. Production identity migration, secrets, resilience, and operational reconciliation remain environment-owned deployment work.

## Implementation status and remaining work

The portal roadmap is no longer progressing as a single strictly sequential phase. Phases 1-4, 6, and 7 are complete for their documented development scope; Phase 5 BFF hardening and the Phase 8 and Phase 9 functional slices are implemented. Phase 10 product integration is partial, Phase 11 testing is continuous, and Phase 12 now includes Payment liveness/readiness, Payment/Order recovery telemetry, RabbitMQ metrics/alerts, process-kill/full-broker-restart rehearsal, isolated firing/resolved webhook delivery, and disposable rollback-forward verification. Production paging/escalation, concrete-provider acceptance, broader service readiness, load, and security validation remain release gates.

Phase 11 now includes opt-in live Catalog PostgreSQL and RabbitMQ acceptance plus a successful seven-test Inventory acceptance run against local PostgreSQL 17 and RabbitMQ. Inventory coverage includes outbox/audit rollback, tenancy, concurrency, idempotency, append-only audit, confirmed persistent publication of stock-set, reservation-created, and reservation-released events over a recovery connection, and the actual migration runner's 0→5→4→5 lifecycle in a validated disposable database. The broker cases do not prove automatic reconnection of an established dispatcher. Full service clean installs beyond the accepted slices, broader service-owned PostgreSQL controller coverage, load/security validation, and release-environment browser execution remain continuous work.

Payment intent creation, authorization recovery, immediate capture, and capture recovery are implemented. Capture recovery uses capture-specific PostgreSQL leases and bounded attempts, performs provider status lookup without holding a database transaction, and publishes transactional `PaymentCaptureReconciledV1` plus safe audit state through Payment migration 6. Order migration 2 durably consumes reconciliation and ensures authorization alone cannot mark an order paid. Sixteen coordinated cases passed on 2026-08-28; separate harnesses passed process/broker faults, isolated alert firing/resolution, and Payment/Order rollback-forward lifecycles. Concrete-provider acceptance, production paging/escalation, and live-traffic rollback remain environment-specific gates. Delayed capture, void/refunds, and settlement remain outside this slice.

### Implemented Payment capture recovery

The recovery slice implements the following approved boundary:

1. Add provider capture-status lookup keyed by the existing Payment intent/provider idempotency identity.
2. Add recoverable capture leases, expiry, bounded attempts, and reconciliation timestamps without holding a database transaction across provider I/O.
3. Add a background recovery worker for `capturing` and `capture_unknown` intents.
4. Resolve provider outcomes conservatively: captured finalizes Payment and Order; definitive failure triggers idempotent Inventory/Kitchen compensation; unresolved status keeps Order in `payment_pending`.
5. Publish a versioned `PaymentCaptureReconciledV1` contract through Payment's transactional outbox and consume it through Order's durable, idempotent reconciliation boundary.
6. Reserve Payment migration 6 and Reporting migration 11 for recovery persistence and projection vocabulary, with downgrade guards derived from the financial invariants.
7. Cover concurrency, duplicate delivery, worker termination, delayed/dropped provider responses, tenant isolation, transaction rollback, Reporting replay, and `0→6→5→6` migration behavior.
8. Require live PostgreSQL, RabbitMQ, provider-stub, recovery, and rollback-runbook evidence before closing the slice.

The implementation is development-complete, but its live release evidence remains gated as described above. Capture recovery precedes void/reversal, partial/full refunds, compensation-policy expansion, and end-of-day settlement. Those later slices must preserve authorization, tenant isolation, idempotency, immutable audit, explicit transaction boundaries, and accounting reconciliation.

POS adds successful isolated-schema PostgreSQL 17 evidence for exact/concurrent cash replay, rollback, terminal/shift-subject denial, active terminal scope, shift persistence/concurrency, and duplicate-open conflict. The actual migration runner passed 0→3→2→3. Native SQLite/replay tests and broader operation synchronization remain open.

Kitchen adds five coordinated PostgreSQL 17/RabbitMQ acceptances: multi-station identity, snapshot idempotency, tenant-leading lifecycle/history/audit/outbox atomicity and rollback, confirmed recovery publication, Reporting migration-5 replay, and Kitchen 0→3→2→3. Unit coverage protects legal transitions and exact Order workload creation. The ticket-level Phase 10 slice is closed; item/KDS/offline workflows remain outside this evidence.

Customer profile creation now has a Domain-owned normalized identity, strict tenant-only authorization, organization-leading conflict-safe replay, append-only audit that excludes profile fields, transactional profile/audit event publication, and Reporting migration-6 compatibility. Six coordinated PostgreSQL 17/RabbitMQ acceptances passed locally, including concurrent replay, rollback, confirmed publication, Reporting replay, and 0→2→1→2. The creation slice is closed; status changes, contacts, addresses, preferences, and loyalty remain separate Customer capabilities.

The full Catalog migration-runner acceptance is now implemented for an empty disposable database and executes 0→4→3→4 with history/checksum, schema, and repository verification. It is guarded by an exact generated database-name boundary and explicit opt-in. It has not run successfully in the current local environment because the configured PostgreSQL administrator password is stale and the least-privilege migration role lacks `CREATEDB`; no role or password was changed.

Phase 4 is complete for the implemented customer API surface. Catalog, Inventory, Order, Payment, and Customer resolve active organization/product access from the authenticated bearer `sub`, reject conflicting tenant context, evaluate product-owned permission codes, and apply organization/resource ownership before use cases execute. Only endpoint-specific, explicitly documented workload `azp` identities may use internal unscoped paths; Customer profile routes allow none. Catalog and Inventory customer persistence paths include `organization_id`; Customer was already organization-keyed, while Order and Payment validate stored ownership. Automated coverage currently includes authorization helpers, migration catalogs, and Catalog/Order cross-tenant controller paths; equivalent PostgreSQL-backed controller tests for every service remain continuous Phase 11 hardening rather than Phase 4 behavior work.

Phase 5 hardening is implemented. Both BFFs keep tokens in server-side tickets, renew expiring access tokens, clear sessions when renewal fails, use direct HTTPS service addresses, and propagate safe correlation identifiers. Platform Admin forwards bodyless responses correctly and exposes narrow bootstrap proxy routes for Restaurant-owned hierarchy and Authorization-owned product-role provisioning without direct database access. Unit coverage demonstrates successful renewal, bodyless copying, route protection, and provisioning validation; rejected-refresh and PostgreSQL-backed proxy/controller scenarios remain continuous integration hardening.

Phase 6 frontend foundations are implemented as an npm workspace under `src/Frontend`. Independently versionable packages now provide the Ant Design theme, responsive portal layout/navigation, BFF API contracts, Zod form validation, localization, safe error presentation, presentation-only capability helpers, and redacted UI telemetry. The workspace has strict TypeScript project boundaries and unit coverage for contracts, authorization hints, validation, localization, error handling, and telemetry redaction.

These packages share components and contracts only. Each portal supplies its own capability evaluator, message catalogs, BFF base contract, and telemetry service name. The UI helpers never resolve roles or tenants and never authorize an operation; separate BFFs and owning services retain all runtime authorization decisions described by ADR-006.

Phase 7 is complete as a compatibility implementation in `src/Frontend/apps/product-owner-portal`. It uses the dedicated Platform Admin BFF session shell and exposes organization listing/create/update, membership changes, product registration and enablement, Restaurant-owned hierarchy bootstrap, Authorization-owned hierarchical customer product-role assignment, platform-user create/update/role assignment, role/permission and audit views, the full request/inspection/approval/revocation/effective support workflow, Platform Directory summary counts, and controlled links into separate product administration portals. Membership, product-access, hierarchy, and product-role forms reuse caller-authorized organization, Keycloak user, Restaurant, and branch directories for searchable labels while retaining immutable IDs in their contracts. The hierarchy UI browses organization → restaurant → branch and refreshes after provisioning; product-role scope selectors cascade through the same ownership chain and clear stale descendants. Product registration, product access, and support forms select immutable application codes from a deployment-reviewed catalog rather than deriving business products from Keycloak clients. Elevation and support-role organization identifiers remain manual until appropriately authorized listing contracts exist. Publishing the BFF builds and hosts the SPA on the same origin with explicit browser security and cache policies. Billing plans and product-owned business metrics still require future versioned contracts and are not detailed customer operations.

Phase 8 now provides an independently buildable Customer Portal context and navigation shell with BFF-owned authentication, active organization/product selection, organization profile, enabled-product switching, and ordered navigation for users/memberships, product configuration, branches/locations, dashboards, reports, media, and activity/audit. Pages are client-gated until a valid context from the current access response is selected; `/tenant` and the concrete feature routes perform server-side revalidation of the exact organization/application pair. Organization-scoped tenant-administrator assignments authorize organization-wide Branch and Media calls; restaurant-scoped store-manager and branch-scoped operational assignments remain narrower descendants.

Customer membership administration established the management pattern and branch/location management extended it into Restaurant. Media provides tenant-authorized list, presigned upload/completion, original/variant download, and delete flows through its own service and storage boundary. Completion verifies provider size/SHA-256, file signature, and ClamAV result. Organization original-upload quotas, expired-session deletion, and durable thumbnail/display generation are implemented and covered by focused HTTP, PostgreSQL, MinIO, and ClamAV component tests. An opt-in Playwright harness now joins Keycloak, the Customer Portal/BFF, Media, MinIO, ClamAV, PostgreSQL, and the Media worker for tenant denial and the upload-to-deletion lifecycle. Executing it in each release environment and completing recovery, load, TLS, CORS, credential, and operational validation remain release gates.
