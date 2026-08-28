# ADR-004: Product Authorization And Financial Approval

- **Status:** Accepted
- **Date:** 2026-08-06
- **Scope:** NexaConnect product authorization, financial approvals, and audit retention

## Decision

NexaConnect uses product-owned scoped RBAC with direct user permission overrides. Grants apply at organization, restaurant, or branch scope. Explicit denies take precedence over grants at the same scope; otherwise branch, restaurant, and organization grants are inherited. Keycloak authenticates users and supplies only coarse roles; it never decides product resource access.

The Restaurant capability is authoritative for restaurant and branch hierarchy. Authorization consumes that hierarchy through versioned APIs or events and never reads another service database.

Voids, discounts, refunds, cash payouts, and shift variances are online-only. A manager may approve their own operation when authorized. Limits are configured per restaurant, action, and ISO-4217 currency; the amount limit is inclusive.

Financial approvals and authorization decisions are append-only records. Retain them for at least seven years, restrict normal application roles from update or delete, and export daily to WORM-capable storage. Confirm applicable jurisdictional retention requirements before production.

Long-running financial side effects use a database-backed, fenced decision lease rather than holding a database transaction across network calls. Claiming advances the case concurrency version and records an opaque claim token. Final commit must match both; an expired claim may be taken over only for the same resolution, preventing a conflicting operator outcome after compensation has started. The Authorization decision ID is retained atomically with the resulting history and integration event.

The initial Authorization schema stores scope projections, roles, role permissions, scoped assignments, direct overrides, financial limits, and immutable decision records in its own database.

## Consequences

- Product services must obtain an authorization decision before privileged actions and persist the decision reference atomically with the business transition.
- Offline sessions may perform only non-privileged actions explicitly allowed by the applicable branch policy.
- The Authorization capability must deny when organization membership, resource hierarchy, permission, limit, or currency context is missing.
