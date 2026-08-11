# ADR-006: Separate Product Owner, Product Administration, and Customer Portals

- **Status:** Accepted
- **Date:** 2026-08-10
- **Scope:** Product ecosystem portals and all tenant-facing products

## Context

The ecosystem has three portal trust boundaries:

- Product owners and platform administrators manage products, organizations, global configuration, support, and cross-product summaries.
- Product administrators manage one product's configuration and operations.
- Customers manage only their own organization, enabled products, and product resources.

These users have different authorization models, operational risk, scaling patterns, and release requirements. Combining them into one portal would increase the blast radius of an authorization defect and make tenant isolation depend on every shared route and UI branch being correct.

## Decision

The ecosystem will use separately deployed portal applications in three categories:

1. **Product Owner Portal** — a platform control-plane application for cross-product management.
2. **Product Administration Portals** — one product-scoped application per product, such as `NexaConnect.Admin`.
3. **Customer Portal** — a tenant-scoped application for customer organizations and their enabled products.

Each portal has its own BFF, OIDC client, session cookie, scopes, audiences, deployment lifecycle, and authorization policies. The portals may share versioned design-system components, API contracts, client libraries, validation, localization, and telemetry packages, but they do not share a runtime authorization boundary.

The Product Owner Portal does not access product databases directly. It uses Platform Control Plane APIs and explicitly approved product administration APIs. The Customer Portal uses tenant-aware BFF endpoints and product-owned APIs.

The existing `NexaConnect.Web` is the starting point for the Customer Portal and retains `nexaconnect-web-bff` during migration. `NexaConnect.Admin` remains the NexaConnect product administration dashboard. The ecosystem-wide `platform-admin-bff` and Product Owner Portal belong to the shared platform boundary; NexaConnect's `NexaConnect.PlatformAdminBff` is only a temporary compatibility foundation and does not own platform data or APIs.

## Authorization and tenancy

Platform roles (`platform-owner`, `platform-admin`, `platform-support`, and `platform-auditor`) are separate from customer roles (`customer-owner`, `customer-admin`, `customer-manager`, `customer-user`, and `customer-viewer`). Platform roles do not automatically grant product operational permissions.

Customer requests resolve the stable identity `sub`, active organization membership, product access, and product-specific resource authorization before application use cases execute. Product data is filtered by server-derived organization identifiers and ownership constraints; browser-supplied tenant identifiers are never trusted as authorization proof.

Support access is explicit, time-limited, approved, and audited. Media is owned by the Media service and accessed through authorized media APIs or short-lived signed object-storage URLs.

## Consequences

### Positive

- A customer authorization defect cannot directly expose platform administration routes.
- Customer and owner portals can scale, deploy, and roll back independently.
- Separate sessions, cookies, audiences, and secrets make trust boundaries observable.
- Shared UI and contract packages preserve reuse without coupling portal authorization.
- New products can add product-specific customer modules without expanding the owner portal's customer data surface.

### Costs and risks

- Two frontend and BFF deployments must be operated.
- Shared packages need versioning and compatibility discipline.
- Cross-portal navigation and support handoff require explicit, audited flows.
- Platform summaries are eventually consistent and must use approved contracts.

## Rejected alternative

A single portal with RBAC was rejected as the default architecture. It is acceptable only for a temporary prototype or a small product with one trust boundary; it is not the long-term ecosystem design.
