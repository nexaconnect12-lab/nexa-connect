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
Customer input cannot authenticate or invoke a financial action. A separately controlled
idle panel may show a table label and its public guest-admission QR bitmap from a
terminal-provisioned portal export. It clears at lock, sale or timeout. This grants the
same guest entry as a printed table QR. See [operations](../../Deployment/POS-Customer-Table-QR.md).

Cart amounts are estimates. Quotes and accepted Order responses supply authoritative
pricing. Only validated Paid results show payment confirmation; uncertain transport or
mismatched manual-settlement responses retain recovery and never imply payment success.
Manual confirmation validates the exact original Order, tender, amount, currency and nonempty
settlement identity before local recovery clears. This is client response validation, not
a new settlement rule. Existing source-service transactions/idempotency remain unchanged.

Explicitly select distinct staff/customer monitors on initial setup or after a layout change. Place windows using physical
pixel coordinates and PerMonitorV2 awareness, not WPF DIP coordinates. The customer window
cannot activate and contains no operational input. Constrain staff geometry to its assigned
monitor. The 5 November 2026 [0.8.1 placement follow-up](../../Deployment/POS-Display-Placement-Fix.md)
originally constrained staff moves and maximization to the work area. The current staff
window fills the monitor and keeps exact bounds through native move/resize attempts.
Customer-only window state/DPI events refit the existing public view while topology and
private staff bounds remain valid; they do not authenticate or change custody. Actual
topology/settings changes, staff-DPI changes, unexpected customer closure and unresolvable
unsafe placement close customer output, mask staff content and invoke retained-work lock. Do not
automatically move protected work to another monitor or resurrect a customer window after
reconnection in the same process. Stop customer use and restart after checking the private
staff screen and the Windows display layout.

Single-screen staff operation remains the default. An authenticated, non-busy operator may
explicitly close customer output and clear saved roles. Successful role assignment writes
a bounded local preference for the Windows account and terminal. Startup restores only
when the saved hash of monitor interface identities, device names, physical bounds and the
primary display matches the current layout. Customer output begins with a public welcome
view, and existing interactive-login guards still apply. A missing or incompatible
preference falls back to staff-only on the primary monitor.

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

## Staff window presentation follow-up

The cashier window is borderless and fills its current staff monitor in every session
state. Before role assignment it starts on the Windows primary monitor. Explicit roles
select the staff and customer bounds separately. Closing from the cashier surface uses
the normal shutdown path. Display faults continue to hide staff content and close the
customer window.
