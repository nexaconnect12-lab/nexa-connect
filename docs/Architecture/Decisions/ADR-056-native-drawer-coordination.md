# ADR-056: Native consumed attempts and financial-independent drawer coordination

Status: Accepted

## Context

UX8A supplies first-response-only server grants and authoritative audit. The native workflow
must retain a lost response as uncertainty, fence cashier/lock/custody and never turn a drawer
failure into payment failure. Hardware details remain unavailable; the user selected software
preparation with physical opening disabled.

## Decision

The native coordinator composes UX8A HTTP authority, a lazy separate scoped SQLite journal and
DisabledDrawerDriver. There is no runtime enable/profile/command path until a verified adapter
is supplied. Manual controls remain disabled. Tests inject fakes through explicit dependencies.

Reserve local intent before HTTP, accepting a missed opening rather than risking another on
response loss. A typed grant must match the original operation and reserved status, with a
maximum60-second lease. Check session/generation/custody/expiry immediately before one call.
The bounded cookie-free/redirect-disabled HTTP adapter uses the original current subject and
correlation; read/replay cannot grant. Outcome reporting is independently bounded and never
retries hardware. Failed report/local completion preserves consumed uncertainty. Local facts
do not replace authoritative server audit or prove physical opening.

A cash-only hook runs after authoritative confirmation/financial cleanup and only for a fresh
original response, excluding recovery/replay and other tenders. It retains the original amount,
Order/key and cashier/shift/cash session. Drawer failures remain separate from payment. Manual
requests require a predefined reason, explicit confirmation and live UX8A authority. Existing
customer display, financial schema, payment requests and backend contracts remain unchanged.

## Consequences

Subsequent [ADR-057](ADR-057-drawer-history-review.md) adds authorized read-only history:
an explicit review may open an existing journal even with the driver disabled. The no-I/O
statement below records UX8B startup/opening behavior; it does not prohibit those later reads.

No physical driver or production journal I/O is enabled; hardware UX8B remains incomplete.
Future activation must preserve the journal alongside financial recovery, supply matching UX8A
schema/binaries/configuration and verify equipment. No retry/reset/reconciliation-opening UI
is introduced. Denied/lost/expired attempts remain consumed; pending server/local audit can
need investigation. These gates intentionally prefer missed opening over unintended replay.

See [native preparation](../../Deployment/POS-UX8B-Native-Drawer.md) and
[server decision](ADR-055-drawer-authority.md).
