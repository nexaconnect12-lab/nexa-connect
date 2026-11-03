# ADR-064: Session-bound POS quick unlock and public customer panels

- Status: Accepted
- Date: 2026-11-03

## Context

Cashiers need a visible ready identity/status and quick resume without repeated password typing. The customer screen should show approved advertising beside welcome/order details. Original cashier custody, locked recovery, online authority and customer privacy must remain intact. No supported fingerprint/card hardware or enrollment protocol is known.

## Decision

Keep Keycloak OIDC/PKCE and authenticated UserInfo as cashier identity authority. Add optional explicit six-digit PIN enrollment only within two minutes of full sign-in. The verifier and suspended live token set exist only in process memory; ordinary token access and persisted storage expose the existing nonsecret lock marker while locked. Manual/idle locks alone may use quick resume, constrained by same operator, access validity, original absolute deadline and five-consecutive-failure limit. PIN cannot refresh credentials or bypass full sign-in for restart/expiry/security transitions. Full sign-in/sign-out/disposal clear the verifier. No reusable staff credential registry, offline sign-in authority or biometric/card implementation is introduced.

Retain existing scoped durable financial recovery and sign-out guard. Ready-card shortcuts navigate only existing workflows; cashier switching requires no retained work and full authentication. Customer presentation remains an immutable allow-listed projection: equal advertising/order columns, approved customer gallery with existing bounded lease, neutral/welcome public shop name outside active work, masked details on lock. It receives no staff identity, credentials, raw proof or command/recovery object.

## Consequences

Quick resume can expire before the absolute session deadline because the suspended access token is not renewed while locked. Session-only enrollment avoids persistent low-entropy credentials and migration/revocation complexity, but cashiers must re-enroll after full login. Device-verified biometric/card enrollment remains a separate hardware/security design. Public shop text is native configuration; other contexts retain their ownership and no service/API/schema boundary changes.

Alternatives rejected for this slice: a universal/shared PIN, storing raw or persistent PINs, treating an RFID UID as verified identity, retaining ordinary bearer access while locked, and exposing the staff window on the customer monitor. Physical display/reader acceptance remains separate from synthetic fixtures.

See [identity details](../../Identity/POS-Session-PIN.md) and [workflow/configuration](../../Deployment/POS-Cashier-Ready-And-Customer-Panels.md).
