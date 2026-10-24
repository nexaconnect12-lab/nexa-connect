# ADR-047 — atomic QR acceptance into the existing durable Order workflow

Status: accepted for QR4,23 October2026.

Staff acceptance requires live exact branch order.place authority and expected request
version. Order revalidates Restaurant-owned visitor/entry/hierarchy/dine-in policy through
an exact Order-workload-only read, without storing or recovering guest capabilities.
Current Catalog prices/availability and Restaurant pricing are resolved with the same
commercial resolver. Changed prices move the original request to price_review; only that
live visitor may confirm the exact current review fingerprint/version before staff retries.
Neither a staff retry nor guest review creates a new request or extends its deadline.

Acceptance atomically commits the original stable Order (UUID=request UUID), accepted
request version, immutable acceptance provenance/actor/decision and audit/outbox. The
existing Order workflow recovery worker advances Submitted through Inventory and Kitchen
using its fenced claims and idempotent owning dependency operations. There is no QR
foreground coordinator or new worker: one local handoff transaction removes the crash
window between acceptance intent and Order creation. Startup requires PostgreSQL, HTTP
workflow adapters and enabled recovery. Recovery uses the accepted snapshot; later price
or authorization changes cannot rewrite or replace an admitted command.

The Order uses channel qr, service dine_in and cash_manual to stop before payment. Guest
quote fingerprints retain their cash-preview meaning; accepted manual fingerprints are
derived from the same resolved lines/policy with the existing manual method. Accepted
logical table label/entry/visitor provenance is retained in Order-owned acceptance storage,
not silently mapped to Restaurant floor dining_tables or a table account. Existing POS
history and Kitchen contracts remain unchanged; original Order UUID links all effects.

Accepted work does not expire or get pruned by QR3 cleanup. Public status includes the
original Order ID/state, without granting settlement or arbitrary Order lookup authority.
Inventory rejection is a failed original Order, never a reason to place a replacement.
Normal owning cancellation/refund authority remains separate. Visit expiry/revocation
denies subsequent guest access but does not revoke already committed staff acceptance.
Before commit, remote checks are observations; no distributed lock spans service reads.

Order migration21 adds immutable acceptance provenance and review/consent snapshot columns,
state constraints and active-request indexes. No cross-service SQL or new grant/role.
All flags default false; cashier import/settlement remains QR5 and joined hosted/phone
acceptance remains QR6. See the QR4 API, database and deployment guides.

Alternatives considered: a separate QR acceptance worker would require another durable
handoff/lease protocol beside the existing Order recovery worker. Foreground Inventory/
Kitchen calls would leave response-loss and process-crash gaps around Order creation.
The selected local transaction reuses established recovery and idempotency, at the cost
of requiring Order schema21 and active recovery before QR4 enablement. It does not make
Restaurant/Authorization observations atomic with the later owning transaction.
