# ADR-011: Online ticket-level Kitchen operator queue

- Status: Accepted for the development slice
- Date: 2026-09-22

## Context

Kitchen already owns tenant-scoped tickets, station snapshots, legal status transitions, optimistic concurrency and atomic audit/outbox writes. Operators cannot discover their active work through a queue. Order creates kitchen work before payment, including manual tenders that settle only after cashier confirmation. Branch-edge hardware, offline authorization and device deployment remain undecided.

## Decision

Expose an online, branch-scoped ticket queue and Start/Ready/Complete controls through the existing Customer Portal/BFF. Kitchen Application owns authorization/use-case orchestration; Domain retains lifecycle rules; Infrastructure owns bounded keyset queries and atomic persistence. Preserve existing station snapshot codes and migration-3 storage. The BFF forwards server-derived tenant context, revalidates membership, uses CSRF for mutations and propagates correlation. The browser polls read state, fences writes with displayed versions, and refreshes after uncertainty without replaying a mutation.

Preserve preparation-before-payment semantics. Kitchen status is not a financial status. Order-only cancellation may cancel unfinished preparation, but cannot cancel a completed ticket. All tickets in an order cancellation succeed together or none commit. A completed-preparation conflict requires operational investigation under existing Order compensation failure handling; this slice does not create a new automated financial recovery policy.

## Alternatives and consequences

Requiring payment before preparation would alter manual-tender and dine-in behavior and requires a separate durable cross-service policy. Building branch-edge/offline KDS now would prematurely decide device topology, enrollment and offline authorization. Canonical station management and item-level transitions span additional ownership/contracts and remain future slices.

The initial browser screen is an online operator surface, not selection of the eventual dedicated KDS appliance. Pagination is a moving view, not a transaction across pages. Existing per-ticket transaction boundaries remain unchanged and no migration is needed. A browser or service restart recovers through server state, never a browser write queue. Inventory release can precede a terminal Kitchen compensation conflict; automated wastage/refund/financial exception resolution and joined production acceptance remain required future work. See [the contract and runbook](../../API/Kitchen-Queue.md).
