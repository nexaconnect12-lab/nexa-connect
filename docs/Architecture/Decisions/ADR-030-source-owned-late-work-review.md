# ADR-030: Source-owned review of retained late financial work

POS late-cash posting is implemented separately by [ADR-031](ADR-031-verified-late-cash-corrections.md); the review behavior documented here remains unchanged. Other source corrections and fiscal/Reporting integration remain planned.

Status: Accepted and implemented. Local verification is recorded in [acceptance evidence](../Evidence/Late-Work-Review-Acceptance.md).

## Context

ADR-029 preserves late financial deliveries behind permanent source barriers. Managers need to inspect affected identities and record review decisions without changing the approved snapshot or accidentally applying retained messages.

## Decision

Order, Payment and POS each own their review aggregate and append-only `source_late_work_reviews`. Each existing custody record gains a stable opaque UUID without changing its payload, fingerprint, provider/broker identity or disposition. Review identity is organization + committed settlement barrier + work UUID: a delivery linked to several settled days has independent review history for each day. Source Domain policy advances review version and validates decision/reason pairs. Application enforces exact live hierarchy, source financial read, day read and manager review permission; Infrastructure owns bounded projections, parameterized SQL and transactions.

POS derives the source window and settlement UUID from its saved committed receipt/preparation. Its facade forwards the customer bearer to a selected allow-listed owner; it neither reads a foreign database nor uses its coordinator workload token for reviews. Each source checks live customer authority again, including immediately before appending and on exact retries. BFF uses protected session/tenant selection, live membership, server-held bearer, antiforgery and correlated no-store transport.

Version zero is `pending_review`. `investigate/investigate_delivery` produces `investigating`; `require_correction/correction_needed` produces `correction_required`; `acknowledge/evidence_checked` produces `reviewed`. Reviewed cases may be investigated again through a new append. None of these states certifies financial correction, releases a message, changes custody, advances financial revisions or edits a settlement receipt.

A source-local transaction serializes organization/operation and organization/settlement/work keys. A new decision requires the observed version; racing managers have one winner. Exact operation replay binds actor, complete command and scope, returns its original decision and fresh current detail, and never appends twice. Changed actor/body/scope under the same operation conflicts. Private actor, authorization decision UUID, fingerprint and review timestamp are retained for audit; public history omits actor and authorization metadata. Database guards prohibit history rewriting, require committed work membership and enforce consecutive versions and valid decision pairs.

Lists seek by received timestamp/opaque UUID with 1–50 results and a bounded cursor. These are live pages, not a certified financial snapshot; reload to see deliveries arriving before a cursor. Detail exposes only allow-listed UUID references, recognized event categories, custody reason, times, review state, up to 20 committed same-branch settlement references and the latest 20 decisions with history truncation flagged. The selected settlement is always included in its references. Provider identifiers, hashes, amounts, payloads, arbitrary error text and subjects stay private.

## Consequences

This adds one review table per source (Order 19 / Payment 18 / POS 19), stable custody metadata and Authorization 14 manager grants; minimum application compatibility is 0.33.0. Reporting remains 20. Existing histories and receipts stay immutable. Downgrade refuses retained custody or review rows because removing public work identity would break references. Deploy matching binaries and migrations together; least-privilege runtime roles need SELECT/INSERT on their own review table, never UPDATE/DELETE/TRUNCATE. Verified POS late-cash posting is implemented separately by ADR-031; other correction types and fiscal/Reporting integration remain planned.

See [API](../../API/Late-Work-Reviews.md) and [rollout](../../Deployment/Late-Work-Reviews.md).
