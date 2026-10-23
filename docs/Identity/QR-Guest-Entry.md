# QR1 guest authority

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
session or access to other visitors. A rescan creates a new visit, so submitted-order
recovery must be explicitly defined in QR3 before orders exist. QR1 stores no such work.

Production prerequisite: trusted reverse-proxy/HTTPS/host configuration and appropriate
ingress abuse controls. Do not trust arbitrary forwarded addresses or expose the private
Restaurant service. NAT users share one BFF address window; measured deployment capacity
and physical phone acceptance remain pending. Shared telemetry omits all credentials,
cookies, request bodies, labels, addresses and arbitrary headers.
