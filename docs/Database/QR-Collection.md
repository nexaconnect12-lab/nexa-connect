# QR collection persistence

The [staff branch order list](../API/Branch-Order-List.md) reads `orders` plus same-owner
`order_qr_acceptances`/`order_qr_collections` and cancellation state in one parameterized
statement snapshot. It filters organization, restaurant and branch and keyset-pages by
descending creation time/UUID. Actual returned row ownership is checked again in
Application. Viewing performs no claim, guest expiry, Order audit/outbox or financial write.
Existing Order22 and its indexes remain; no table, index or migration is added. Displayed
readiness can change concurrently, so preview/claim remain authoritative.

Order migration22 adds one table, `order_qr_collections` (current full catalog209).
One row per original accepted Order references `order_qr_acceptances.order_id`. A unique
settlement UUID and immutable actor/organization/branch/terminal/store/shift/decision/
correlation/time preserve collecting custody. UPDATE/DELETE triggers deny mutation;
runtime requires SELECT/INSERT only and existing Order/outbox privileges, never TRUNCATE.
No other owning schema changes. Verify real least-privilege grants before deployment.

Claim locks the original eligible Order, inserts custody once and commits its generic
audit outbox atomically. Exact replays compare all custody fields; different contenders
cannot adopt the Order. No foreground remote call occurs within this database transaction.
Manual settlement's existing conditional Paid update additionally checks immutable QR
actor, terminal and settlement UUID under the same transaction as settlement, receipt,
sale publication and outbox. Existing financial fences still apply.

Current Order QR request readers join this table for safe collection status, and
manual-settlement SQL references it even when QR collection is off;
deploy migration22 before current Order binaries. It retains compatibility0.38.0 as the
existing financial schemas are unchanged. POS23/Reporting22, Restaurant4/Catalog4/
Inventory5/Kitchen3 remain current owning versions. Current POS recovery uses existing
SQLite schema2/DPAPI pending-checkout and settlement rows; QR cashier/shift/operation fields
are additive. Preserve the original Windows profile and scope. Do not downgrade clients
with QR collection recovery or delete/transfer its state. No automatic custody archive,
reassignment or repair tooling is introduced. Back up custody with Orders/outbox.

Empty migration22 downgrade/reapply is supported; any collection row blocks downgrade.
After use, forward recovery is required. No cross-service SQL, new broker contract, shared
entity or distributed transaction is added. Financial projection reuses existing original
manual-tender/sale contracts. See [contract](../API/QR-Collection.md).
