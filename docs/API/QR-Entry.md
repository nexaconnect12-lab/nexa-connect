# QR1 table entry contract

QR1 admits individual visitors to a Restaurant-owned table context. It grants no staff,
menu/order, Inventory, Kitchen, payment or other guest-order access. All routes are opt-in
and no-store. See [ADR-044](../Architecture/Decisions/ADR-044-table-qr-entry-and-guest-sessions.md)
and [operations](../Deployment/QR1-Entry.md). API prefix version: v1; DTOs in
`NexaConnect.Contracts.RestaurantQr` are transport contracts, not shared domain entities.

## Staff administration

Restaurant prefix:
`/api/restaurant/v1/customer/organizations/{org}/branches/{branch}/qr-entries`.
Customer BFF prefix: `/bff/customer/qr-entries/branches/{branch}`. BFF organization comes
only from protected tenant selection matching the staff session. Every operation rechecks
live Directory membership and exact Restaurant hierarchy/configuration-manage authority.
Guest cookies or service-workload identity are not substitutes for staff authority.

| Method/suffix | Input | Success |
| --- | --- | --- |
| GET root | None | Up to 200 entry views |
| POST root | `{ "tableCode": "A1" }` | `{ entry, entryToken }` from Restaurant; `{ entry, entryUrl }` from BFF |
| POST `/{id}/rotate` | `{ "expectedVersion": 1 }` | New version and one-time replacement capability/link |
| POST `/{id}/revoke` | `{ "expectedVersion": 1 }` | Entry view with active=false and advanced version |

Entry view fields: `id, tableCode, version, active, expiresAtUtc`. Codes normalize to
uppercase ASCII letters/digits/hyphen/underscore, 1–32 characters; unique per branch.
They are staff-assigned ordering labels, not a floor-plan/table-account editor. Existing
`dining_tables`/`qr_context_id` records are not read or rewritten by QR1; future physical
table/order integration must explicitly translate/validate that model. Only live
organization/restaurant/branch ownership authorizes the entry.

Create is uniqueness-protected; a duplicate code, stale version, invalid owner or changed
branch returns conflict. Each successful mutation appends audit/outbox in its transaction.
Raw entry secrets are returned once; list/recovery does not disclose them. If a successful
issue response is lost, reload the version and deliberately rotate. Rotation can reissue a
revoked entry and permanently invalidates prior entry secrets and visitor versions.
Revoke does not cancel orders or financial records. Entries expire after 30 days.

BFF mutations additionally require CustomerSession and existing `X-Nexa-CSRF`, acquired
with GET `/bff/customer/qr-entries/csrf`. They use JSON bodies bounded to 1 KiB. JSON
responses are bounded to 64 KiB; Directory access to 256 KiB; downstream deadline 15s.
Dependency bodies/errors are sanitized. Staff issuance links are HTTPS `/order#<opaque>`;
do not send real links to external QR generators, analytics or support logs.

## Visitor boundary

Restaurant prefix `/api/restaurant/v1/guest/qr`; BFF prefix `/bff/guest/qr`:

| Method/suffix | Authority/input | Success |
| --- | --- | --- |
| POST `/sessions` | `{ "entryToken": "<43-char base64url>" }` | Restaurant-only `{sessionToken,context}`; BFF sets guest cookie and returns public context |
| GET `/context` | Restaurant `X-Nexa-Guest-Session`; BFF only its guest cookie | Current visitor's live context |
| DELETE `/sessions` | Same current visitor capability | 204; exact session ended, BFF cookie cleared |

Restaurant context binds `visitorId, entryId, organizationId, restaurantId, branchId,
restaurantName, branchName, tableCode, currency, expiresAtUtc`. BFF returns only visitor/
entry IDs, names, code, currency, expiry and `orderingAvailable:false`; it never sends
session secrets or internal owner IDs to browser JavaScript. Input IDs/query parameters
cannot override scope. Browser-supplied Authorization or guest-capability headers are not
forwarded by the guest adapter. No guest list/search/staff operation exists.

Each successful exchange creates a separate visitor, including a repeated scan in the
same browser; the newest cookie replaces that browser's entry context. QR1 holds no guest
orders. QR3 must define recovery/retention of submitted work before relying on rescan as
an order recovery mechanism. Refresh without a new scan reads the current cookie.

The BFF cookie is `__Host-nexa-qr-guest`, Secure, HttpOnly, SameSite=Strict, Path=/, no
Domain, absolute expiry at most two hours/entry expiry. It is separate from staff cookies
and is never registered as staff authentication. Cookie-backed POST/DELETE require exact
configured HTTPS Origin; POST also requires JSON, maximum 1 KiB. Guest GET has no
cross-origin read permission. Restaurant direct calls use explicit bearer capabilities,
not ambient cookies; keep Restaurant private. No guest OIDC/client/role is provisioned.

Every read rechecks hash, expiry, entry version/active state and current active restaurant/
branch. Missing/guessed/expired/revoked capabilities return 401 without disclosing which
check failed. Invalid syntax is 400 at the BFF; disabled routes return 404; exact-origin
failure 403; contention 409 for staff; guest quota 429 with Retry-After:60; unavailable dependency
503. Staff creation at the 200-entry branch cap returns 429 without a retry deadline.
No renewal/sliding session or offline authority exists. Ending one visitor does not
end another visitor's session.

## Abuse, telemetry and privacy

Restaurant serializes writes/admission on the owning branch row. Domain budgets limit
entries to 200/branch, admission to 30/entry/minute, 100 live sessions/entry and 1,000/
branch. Ending a visit marks its hash ended and denies later context reads; it retains the hash for rolling-minute accounting until later admission prunes it. Active-session counts exclude ended visits. Admission prunes expired/obsolete-version session hashes for that branch. BFF
transport: 60 requests/client address/minute, 600 global/minute, at most 4,096 client
buckets, cleared every minute. Restaurant transport: 1,000 global guest requests/minute.
These process-local transport counters reset on restart and do not replace shared SQL
budgets, a trusted ingress or measured public-internet abuse/capacity verification.

Shared JSON/OTLP service names remain `nexaconnect-restaurant` and
`nexaconnect-customer-bff`. Outbound Directory/Restaurant/Authorization calls preserve
validated correlation identifiers. Query `{service_name="nexaconnect-restaurant"} |= "QR"`
or `{service_name="nexaconnect-customer-bff"} |= "QR"`, then filter `CorrelationId`.
Only bounded operation/status/outcome labels are logged; no tokens, cookies, bodies,
table labels, IP addresses, arbitrary headers or financial data. QR responses/landing
use no-store and no-referrer. The landing also has a same-origin CSP and no staff boot.
