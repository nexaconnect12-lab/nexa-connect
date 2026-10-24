# ADR-046 — durable QR guest requests before staff acceptance

Status: accepted for QR3, 23 October 2026.

Order owns guest requests separately from executable Orders. When QR3 is enabled, QR2
quotes gain a retained quote ID and an exact visitor/entry/owner/table-bound commercial
snapshot. Submitting `{quoteId,fingerprint}` consumes that quote once; its UUID is also
the request/idempotency identity. A matching replay returns the original request even
after quote expiry; changed fingerprints or visitors cannot reuse it. New submissions
require an unexpired quote. No browser prices or owner identifiers are accepted.

One pending request per visitor prevents a new reviewed quote from accidentally duplicating
an uncertain submission. Refresh reads the visitor's own retained requests using the
existing HttpOnly cookie; no browser credential storage. A rescan creates a different
visitor and never recovers another visit's work. Ending/rescanning does not cancel a
submitted request; guests must consult staff. Request authority always rechecks Restaurant
admission; revoked/expired visits cannot submit, replay or read private status.

An Order-owned Domain aggregate enforces pending/rejected/expired transitions and version
checks. Staff queue reads require live branch `order.read`; rejection requires live
`order.place`, protected organization and customer identity. No workload/guest bypass.
Fixed rejection categories avoid arbitrary guest/staff notes. Queue returns bounded
oldest pending work, with an explicit overflow indicator; there is no acceptance command.

Order migration20 adds quote/request/audit tables. Branch-scoped PostgreSQL transaction
advisory locking serializes admission, replay, quotas, expiry and rejection. No transaction
spans Restaurant/Catalog/Authorization HTTP. Request plus immutable audit and generic audit
outbox commit together. Expiry is evaluated on scoped access using database time and
persisted atomically; no worker or stock/payment/Kitchen command. Requests expire after
15 minutes or visitor expiry, whichever comes first. Limits:10 retained submissions per
visitor,30 per entry/hour,500 per branch/hour; pending caps1/visitor,10/entry,200/branch.
Unexpired quote caps10/visitor,100/entry,1,000/branch. Domain policies evaluate counts
under the branch lock. Reads/writes expire pending rows and prune terminal requests after
24 hours and unused expired quotes. Consumed quotes survive with the request; immutable
audit/outbox is retained under operational retention. Cleanup occurs on access, not a
guaranteed background deletion schedule. Replay retention exceeds the two-hour visit.

QR3 is separately default-off and requires PostgreSQL. QR2 without QR3 retains transient
quotes. Submitted requests are unpaid proposals, not executable Orders, Kitchen tickets
or Inventory reservations. QR4 must revalidate prices/availability, translate table
provenance and retain original workflow identity before acceptance. QR5 collects payment.
Hosted/physical-phone/release acceptance and retained Phase6–9 prerequisites remain open.

Retaining an owning quote UUID avoids trusting a browser-resubmitted commercial snapshot
or introducing an unrelated client-generated submission identity. Keeping a proposal
separate from an executable Order avoids premature placement and downstream work before
staff acceptance. Branch advisory serialization favors explicit bounded admission and
retry correctness over parallel writes within one branch; measured capacity remains a
deployment gate. Access-driven expiry avoids a new worker but cannot promise scheduled
cleanup of inactive branches. Later acceptance must extend this durable identity rather
than recreate the request as independent work.

QR4 extension: [ADR-047](ADR-047-atomic-qr-acceptance.md) implements that handoff
with original-request Order identity and explicit logical-table provenance. It adds
price_review/accepted transitions and excludes accepted requests/quotes from the
24-hour proposal cleanup described above. The original QR3 rationale remains unchanged.
