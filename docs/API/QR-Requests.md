# QR3 pending guest requests

QR4 extends these QR3 contracts with price_review/accepted states, original Order ID/
status, versioned staff acceptance and guest price confirmation. Current QR binaries
require Order21 even with acceptance off. QR3 alone retains proposal semantics below;
see [QR4 contract](QR-Acceptance.md).


See [ADR-046](../Architecture/Decisions/ADR-046-durable-qr-guest-requests.md),
[schema](../Database/QR-Requests.md) and [operations/verification](../Deployment/QR3-Requests.md).
QR3 submission alone adds no executable Order, Kitchen, stock or payment action.
QR4 acceptance and price-review extensions are documented separately above.

## Guest

When enabled, QR2 quote returns optional `quoteId`: the UUID of the retained exact quote.
Guest request input is only `{quoteId,fingerprint}` (max1 KiB, unknown fields rejected).
No request owner, table, prices, quantities or currency can override the retained snapshot.

| Method | BFF | Private Order | Result |
| --- | --- | --- | --- |
| POST | `/bff/guest/qr/requests` | `/api/order/v1/guest/qr/requests` | Original request view |
| GET | same | same | `{items,hasMore}` for current visitor only |

Existing QR1 cookie/HTTPS Host/Origin rules apply. POST requires exact configured Origin
and JSON. Private Order requires explicit session header and live Restaurant context even
for replay. BFF ignores inbound staff bearer, guest headers and query owner IDs. Context
adds `requestsAvailable`; `orderingAvailable` remains false. QR4 exposes staff acceptance
separately; neither marker grants guest Order or payment authority.

The quote UUID is also the request/idempotency UUID. First submission requires retained
unexpired quote and same visitor, entry, organization, restaurant, branch, table and
fingerprint. Replay checks exact ownership/payload before returning the original result;
it succeeds after quote expiry while the visit remains valid. A different fingerprint
cannot reuse that UUID. A different reviewed quote cannot create a second pending request
for that visitor. No automatic fresh identity is created after an uncertain response.

Request view: `id,tableCode,status,version,submittedAtUtc,deadlineUtc,reason,quote`. The
quote is the exact public QR2 commercial snapshot plus quoteId. No visitor/organization/
restaurant/branch IDs, actor, authorization decision, raw capability or station is exposed.
GET returns up to10 retained requests, newest first. No other visitor's status/search or
individual-ID read endpoint exists, including visitors at the same table.

Pending deadline=min(submitted+15minutes,visitor expiry). QR3 proposal states are `pending`, `rejected`,
`expired`; QR4 adds `price_review` and `accepted`. Version starts1 and advances on transition. Expiry is applied durably
on scoped access using database time, not a worker schedule. Ending/revoking/rescanning
does not cancel pending work. Expired/revoked visitors lose read/replay/submit authority.
A rescan creates a new visitor and cannot recover old requests; staff can still review
the branch queue. Refresh with the original cookie recovers its own requests.

Current reviewed prices may change after submission. QR3 stores guest consent to the
retained quote and performs no new financial action. QR4 freshly revalidates and
obtains review of changes before acceptance; pending is never a price/stock/payment promise.

## Staff

Order prefix `/api/order/v1/customer/organizations/{organization}/branches/{branch}/qr-requests`.
BFF prefix `/bff/customer/qr-requests/branches/{branch}`. BFF organization comes only from
protected staff tenant selection matching the current subject. All owning calls require
customer roles, matching tenant headers, live Directory/active branch authorization and
real nonempty Authorization decision. No workload or guest bypass.

GET `?state=pending|price_review|accepted|rejected|expired` requires `order.read`, returns at most10 views and
`hasMore`. Pending work is oldest first; terminal work newest first. When more pending
work exists, resolve displayed requests then reload. This is a bounded queue, not full
historical pagination. POST `/{id}/reject` requires `order.place` and
`{expectedVersion,reason}`; reason is `unavailable|closing|staff_review`. It transitions
only live pending/price_review work with the exact version. Staff mutation also requires existing
`X-Nexa-CSRF`, obtained at `/bff/customer/qr-requests/csrf`. No arbitrary notes or refund.
Operational branch discovery `qr-requests` maps to existing `order.read`; it grants no
new permission. Customer Portal `/#qr-requests` presents the queue/rejection controls.

## Limits, failures and telemetry

Domain limits inside the branch-serialized transaction:10 retained submissions/visitor,
30/entry/hour,500/branch/hour; pending1/visitor,10/entry,200/branch. Quotes10 live/visitor,
100/entry,1000/branch. Only actual submissions consume submission quotas; replay does not.
Unused expired quotes are pruned; rejected/expired requests and their consumed quotes are
eligible for pruning24hours after submission. Accepted requests/quotes remain retained.
Pending counts include price_review; accepted rows count toward retained submissions. Cleanup occurs on branch access; no background timing
guarantee. Immutable audit/outbox retention remains operational policy.

All routes no-store, bounded JSON and shared correlation. New request/review adapter
responses max2 MiB; existing menu/quote limits stay512 KiB. Guest POST max1 KiB, quote8 KiB;
guest transport counters shared with QR1/2. Order12s and BFF15s deadlines apply. Flags
disabled404; missing/invalid/revoked guest401; staff/origin denial403; malformed400;
quote/identity/pending/version conflict409; quota429 (Retry-After60 is a minimum retry hint,
not proof an hourly limit has reset); missing scoped staff request404; dependency503.
BFF discards downstream error bodies. Staff retries after uncertainty must reload/version
check; a repeated rejection of an already terminal request returns409 without another audit.

Request transition, append-only audit and generic `order.audit.v1` outbox commit atomically.
These audit messages are not sale/placement/Kitchen events. No network call occurs inside
the persistence transaction. Admission authorization is a pre-commit observation; there
is no distributed lock across Restaurant/Authorization/Order. QR4 acceptance rechecks.
Service names remain `nexaconnect-order`, `nexaconnect-customer-bff` and
`nexaconnect-restaurant` for branch discovery. Query `|= "QR"` with validated CorrelationId.
Never log tokens/cookies/bodies, snapshots/prices, table labels, actors or arbitrary headers.
