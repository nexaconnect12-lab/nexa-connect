# ADR-048 — retain original QR cashier collection custody

Status: accepted, 24 October 2026.

QR5 collects the original QR4 accepted Order through existing manual settlement, receipt,
cash and sale-reporting paths. A bounded original-UUID reference identifies work without
granting authority. Live customer branch payment permission and POS-owned exact terminal/
store/cashier/shift admission precede collection. Preview is read-only; the Windows client
persists the original UUID, settlement operation and cashier/shift before claim I/O.

Order22 atomically records one immutable custody binding and audit outbox under its Order
lock. The original collecting cashier, terminal and operation remain required even for
committed settlement replay; new payment also requires that exact open shift/store.
Exact committed payment replay remains readable after shift closure. A domain custody
value object, Application checks and transactional settlement predicate protect ownership.
The original price snapshot is loaded, never requoted or placed again. Existing consumers
project one settlement/cash movement/sale; no new payment integration contract is needed.

An unclaimed reference-only settlement would permit competing cashiers to collect before
learning about a conflict. A distributed POS/Order reservation would add cross-service
recovery machinery beyond the existing admission observation. We choose immutable
Order-owned custody plus retained local recovery. It does not lock remote POS closure or
revocation across the payment transaction. Lost terminal/cashier/shift custody requires
operational reconciliation; no unaudited release, expiry or reassignment is provided.

Guest receipts are an allow-listed projection of the original immutable paid receipt,
available only to the original live visitor. Staff PDF lookup and the customer display
retain their existing separate authorization/disclosure rules. Hosted/physical-phone/
clean-CI/release certification remains QR6 and deferred target gates.
