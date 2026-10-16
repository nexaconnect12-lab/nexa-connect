# ADR-014: Order-owned immutable paid-order receipts

- Status: Accepted
- Date: 2026-09-29

## Context

A cashier needs a stable receipt immediately after payment and must retrieve or reprint the same bill after response loss or restart. Rebuilding from current Catalog or Restaurant configuration could change historical evidence. A POS-only document could be lost with the terminal and disagree with provider recovery or reconciliation.

## Decision

Order owns an immutable versioned receipt snapshot and issues it inside every transaction that first commits `Paid`. It uses accepted Order pricing and lines, records the completion timestamp and tender, and derives a stable ordinary-receipt number from the Order UUID. PostgreSQL preserves the first committed candidate under concurrency and prevents later receipt, ownership, amount, currency or terminal-state mutation.

Reads require live branch-scoped `order.read` and exact organization/branch ownership, including callers that otherwise qualify as trusted workloads. POS persists only the last Order ID, clears displayed financial data when the identity session locks, reauthorizes each reprint, and treats preview/print as post-payment operations.

## Consequences

Recovery, manual settlement and normal checkout produce the same receipt semantics. Historical Paid Orders are not inferred or backfilled because original tender and paid time may be unavailable. The UUID-derived number is an ordinary operational identity, not a gapless fiscal sequence. Tax invoices, fiscal signing, device-specific printer adapters, refunds and void documents require later decisions.
