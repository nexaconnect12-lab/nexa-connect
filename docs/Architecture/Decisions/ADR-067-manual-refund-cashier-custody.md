# ADR-067 — Manager approval and cashier custody for manual refunds

## Status

Accepted design. Original receipt review, domain rules and the POS 27 transaction store
are implemented, with default-off new-work APIs, live scoped approval and operation lookup.
The native cashier workspace uses the outstanding assignment queue and exact
operation lookup. POS 28 adds the manager workspace, authenticated five-minute
cashier availability, and immutable release of untouched approvals. Reporting 23
projects transactional POS completions. Exact source comparison is available in
the day-close draft. POS 29 / Reporting 24 add exact cutoff, sealed and permanent
settlement proof. Activation and physical verification remain pending.

## Context

Order owns the original cash or manually verified PromptPay settlement and immutable
sales receipt. POS owns cashier shifts, drawers and their cash movements. Payment owns
captured provider refunds. The manager who authorizes a manual refund and the cashier
who controls the paying drawer use separate accounts.

## Decision

The POS manual refund ledger is scoped to the verified original Order settlement. A
manager's live branch approval reserves an exact amount for a named cashier. The
cashier starts the payout under their own active terminal and shift. A cash payout
also binds the open paying drawer. PromptPay completion records verified return-transfer
evidence without touching any drawer. Each operation retains both identities and its
original authorization decision. The original Paid order and sales receipt stay intact.

Use a separate start and completion so a crash after the physical payout leaves a
recoverable reservation. The server must serialize commands by original settlement.
Cash completion must record the refund and one linked cash movement in the same POS
transaction, ordered against drawer closure. Read-only lookup recovers the original
result when a response is lost. A started payout remains reserved until reconciled.
An existing start is recovery evidence. A replay must never tell the cashier to pay again.
Expose a separate manager action for releasing an untouched approval. Use immutable
completion evidence for reporting and refund-day attribution.

## Consequences

Separate credentials let the cashier retain custody throughout the shift. A manager
can approve without taking over the terminal's drawer. The transaction boundary keeps
cash refunds and the register ledger consistent. Order must supply authoritative
settlement evidence through its own service. Exact-scope authorization, protected
PromptPay evidence, day-close and reporting verification are prerequisites for
activation. [Workflow and status](../../Deployment/POS-Manual-Refund-Review.md)
