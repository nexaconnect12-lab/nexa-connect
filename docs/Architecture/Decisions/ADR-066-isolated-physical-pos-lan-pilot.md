# ADR-066 — Isolated LAN pilot for physical Windows POS onboarding

Status: Accepted for private testing, 5 November 2026. Separate backend Ready/provisioned;
physical-device installation and acceptance pending.

## Context

The delivered Windows POS is a separate machine. Existing retained first-draft service URLs,
identity issuer and protected state are loopback/device-bound. Replacing localhost strings,
copying DPAPI material or sharing an active terminal identity is not a safe onboarding path.
The operator chooses a separate fresh-data LAN pilot on the development PC, preserving the
current normal/phone pilot. The initial device address was on a different /24 subnet;
the updated device report `192.168.0.165` matches measured host `192.168.0.191/24`.
The intended Windows account is operator-confirmed as PC. CA installation on both machines
is operator-confirmed, with normal Windows trust verified on the development PC only.
Actual physical-device connectivity/trust and sign-in remain separate verification gates.

## Decision

Reuse existing first-draft resource ownership/provisioning and private per-user EXE installer.
An explicit fresh-run plan selects a separate staff-only HTTPS edge and Keycloak Compose
override with one canonical external issuer; backend discovery retains normal TLS trust.
Only the two fixed private PCs and a directly connected same-subnet arrangement are supported
by the staff listener. An optional, separately bound guest HTTPS edge can reuse the same
physical pilot's loopback BFF and branch. It reuses the existing QR edge allowlist, rate
limits, certificate pin and correlation logging. The staff issuer and terminal custody
are preserved. The independent phone pilot retains its own listener and branch.

The edge is transport-only: bounded forwarding to fixed loopback services, restricted identity
paths, bearer gating plus the existing exact gallery-reader GET exception, safe correlation
and no command retry. Owning service rules and financial authority remain unchanged. Native
terminal/client-operation GUID headers preserve original recovery identity. Browser forms use
`strict-origin`, disclosing no referrer path/query while retaining the same-origin Origin
header; null/cross-origin requests remain rejected. This follows the documented
[referrer-policy effect on form Origin](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Referrer-Policy)
and the real pilot's reproduced no-referrer failure and successful strict-origin retest.
Public
proxy headers are overwritten. Keycloak configuration follows its official
[hostname](https://www.keycloak.org/server/hostname) and
[reverse-proxy](https://www.keycloak.org/server/reverseproxy) guidance. Admin/master paths
remain local. This is private Development tooling, not a production ingress architecture.
Shared JSON/correlation observability is retained, but the tool rejects OTLP export
configuration until outbound identity-URL and original hosting-scope redaction are separately
verified. Default-off alone is insufficient protection for identity traffic.

Generate immutable versioned onboarding folders from a matching source-bound installer and
new provisioned terminal scope. Export only canonical public certificate/configuration and
fixed file hashes; never transplant recovery or credentials. Verify the independently supplied
manifest hash, then its checker/CA hashes before running the checker or installing trust;
self-validation is not a trust bootstrap. Keep default manual tender,
existing PIN/session policy and disabled physical drawer adapter. A local Windows-compatible
read-only checker verifies trust/discovery/connectivity before the existing actual installer
preflight. Compatible updates require increasing versions and exact custody; no automatic
update, unattended startup, unsigned public distribution or arbitrary rollback is introduced.

## Consequences and gates

Fresh pilot records do not include the retained pilot's orders/menu changes/identities/guest
QR activity. New branch provisioning is explicit. No schema, business API or grant is added.
There is a new tool/service identity, optional local topology and public-profile delivery
artifact. Certificate issuance/trust, firewall scope, same-subnet networking, intended Windows
account and actual device/identity/update acceptance are prerequisites, not inferred passes.
Source tests and private compilation do not certify deployment or physical hardware.

See [the operating guide](../../Deployment/POS-Physical-Device-Onboarding.md) for commands,
recovery limits, supported routes, version control and verification.
