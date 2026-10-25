# Local QR pilot guest edge

QR9 provides a read-only exact live-leaf/default-Windows-trust diagnostic and bounded operator checklists without changing this edge. See [QR9](../../../docs/Deployment/QR9-Acceptance.md); actual Safari/Chrome trust remains an operator observation.

Local ASP.NET Core TLS transport for the current Windows test PC. It accepts only
Development or Testing environments; Testing supports the isolated test host. The shared
observability foundation provides JSON, validated correlation propagation and optional
OTLP. Only guest order assets and exact QR guest BFF operations cross to a pinned localhost
HTTPS backend. Its fixed public HTTP Host preserves BFF admission while TLS checks the
backend localhost SAN, lifetime and original pin independently. Staff/tenant authority,
arbitrary paths/queries, redirects and diagnostics
are denied. No business workflow, database or financial authority lives here.

Use [QR7 tooling](../../../docs/Deployment/QR7-Pilot.md), not ad hoc URLs or inherited
environment secrets. Configure `QrPilot` exact PublicOrigin, ListenAddress, BackendOrigin,
BackendCertificateSha256, CertificatePath and PrivateKeyPath. The launcher binds protected
source/artifacts/certificates/plan and process identity. Actual network/trust/device facts
remain prerequisites; the tool never imports CA trust or changes firewall/DNS.

Build with `dotnet build src/Tools/NexaConnect.QrPilotEdge`; verify HTTP/pinning with
`QrPilotEdgeTests` in Integration and the plan/certificate/lifecycle gates in
`scripts/test-qr7.ps1`. Real loopback TLS is distinct from pending LAN/phone/PDF acceptance.
See [API limits/telemetry](../../../docs/API/QR7-Pilot-Edge.md).
