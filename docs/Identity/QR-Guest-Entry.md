# QR1 guest authority

QR4 staff acceptance uses live branch order.place/Directory and CSRF, not guest authority.
Only the original visitor can confirm a price-review snapshot via its existing cookie
and exact Origin. Private Restaurant context admits only exact Order workload, returns
no secret and performs no guest-ID capability recovery. See [QR4](../API/QR-Acceptance.md).


QR3 reuses the visitor capability for private submissions/status, with live Restaurant
validation even on replay. New scans never inherit previous visitor requests; end/rescan
does not cancel pending proposals. Staff queue/rejection use customer identity plus live
order.read/order.place at the exact branch, independently of guest authority; no new roles
or permissions. See [request boundary](../API/QR-Requests.md).


QR2 reuses this cookie/capability for [menu and quote reads](../API/QR-Menu.md). Exact
Origin/JSON is additionally required on quote POST. Order validates Restaurant visitor
context before/after dependencies, then uses its own existing exact workload for private
Catalog/pricing reads. Neither the browser's staff session nor staff tokens participate.
QR2 grants no guest staff role, permanent profile, submission/payment privilege or new
identity. QR3 separately grants visitor-scoped pending submission/status, never payment.

QR guests receive no Keycloak identity, staff role, permanent Customer profile or access
to existing authenticated financial APIs. Restaurant is the admission owner; read the
[v1 contract](../API/QR-Entry.md) and [ADR-044](../Architecture/Decisions/ADR-044-table-qr-entry-and-guest-sessions.md).
Staff administration retains live Directory membership and existing exact-branch
`restaurant.configuration.manage`. No role seed, realm mapper or new permission is added.

Printed QR secrets and independent visitor secrets have 256 random bits. Restaurant
stores only SHA-256 hashes. Printed entry is not a guest-session credential and neither
capability grants payment/order commands. The Customer BFF never borrows staff tokens
on a guest request; the guest cookie never authenticates CustomerSession. A visitor's
context read requires its own capability plus live entry version/expiry and hierarchy.

`__Host-nexa-qr-guest` requires HTTPS, Path=/ and no Domain; Secure/HttpOnly/Strict prevent
JavaScript reads and reduce ambient cross-site use. Exact configured Origin protects
cookie-backed mutations, JSON/body limits bound exchange, and no CORS enables third-party
reads. Printed tokens are fragment-only, removed before the guest component sends an
exchange; no referrer/analytics or persistent browser token store is used. A staff-issued
QR is intentionally a transferable public table-entry capability, not proof of physical
presence. Staff approval of any future request remains required.

Session lifetime is absolute, at most two hours; refresh reads it without renewal.
Rotation/revocation invalidate older sessions immediately on the next read. Stolen
capabilities require entry rotation/revocation; copying a guest link never grants a staff
session or access to other visitors. A rescan creates a new visit and cannot recover or
cancel earlier requests. [QR3](../API/QR-Requests.md) recovers private request status with
the original visitor cookie. QR1 admission itself stores no order work.

Production prerequisite: trusted reverse-proxy/HTTPS/host configuration and appropriate
ingress abuse controls. Do not trust arbitrary forwarded addresses or expose the private
Restaurant service. NAT users share one BFF address window; measured deployment capacity
and physical phone acceptance remain pending. Shared telemetry omits all credentials,
cookies, request bodies, labels, addresses and arbitrary headers.
