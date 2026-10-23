# ADR-039: Explicit staff and customer Windows POS display roles

- Status: Accepted
- Date: 2026-10-21
- Scope: Windows presentation and monitor safety; no business service/schema change

## Decision

Use a second native WPF window on the same Windows extended desktop. The existing main
window retains staff controls and its original authentication, authorization and checkout
pipeline. A detached, allow-listed customer projection contains item names, quantities,
prices, server breakdown/total, bounded tender labels and outcome guidance. It carries no
staff identity, credentials, bank reference, internal identifiers, receipt lookup or command.
Customer input cannot authenticate or invoke a financial action.

Cart amounts are estimates. Quotes and accepted Order responses supply authoritative
pricing. Only validated Paid results show payment confirmation; uncertain transport or
mismatched manual-settlement responses retain recovery and never imply payment success.
Manual confirmation validates the exact original Order, tender, amount, currency and nonempty
settlement identity before local recovery clears. This is client response validation, not
a new settlement rule. Existing source-service transactions/idempotency remain unchanged.

Explicitly select distinct staff/customer monitors each startup. Place windows using physical
pixel coordinates and PerMonitorV2 awareness, not WPF DIP coordinates. The customer window
cannot activate and contains no operational input. Constrain staff geometry to its assigned
monitor. Topology/DPI changes, unexpected customer-window closure or unsafe staff movement
close customer output, mask staff content and invoke the existing retained-work lock. Do not
automatically move protected work to another monitor or resurrect a customer window after
reconnection. Stop customer use and restart on the private staff screen to reassign roles.

Single-screen staff operation remains the default. An authenticated, non-busy operator may
explicitly close customer output while the assignment is intact. Assignments are not persisted;
restart begins without a customer window and existing interactive-login guards still apply.

## Clearing and isolation

Lock shows neutral customer guidance, including when an already-started command completes.
Original-operator resume reads the latest in-memory projection. Paid/cancelled/rejected/failed
outcomes remove transaction details immediately and show only generic outcome guidance.
Sign-out clears the projection. Next cart editing starts a new estimate. Receipt/refund and
cash-review lookups never feed customer output. Restart does not reconstruct private lines
from historical receipts or today's menu; retained pending totals remain verification-only.

## Alternatives and limits

Independent checkout/customer browser sessions would add unnecessary synchronization and
authority boundaries. Automatic reassignment could put staff work on a customer monitor;
requiring explicit role selection and restart after topology loss is deliberately conservative.
The trade-off is interrupted display use until staff restores and reviews the setup.

This is a presentation boundary inside one Windows session, not an OS security/kiosk boundary.
Use Extend and physically verify roles; Windows duplication, remote capture and hardware
drivers cannot be certified by geometry tests. Physical hot-plug, mixed DPI, touch, orientation,
long-order readability and actual device selection remain Phase 9 acceptance. Approved media
and distinct screensavers remain Phase 5.

See [Phase 4 verification and operations](../../Deployment/Next-Iteration-Phase-4.md).
