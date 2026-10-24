# QR4 staff acceptance and guest price review

See [ADR047](../Architecture/Decisions/ADR-047-atomic-qr-acceptance.md),
[storage](../Database/QR-Acceptance.md) and [operations](../Deployment/QR4-Acceptance.md).
Existing QR1–QR3 cookie, scope, bounds, CSRF and tenant protections apply.

## Staff acceptance

POST `/api/order/v1/customer/organizations/{organization}/branches/{branch}/qr-requests/{id}/accept`;
BFF POST `/bff/customer/qr-requests/branches/{branch}/{id}/accept`.
Body `{expectedVersion}` only, max1KiB, unknown JSON fields rejected. Requires customer
identity, matching protected tenant and live exact branch `order.place` decision. BFF
also requires CustomerSession/current membership and `X-Nexa-CSRF`. Guest capabilities
and trusted workloads cannot accept. BFF CSRF response adds `acceptanceAvailable` for UI
discovery; it grants no business authority.

Only a live pending request at that version is eligible for acceptance. A price_review
request can use the same command only to refresh its guest review, never to bypass consent.
Order validates current
Restaurant visitor/entry version, expiry/ended state, owner hierarchy, label, THB and
dine-in policy. It resolves current organization-scoped Catalog availability/prices and
Restaurant tax/service policy through existing Order commercial rules. Availability
failure returns409 with no executable Order or downstream work. Exact quote comparison
includes policy version, names, unit prices, quantities, station and breakdown.

Changed commercial fingerprint returns200 with `status:price_review` and `reviewQuote`;
this is not acceptance. That snapshot expires within2minutes/original request deadline.
Only the same current guest may confirm it. Staff must reload and accept the new pending
version afterward; no automatic acceptance or changed charge.

Successful acceptance returns200 with `status:accepted`, `orderId` equal to original
request UUID and current `orderStatus`. In one local transaction, Order commits the
Submitted Order, request/version, immutable acceptance provenance and audit/outbox. Existing
recovery then reserves Inventory and creates Kitchen work using original Order identity.
No network call or inline broker publication occurs inside acceptance transaction.
An already accepted request replays its original Order after live staff authorization;
stale acceptance versions never create a replacement. Scope mismatch/invalid authority
does not receive another branch's result.

Public request views add optional `orderId,orderStatus,reviewQuote`. Request status
accepted means admitted, not Kitchen completion or payment. Order statuses include
submitted/inventory_reserved/kitchen_accepted/cancelled; existing rejected Orders map
to cancelled storage. Later paid status is observation only, never settlement authority.
The Order remains `cash_manual`, no Payment call/intent/receipt. QR5 owns cashier import.
Queue state now accepts pending/price_review/accepted/rejected/expired. Accepted requests
remain retained and do not expire or enter24hour QR3 cleanup.

## Guest price confirmation

POST `/api/order/v1/guest/qr/requests/{id}/confirm-price`;
BFF `/bff/guest/qr/requests/{id}/confirm-price`.
Body `{expectedVersion,fingerprint}` only, max1KiB. Requires the original live visitor;
BFF exact HTTPS Origin/JSON/cookie and shared admission counters remain mandatory. There
is no staff token forwarding. It atomically confirms the unexpired retained review quote,
advances version to pending, clears reviewQuote and appends immutable audit/outbox. Neither
request identity nor its15minute/visitor deadline changes. Wrong visitor/version/hash,
expired review, expired/revoked session fail closed. Response loss: recover private status,
then use its current version; repeated old confirmations conflict without another effect.

## Private Restaurant context

GET `/api/restaurant/v1/internal/organizations/{organization}/branches/{branch}/qr-visitors/{visitor}/entries/{entry}`
requires service-workload policy and exact `azp:nexaconnect-order-service`. Returns
`{context,dineInEnabled}` only for a live scoped visitor/entry. This is not an anonymous
visitor-ID lookup, capability recovery or staff bypass. Database predicates include owner,
branch, visitor, entry, current entry version/active state and non-ended session expiry.
Order uses its dedicated workload client, bounded4KiB/10s, redirects disabled; no guest
secret is persisted/forwarded. Restaurant remains private at ingress.

## Failure, concurrency and observability

Existing403/401/404/400/409/429/503 rules apply. Financial source fences/barriers preserve
existing409 codes and rollback acceptance. Dependency loss before handoff has no Order;
after commit the original durable recovery command remains. Competing accepts, rejection,
expiry and guest confirmation serialize on the Order-owned branch lock with version
checks. Accepted commands survive subsequent visit expiry/revocation and are not repriced
on recovery. Live service checks before commit are observations, not a distributed lock.

Existing Inventory order-key idempotency and Kitchen order/ticket identity plus fenced
Order recovery prevent repeated side effects after committed response loss. Definite
stock rejection closes the original Order without Kitchen; staff must investigate that
original result, never replay it as a replacement. Broker delivery may lag in the existing
transactional outbox; actual hosted broker interruption remains part of QR6 verification.

Services `nexaconnect-order`, `nexaconnect-restaurant`, `nexaconnect-customer-bff` retain
shared JSON/OTLP/correlation. Query `|= "QR"` or `Order workflow recovery` by validated
CorrelationId. Never log bodies, capabilities, cookies, prices, table labels or actors.
Request/review responses retain2MiB/15s BFF bound and12s owning request deadline. No new
grant, public direct service route, payment command or guest Order lookup is introduced.
