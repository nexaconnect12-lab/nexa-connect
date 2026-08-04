# ADR-002: Shared Platform Data Ownership Across Products

- **Status:** Accepted
- **Date:** 2026-08-04
- **Scope:** NexaConnect, the shared identity platform, and other products using the same organizations and identities

## Context

NexaConnect will share authentication and selected organization-level information with another product. Directly sharing database tables would couple product deployments, migrations, permissions, availability, and data models. NexaConnect also requires branch-offline operation and cannot depend on live identity or organization database queries for every restaurant operation.

## Decision

Cross-product data is shared through an owning platform capability, versioned APIs, integration events, and stable identifiers. Products do not directly query or modify another product's database tables.

Keycloak owns authentication, credentials, MFA, external identity-provider links, OIDC clients, and stable identity subject identifiers. No product queries Keycloak's internal database.

A Platform Directory capability owns shared business organizations, common identity-to-organization membership, registered product applications, and organization-level application access when those records are required across products.

The initial Platform Directory logical tables are:

- `organizations`
- `organization_memberships`
- `applications`
- `organization_application_access`

NexaConnect owns restaurant-specific data, including restaurants, branches, employee restaurant profiles, devices, menus, orders, kitchen execution, payments, inventory, reporting, and offline synchronization.

NexaConnect records stable external identifiers such as `identity_subject_id` and `organization_id`. These identifiers are not cross-database foreign keys.

Common authentication and organization roles may be shared. Product-specific authorization remains inside each product. NexaConnect exclusively owns permissions such as shift management, discounts, voids, refunds, menu management, kitchen management, financial reporting, and manager overrides.

Branch-edge and POS runtimes use minimal, expiring local identity and authorization projections. They do not call Keycloak or Platform Directory for every offline operation.

Technical tables such as migration history, outbox, inbox, idempotency, and audit records may share conventions and templates, but every service owns a separate physical copy.

Administration of this platform boundary follows [ADR-003](ADR-003-platform-and-product-dashboard-separation.md): one Platform Admin Dashboard manages the cross-product control plane, while every product owns an independently deployed administration dashboard for its business domain.

## Data exchange rules

- APIs provide current platform information when an immediate answer is required.
- Versioned integration events distribute organization and membership changes.
- Consumers build minimal local projections containing only fields required by their business behavior.
- Events never contain credentials, secrets, or unnecessary personal information.
- Removed or suspended access is propagated with an explicit effective time and revocation semantics.
- Offline projections include enrollment, issuance, expiry, and last-synchronized timestamps.
- Service behavior remains auditable when cached authorization is used during an outage.

## Consequences

### Positive

- Products can deploy and evolve independently.
- One capability has clear authority for shared organization information.
- Credentials remain isolated within the identity platform.
- Restaurant-specific authorization does not leak into other products.
- Offline branches can operate from controlled local projections.
- Cross-product data changes are explicit, versioned, and observable.

### Costs and risks

- The Platform Directory becomes a separately operated capability when introduced.
- Consumers must handle eventual consistency and membership revocation events.
- Claims, APIs, and events require versioned compatibility contracts.
- Offline access requires expiration and risk policies because immediate revocation cannot always reach a disconnected branch.
- Duplicate local projections require retention, privacy, and reconciliation controls.

## Alternatives rejected

### Shared physical database tables

Rejected because they couple schemas, deployments, credentials, availability, and ownership across products.

### Copying Keycloak user tables

Rejected because products must integrate through OIDC/OAuth and stable subject identifiers rather than identity-provider internals.

### Putting all authorization in Keycloak

Rejected because detailed restaurant permissions and resource-level decisions belong to NexaConnect and must continue under controlled offline conditions.
