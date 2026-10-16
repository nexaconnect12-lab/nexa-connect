# ADR-013: Authoritative order pricing snapshots

- Status: Accepted
- Date: 2026-09-28

Restaurant owns versioned branch pricing configuration; Catalog owns menu prices. Order translates both through service-owned ports and owns calculation, confirmation comparison and immutable accepted commercial snapshots. POS and BFF never calculate an authoritative payable amount or write another service's database.

The first slice supports one THB bill-level tax rate, inclusive/exclusive menu prices, service charge on the net subtotal, and tax on service charge. Two-decimal rounding is midpoint away from zero. Inclusive bills retain their menu total through residual included tax. See [the exact formulas and limits](../../API/Order-Pricing.md).

Preview is stateless: a fingerprint covers server-resolved scope, policy and line data. First placement recalculates and requires a match; changed data requires explicit confirmation. Accepted snapshots and the initial event commit together under Order migration 8, and durable unique placement identities fence competing requests. Replay uses accepted history without re-reading current pricing. We do not hold a distributed transaction across configuration/menu reads and order acceptance; the accepted snapshot represents that attempt's reads.

This keeps recovery independent of configuration availability and prevents later menu/tax edits from changing historical payable amounts. It also retires client-priced HTTP order creation, requires coordinated client rollout, and prevents database/application downgrade after priced history exists. Future discounts, mixed tax classes, returns and refunds must extend the financial model explicitly rather than mutate accepted prices. Quotes do not guarantee future prices, and tax-law compliance is outside this architectural decision.
