# ADR-037: POS-owned manual-tender terminal admission

Status: Accepted — 15 October 2026

## Context

Order owns manual Paid, immutable receipt, audit and outbox. POS owns terminal/store,
shift and drawer facts. Previously Order accepted a nonempty terminal UUID while the
asynchronous POS projector could reject it after Paid. The one-terminal Windows draft
needs invalid-terminal and cashier-scope rejection before new collection is recorded.

## Decision

Order Application calls a narrow service-owned POS admission port after existing live
customer authorization, stored Order ownership and tender validation, before aggregate
mutation or Paid commit. Only exact Order client credentials may use the internal POS
route. POS resolves Restaurant hierarchy and reads its own active scope and cashier
shift/drawer facts. Infrastructure SQL remains parameterized and local to POS.

Admission returns the POS observation time, bounded to thirty seconds against Order's
pre-call clock. That time is used for the settlement and its receipt/event so a later
close can still attribute the sale to its original shift/drawer. Committed exact replay
is checked before admission and retains live customer authorization; it must remain
recoverable after closure or a POS outage. New admission dependency loss fails closed.
See [HTTP contract](../../API/Terminal-Admission.md).

## Consequences and alternatives

This removes the known arbitrary-terminal admission gap without copying POS persistence
or making Order own device state. Matching binaries and `Services:POS` become mandatory
for new manual settlements. POS is an additional synchronous checkout dependency.
No schema migration or shared entity/contract model is introduced.

Strict workload validation also requires the Order service-account username in access
tokens. The realm template supplies an access-token-only username mapper; persisted realms
require a reviewed mapper update and fresh Order token, rather than relaxed endpoint checks.

The observation does not reserve a drawer or serialize later revocation/closure with
Order commit. A distributed reservation/commit protocol would require durable admission
identity, cancellation/expiry and recovery in both contexts; that larger change is deferred.
Historical POS projection checks remain, and old invalid-terminal settlements are not
rewritten. Strict instantaneous revocation across services is not claimed. Direct Order
SQL against POS or trusting client configuration was rejected because either would violate
ownership or retain the original authorization gap.

Windows packaging uses separate per-user application releases with a local hash inventory,
scope-preserving activation and read-only DPAPI/SQLite preflight. Protected recovery remains
device/user-bound; hashes are drift detection, not a production signing trust chain.
