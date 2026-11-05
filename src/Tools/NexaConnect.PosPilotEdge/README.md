# Physical POS staff pilot edge

Private Development/Testing transport tool for a separately owned fresh-data Windows LAN
pilot. See [onboarding](../../../docs/Deployment/POS-Physical-Device-Onboarding.md) and
[ADR-066](../../../docs/Architecture/Decisions/ADR-066-isolated-physical-pos-lan-pilot.md).
The [transport contract](../../../docs/API/Physical-POS-Pilot-Edge.md) records admitted
routes, forwarding/error semantics and recovery limits.

It binds one exact private IPv4 HTTPS origin; admits only the development/physical POS
addresses; and forwards one realm's public OIDC/login/resources and five business API
prefixes to fixed distinct loopback origins. No admin/master/internal/guest/metrics route,
database access, financial retry or hardware command is added. Owning APIs keep authorization.
The exact GET gallery capability preserves locked refresh without exposing other media reads.
Valid native terminal/client-operation GUID headers retain original recovery identity;
invalid values fail before forwarding. `strict-origin` referrer policy preserves browser
form Origin without disclosing login path/query; null and cross-origin requests stay denied.
Business requests preserve supplied nonempty terminal/client-operation GUID headers in
canonical form; malformed values fail before forwarding. The `strict-origin` referrer
policy preserves same-origin browser form Origin without exposing login paths/queries;
null/cross-origin requests remain denied. See the transport contract for details.

`scripts/physical-pos-pilot.ps1` prepares the plan and coordinates its fresh first-draft
stack; default existing stacks do not select this tool/Compose override. Cert/key and plan
hashes plus the full edge inventory are checked on restart. Hosted native profiles use the
same staff origin and canonical realm with existing callback/PKCE/manual-only policy.
Do not launch it against a retained normal/QR stack or edit an active identity origin.

Configuration is the `StaffPilot` record: `PublicOrigin`, `ListenAddress`, `DeviceAddress`,
`Realm`, `CertificatePath`, `PrivateKeyPath`, and six `Backends` (`identity`, `pos`, `order`,
`catalog`, `payment`, `media`). Real startup validates the exact SAN/server-auth TLS leaf,
private key and lifetime; normal clients validate chain trust. `Testing` uses TestServer.

Service `nexaconnect-pos-pilot-edge` adopts the shared observability foundation, structured
JSON and correlation propagation. It rejects `Observability:OtlpEnabled=true` before
exporter registration until outbound identity-URL and hosting-scope redaction are separately
verified; OTLP export is not currently supported by this private tool.
Fixed completion events include validated `CorrelationId`, `StatusCode` and, on upstream
completion, fixed `Upstream`. Hosting path scopes are excluded from console output and
inbound request telemetry uses `/staff-edge`. Never log/copy identity URLs, bodies, cookies, arbitrary
headers or provider data. Debug through `StaffEdge.log`/`.error.log`
and the safe correlation; logs are not a financial audit ledger.

Run the isolated `PosPilotEdgeTests` and onboarding/plan tests in the operating guide.
TestServer/mocked upstream passes do not certify actual Keycloak/browser/TLS/device behavior.
Private manual activation and full physical acceptance remain separate from source tests.
