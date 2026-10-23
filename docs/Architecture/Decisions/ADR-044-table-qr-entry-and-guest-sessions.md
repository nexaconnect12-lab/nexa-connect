# ADR-044 — table QR entry and individual guest sessions

Status: accepted for QR1, 23 October 2026.

Restaurant owns revocable table-entry records and visitor admission, independently of
Ordering's future guest requests. A table code is an ordering context, not a floor plan,
open table account, customer profile or proof that a visitor is physically present.
QR1 exposes context only. Menu/cart, submissions, approval, Kitchen and cashier collection
remain QR2–QR5. Existing staff authorization and financial APIs are unchanged.

Restaurant owns this context because it already owns the branch hierarchy and operating
configuration. Requiring Customer/Keycloak registration would contradict guest entry;
sharing one visitor credential across a table would prevent isolation of future private
requests. Separate visitor capabilities preserve that boundary. Reusing existing floor
records now would imply physical-table lifecycle rules that are not yet implemented;
QR1 therefore uses staff-assigned ordering labels and requires explicit translation when
later slices integrate tables and orders.

Staff with live `restaurant.configuration.manage` at the exact active owner/branch can
create, list, rotate and revoke entries. Mutations use an expected version, append-only
audit and transactional audit outbox. The persisted branch determines the restaurant;
guest inputs never select organization/branch/table. One normalized code per branch is
retained. Entry secrets are 256 random bits, issued once, expiring after 30 days. Rotation
issues a new secret/version; revocation increments the version. Secrets are SHA-256 hashed
in Restaurant storage and never included in lists/audit/events/logs. A lost issue response
requires a deliberate versioned rotation, not a secret recovery endpoint.

Each scan exchanges the opaque entry for an independent 256-bit visitor capability with
a two-hour absolute lifetime, bounded by entry expiry. Restaurant stores its hash and
entry version; every context read rechecks active organization ownership, restaurant,
branch, entry version/revocation/expiry and session expiry. No cache extends authority.
Anonymous routes require capabilities, never staff identity. A capability is transferable
if stolen: HTTPS, no referrers/analytics, short expiry and revocation reduce that risk;
scanning does not authenticate physical location. Staff acceptance remains mandatory.

Customer BFF is the same-origin browser adapter. It strips entry fragments from `/order`
before exchanging them and keeps visitor capabilities in a Secure, HttpOnly, SameSite=Strict
host-only cookie separate from staff cookies. It forwards no staff bearer on guest calls.
Guest mutations require an exact configured HTTPS Origin; session exchange also requires
JSON. Ambient staff sessions
grant no guest/staff crossover. Guest responses are allow-listed public context; session
secrets remain server-to-server. No CORS or native app is added. Both hosts are opt-in,
disabled by default. Restaurant remains a private downstream in deployment.

Transport admission uses bounded process-local global/IP windows at the BFF and a bounded
global window at Restaurant. Database branch-row serialization enforces cross-instance
limits: 30 starts per entry/minute, 100 live sessions per entry, 1,000 per branch; expired
and obsolete-version sessions are removed on admission. Logout ends authority immediately;
ended hashes preserve the rolling-minute admission count. Later admission prunes them once
older than that window. Active-session counts exclude ended visits. Counts are evaluated by Domain
policy inside the owning transaction. These are initial availability safeguards, not a
public-internet abuse certification; a trusted ingress/WAF and actual capacity test remain
deployment gates. Proxy headers must not be trusted from arbitrary clients.

Restaurant migration 4 adds entries, sessions and an append-only QR administration audit.
No financial table or existing branch configuration is rewritten. Downgrade destroys
these admission records and is an explicit destructive migration while QR is disabled.
Restored secret hashes may revive old printed codes; rotate entries after a restore and
discard sessions before reopening guest traffic. New binaries/migrations need a new
release candidate; the retained Phase 8 candidate cannot certify these changes.

Use shared JSON/OTLP and validated correlation propagation for both existing service names
`nexaconnect-restaurant` and `nexaconnect-customer-bff`. Only bounded operation/outcome labels
and scoped UUIDs belong in events; never tokens, cookies, bodies, labels, IPs or headers.
See [QR1 contract](../../API/QR-Entry.md) and [operations](../../Deployment/QR1-Entry.md).
