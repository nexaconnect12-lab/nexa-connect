# Native POS drawer intent storage

UX8C adds drawer_history_context for future factory reservations in the same transaction as consumption: intent-key FK, issuer/full-scope/actor hash index and Windows-current-user DPAPI protected version1 request/timestamp. Plaintext/protected bounds8KiB/16KiB; no tokens, amounts or bank/card/provider data. Lookup IDs/subject remain encrypted, while old hash rows are untouched/consumed. Existing reads are SQLite read-only, create/backfill nothing, and validate issuer/actor/scope/key/fingerprint/timestamps. Preserve context and Windows-user keys with recovery. See [review/lifecycle](../Deployment/POS-UX8C-Drawer-History.md).

UX8A owns the separate [authoritative server intent/audit](Drawer-Intents.md) in POS24.
UX8B software now composes the native store lazily with a bounded server adapter and disabled
driver. Local rows cannot replace server authority or certify physical opening.

The factory selects a separate drawer-intents.db in the existing configured current-user
POS storage: NexaConnect/POS by default, or POS-Pilots/{existing-scope-hash} when isolated.
Default startup/opening workflows do not open or create the journal while production uses
DisabledDrawerDriver. Explicit UX8C history can read an existing journal after live server
authorization, without creating it. It does not
change financial pos-state.db schema, keys, encryption or recovery. See
[software flow and hardware gate](../Deployment/POS-UX8B-Native-Drawer.md).

The separate `drawer_intents` table stores `intent_key` (primary key), `fingerprint`, fixed
`cause`, nullable fixed `reason`, `outcome`, `reserved_utc` and nullable `completed_utc`.
Key/fingerprint are SHA-256 hashes of validated full scope/request facts. Subject, identities,
tokens, commands, queue names and amounts are not stored verbatim. Hashes are opaque local
request evidence, not anonymization guarantees or authorization proof.

All runtime values are parameters in Infrastructure SQL. Before HTTP, a transaction inserts Reserved with
`ON CONFLICT DO NOTHING`, then classifies the existing fingerprint. Completion is one guarded
Reserved-to-Submitted/Unknown/Cancelled update. A unique key fences concurrent reservations;
reopening the file retains incomplete intents and denies another submission. Denied/ambiguous
authority also consumes the attempt. A bounded server outcome report precedes local completion;
report failure leaves Unknown, and a failed local completion leaves Reserved. No financial
database, sum, cash movement, provider transaction or owning-service table is changed.

The path is independent of release/install directories, uses the current-user existing POS
directory ACL and rejects link/junction traversal before opening. Preserve it alongside
financial recovery/backups; never delete it or replace a terminal to repeat an action.
File deletion can defeat local deduplication, so UX8A's installation-independent original-sale
authority is also required. This is not tamper-proof audit or exactly-once physical execution.
UX8C supplies read-only restart history and original-record review; no cleanup, automatic
replay, repair/reset or resolution command is supplied. Physical
activation/retention acceptance remains gated on the verified device and matching packages.
