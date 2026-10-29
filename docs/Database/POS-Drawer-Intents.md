# Preparatory POS drawer intent storage

UX8A adds a separate [authoritative server intent/audit contract](Drawer-Intents.md) in POS24. This document continues to describe the isolated, unactivated native SQLite preparation. Native adapter/composition and vendor driver remain UX8B; local rows cannot replace server authority.

UX8 provides a testable SQLite adapter, not an activated production schema or financial
migration. It has no default runtime path; no POS code constructs it in production.
See [scope and completion gates](../Deployment/POS-UX8-Cash-Drawer.md).

The separate `drawer_intents` table stores `intent_key` (primary key), `fingerprint`, fixed
`cause`, nullable fixed `reason`, `outcome`, `reserved_utc` and nullable `completed_utc`.
Key/fingerprint are SHA-256 hashes of validated full scope/request facts. Subject, identities,
tokens, commands, queue names and amounts are not stored verbatim. Hashes are opaque local
request evidence, not anonymization guarantees or authorization proof.

All runtime values are parameters in Infrastructure SQL. A transaction inserts Reserved with
`ON CONFLICT DO NOTHING`, then classifies the existing fingerprint. Completion is one guarded
Reserved-to-Submitted/Unknown/Cancelled update. A unique key fences concurrent reservations;
reopening the file retains incomplete intents and denies another submission. No financial
database, sum, cash movement, provider transaction or owning-service table is changed.

Future production integration must define a full-scope installation-independent protected
storage path, ACL/lifecycle/recovery/retention policy and authoritative server audit/intent
contract. File deletion/replacement or a different terminal can defeat local deduplication;
this adapter does not claim tamper-proof audit or exactly-once physical execution. No cleanup,
automatic replay or reset is supplied. Existing POS local recovery documentation is unchanged.
