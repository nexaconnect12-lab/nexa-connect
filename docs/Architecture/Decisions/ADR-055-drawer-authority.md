# ADR-055: POS-owned consumed drawer intents before device submission

Status: Accepted

Subsequent [ADR-056](ADR-056-native-drawer-coordination.md) implements native software
coordination with a disabled driver and lazy journal. Statements below about no native hook
describe the UX8A server slice; physical integration and activation remain gated.

## Context

POS owns cashier custody and immutable manual-cash settlement projection. Local SQLite
deduplication cannot establish live permission or survive deletion/terminal replacement as
authoritative audit. Payment retry and receipt printing must never replay a physical action.
Hardware details are still unknown, so no vendor command may be guessed or enabled.

## Decision

UX8A implements default-off server authority in POS, with separate live manual-open,
cash-sale-open and own-intent read permissions. Restaurant/Authorization checks precede an
owning transaction; Domain validates current locked POS custody. Cash-sale requests require
the exact immutable cash projection within60seconds, preventing historical backfill/replay.
The first reservation, append-only audit and outbox commit atomically. One organization/Order
consumes a sale across operation IDs/terminals; manual operation facts are immutable.

Only the first response may grant submission within a fixed60-second database-clock window.
Retry/read never renew or recreate authority. A lost response is consumed uncertainty,
accepting a missed opening rather than risking another opening. A single append-only
client-reported Submitted/Unknown/Cancelled outcome records evidence without claiming a
physical opening. Original-actor reporting after custody closes can complete audit but never
authorizes another action. Live revocation/outage can leave consumed incomplete evidence.

No financial mutation, new broker consumer, native payment hook or driver is introduced.
UX8B must integrate the retained server grant, current-session/lock/expiry fences, local
duplicate suppression, explicit manual confirmation and one vendor-verified device call.
Neither a server grant nor a client outcome promises exactly-once physical execution.

## Consequences

POS24 adds three append-only tables and refuses rollback after history. Authorization16
backfills managers with all three permissions and cashiers with sale/read only; no realm
role, credential, cashier manual-open or accountant/Kitchen grant is added. Compatibility
is0.39.0. Existing profiles stay disabled. The narrow freshness window can deny slow/delayed
cash projection; it must not be bypassed with a fabricated timestamp or history replay.
External permission/hierarchy validation is check-time, not transactionally atomic with
PostgreSQL or a future device. Physical acceptance remains a separate gated slice.

See [API](../../API/Drawer-Intents.md), [persistence](../../Database/Drawer-Intents.md)
and [rollout/tests](../../Deployment/POS-UX8A-Drawer-Authority.md).
