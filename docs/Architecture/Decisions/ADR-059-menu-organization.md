# ADR-059: Catalog-owned branch menu organization independent of pricing

- **Status:** Accepted
- **Date:** 2026-10-30
- **Scope:** Catalog, Customer BFF, Order guest presentation, Windows POS and portal

## Decision

Catalog owns a branch-level category/placement aggregate with an expected version. Ordered arrays define category display and item positions; an implicit Uncategorized group preserves existing and newly added products. Domain invariants bound names/IDs/counts/references; Application enforces live branch permissions; Infrastructure serializes first/later writes and validates locked owning product membership. Layout, immutable detailed audit and generic audit outbox share one transaction.

Layout uses separate versioned presentation routes/contracts rather than extending financial menu or quote DTOs. Manager UI selects products by name and saves explicitly. POS sorts/filters wrappers retaining original item references. Order translates the private Catalog contract into a minimal guest projection with live visit and current-menu checks; guest BFF forwards only its cookie capability. Category failures fall back to complete menu browsing without changing financial authority.

## Alternatives and consequences

Adding categories to price/preparation events or quote fingerprints would couple presentation changes to financial recovery and every consumer. Putting branch-specific layouts in Media would violate Catalog ownership. Separate per-category CRUD without a branch snapshot would permit lost ordering/assignment updates and partial UI saves. The chosen bounded aggregate permits one atomic CAS save and clear conflict recovery, at the cost of a200-product/50-category limit and explicit refresh. Each product has at most one category in a branch. No nested categories, category pricing, automatic image approval or offline caching is introduced. Populated downgrade is refused to preserve audit history.

See [API](../../API/Menu-Organization.md), [persistence](../../Database/Menu-Organization.md) and [operations](../../Deployment/POS-UX11-Menu-Organization.md).
