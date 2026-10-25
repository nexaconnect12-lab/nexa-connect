# ADR-049 — isolated local QR pilot with a guest-only HTTPS edge

Status: accepted, 25 October 2026.

The user selected QR7 pilot activation/actual-device acceptance after QR6, then confirmed
that the current PC has no confirmed LAN hostname/certificate yet. Implement preparation
and prerequisites now. Do not open the actual LAN pilot or change certificate trust,
DNS/firewall, retained first-draft state or physical acceptance results automatically.

QR7 uses a fresh isolated first-draft profile, with an immutable exact guest HTTPS origin
selected at creation. Only this profile enables Restaurant entry, Order menu/requests/
acceptance/collection and BFF guest flags. Normal defaults and existing production/pilot
hosting artifacts stay unchanged. Restart/NoBuild require matching source and server-file
inventories; mutable Data Protection state is preserved separately from compiled identity.

The local-only ASP.NET Core edge binds one explicit RFC1918 IPv4 address, matches its
certificate SAN and host, and forwards a closed guest-only route/method set to a pinned
localhost HTTPS Customer BFF. The fixed public HTTP Host preserves BFF admission, while
backend TLS explicitly checks the separate localhost SAN, lifetime and original pin.
It strips staff bearer/tenant/session authority, forwards
only the QR guest cookie and exact mutation Origin, bounds bodies/replies, refuses
redirects/diagnostic failures, and uses shared safe correlation/JSON/optional OTLP.
Untrusted raw paths/queries are normalized before edge telemetry; no capability body or
arbitrary headers are logged. Staff/Kitchen/POS continue using protected loopback entry.

Directly exposing the Development BFF would also expose staff/control-plane navigation
and its development topology. Disabling TLS/Origin guards for phones would change the
guest boundary. The small closed edge preserves that boundary while leaving real server
hosting to the existing dedicated-server phase. It is not a production gateway or WAN
availability solution and refuses production host environments.

An optional short-lived test CA/leaf can be generated in an owner-restricted pilot
directory. The CA signing key is discarded; trust installation remains an explicit manual
device action with public fingerprint verification. Certificate/configuration/server/
edge/scope hashes bind write-once activation and append-only explicit operator attestations.
Missing facts remain blockers and every actual device case starts pending. Successful
source/loopback tests cannot imply physical-phone, WPF/PDF or release acceptance.

No financial authority, business state transition, owning schema or integration event
changes. Prior Phase 6–9 server/hardware/signing/CI/freeze gates and immutable original
cashier recovery remain applicable. See [QR7 operations](../../Deployment/QR7-Pilot.md).

The later [ADR-050](ADR-050-local-pilot-pos-storage.md) adds explicit same-Windows-account
POS test isolation and a reviewed same-scope QR source-refresh procedure. It preserves
this guest boundary, immutable prior activation/observations and normal-draft defaults.
