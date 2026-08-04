# ADR-003: Separate Platform and Product Administration Dashboards

- **Status:** Accepted
- **Date:** 2026-08-04
- **Scope:** Shared platform, NexaConnect, and future products

## Context

The product ecosystem requires one dashboard for the owner or operator of all products and websites. Each individual product also requires a separate dashboard for its own product owners and administrators. Combining these responsibilities would expose excessive privileges, couple deployments, and encourage cross-product database access.

## Decision

The shared platform will own a separately deployed Platform Admin Dashboard. Every product will own a separately deployed product-specific administration dashboard.

The Platform Admin Dashboard manages the cross-product control plane:

- Organizations and common memberships
- Registered products and websites
- Organization-level product access
- Shared reference data
- Platform roles and support workflows
- Subscription or licensing data when introduced
- Product availability and approved operational summaries
- Cross-product security and audit summaries
- Navigation or controlled handoff to product dashboards

The Platform Admin Dashboard does not own or directly manage restaurant menus, orders, kitchen tickets, property listings, product payments, or product-specific reports.

NexaConnect owns `NexaConnect.Admin`, which manages restaurant-specific configuration and operations. Future products, such as a property-listing platform, own independent dashboards and APIs for their domains.

Every dashboard uses a separate OpenID Connect client, BFF, session-cookie boundary, scopes, audiences, redirect URLs, and deployment lifecycle.

Dashboards access APIs only. They do not connect directly to PostgreSQL databases. The Platform Admin Dashboard uses Platform Control Plane APIs. Product dashboards use their product gateway/BFF and product-owned APIs.

Platform roles and product roles are separate namespaces. Platform administration does not automatically grant product operational access. Support elevation requires an explicit reason, limited duration, appropriate approval, and audit records.

Platform reporting contains ecosystem-level summaries. Detailed business reporting remains inside each product. Products publish only explicitly approved summary metrics to the platform.

## Initial identity clients

- `platform-admin-bff`
- `nexaconnect-admin-bff`
- One separately registered `<product>-admin-bff` client for every future product

## Repository ownership

The Platform Admin Dashboard belongs in the shared platform repository when that platform is created. It will not be implemented inside NexaConnect.

`NexaConnect.Admin` remains inside the NexaConnect repository and is independently buildable, configurable, deployable, and rollback-capable.

## Consequences

### Positive

- Cross-product and product-specific privileges remain isolated.
- Products can deploy and evolve their dashboards independently.
- Browser sessions have smaller scopes and clearer audiences.
- Platform operations do not require direct access to product databases.
- Product business reporting remains under product ownership.

### Costs and risks

- Multiple frontend and BFF deployments must be operated.
- Navigation and support handoff between dashboards require deliberate design.
- Shared visual components require versioned packages rather than a single coupled frontend.
- Platform summary metrics require explicit contracts and eventual-consistency handling.
- Users with access to multiple dashboards may have separate browser sessions and authorization contexts.

## Alternatives rejected

### One universal administration dashboard

Rejected because it would accumulate excessive privileges, couple product releases, and blur platform and product ownership.

### Dashboard access directly to shared or product databases

Rejected because it bypasses authorization, business invariants, API compatibility, and audit boundaries.
