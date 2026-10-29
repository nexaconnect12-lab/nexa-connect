# Authoritative drawer intents (POS 24)

POS owns three new append-only tables. Authorization 16 adds permission backfill, not tables.
Full catalog count becomes 215. Minimum application compatibility is 0.39.0; the native
preparatory SQLite journal remains separate and unconstructed in production.

| Table | Ownership and invariants |
| --- | --- |
| drawer_intents | Original full terminal/shift/cash scope, actor, cause/reason, original Order when applicable, Authorization decision, database reservation time and fixed60-second expiry. Primary key organization/operation; unique partial organization/Order for cash sales across terminals and operation IDs. |
| drawer_intent_outcomes | At most one submitted/unknown/cancelled client report per original intent, with current report decision/time. Missing row means Reserved/uncertain, never reusable. |
| drawer_intent_audit | Append-only reserved and terminal-stage records with actor and exact decision; same transaction as intent/outcome and existing pos.audit.v1 outbox. |

Foreign keys retain local store/terminal/shift/cash-session history. Application independently
checks live external organization/restaurant/branch hierarchy and scoped permission before
entering the transaction. Infrastructure locks exact POS custody rows, passes their current
status/owner/currency to Domain validation and checks the immutable cash-settlement projection
for sale requests. Database clock is read after custody locking. No remote call is inside
the transaction, and a prior external decision is not a distributed lock or device guarantee.

Reservation serializes custody and uses unique constraints for one winner. A conflicting
operation cannot alter actor/reason/scope; a sale retry retains its first operation identity.
Reservation attempts revalidate locked custody and sale freshness before checking a retained
intent, so closed/stale retries can conflict. Original reads/outcome reports remain available
with their current permissions and do not need open custody or renew expiry.
Reporting locks the original intent, inserts only the first terminal outcome, and preserves
expiry. Same-outcome replay creates no new audit/outbox; changed outcome conflicts. Audit
or outbox failure rolls the entire owning transaction back. Triggers prohibit UPDATE/DELETE
of intent, outcome and audit. There is no cleanup/replay/reset routine. Restrict DDL privileges,
retain backups and recover forward. An intent is not tamper-proof against a database owner.

No cash movement, payment, balance, original settlement or financial revision is written by
this feature. Confirmed cash projection is read as owning evidence; late/missing/noncash
evidence cannot authorize a new sale intent. No drawer command follows broker replay.
Authorized reservation/reporting audit is durable; denied requests use existing Authorization
decision evidence and safe service boundary diagnostics, not a fabricated successful intent.

Empty migration24 downgrade/reapply is supported; any retained intent refuses downgrade.
Existing financial migrations remain intact. See [operations](../Deployment/POS-UX8A-Drawer-Authority.md)
and [local-only preparation](POS-Drawer-Intents.md).
