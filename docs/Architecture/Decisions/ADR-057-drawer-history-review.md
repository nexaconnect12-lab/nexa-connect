# ADR-057: Scoped read-only drawer history and protected restart lookup

Status: Accepted

## Context

UX8A/UX8B consume uncertain attempts but originally require an operation UUID for inspection.
The local hash-only journal cannot reconstruct lookup IDs after restart. History must not
turn observations into opening authority or invent a missing original intent.

## Decision

POS exposes bounded own-actor/exact-terminal list and selected review under existing live
drawer read permission/default-off feature. Application rechecks hierarchy/permission and
terminal relationship before/after parameterized owning reads. Keyset cursors bind context,
not authority. Closed custody can be reviewed. No new command, permission or financial write.

Future native reservations add protected lookup context atomically with local consumption.
An issuer/full-scope/subject hash filters it; original metadata is current-user Windows-DPAPI
protected and validated before use. Legacy rows remain consumed, without backfill or ID
inference. Read-only SQLite never creates state. Only an authorized server list permits local
context loading; no offline fallback bypasses live authority. Local/server observations stay
separate. Staff select rows, with cancellation/privacy generation fences for late responses.
Unknown, absent or mismatched evidence prompts investigation, never reset/retry/renewal,
physical proof or financial mutation. Production driver remains disabled.

## Consequences

UX8D subsequently separates default-off `DrawerIntents:HistoryEnabled` for list/selected
review from `Enabled`, which continues to gate reserve/outcome/legacy reads and also admits
history. History-only rollout keeps every live authorization check and cannot enable commands.
See [pilot rollout](../../Deployment/POS-UX8D-Pilot-Acceptance.md).

POS25 adds one history index and compatibility0.40.0, no server table or grant. Native context
is an additive side table for future attempts, not a financial-schema change. Preserve it and
Windows-user keys. Older opaque local rows may be individually unrecoverable; authorized
server history/support remains necessary. Recent-window/page bounds limit native memory;
older server history is retained. No manager delegation or global lockout of different
deliberate manual operations. Authority remains check-time, not atomic distributed revocation.

See [workflow/limits](../../Deployment/POS-UX8C-Drawer-History.md) and
[API](../../API/Drawer-Intents.md).
