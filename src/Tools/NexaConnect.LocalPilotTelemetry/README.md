# Local pilot host telemetry

Small local-tool logging adapter for [UX23](../../../docs/Deployment/POS-UX23-Background-Hosting.md).
Arguments are exactly phase, attempt count, QR status and generated D-format GUID; optional
fifth argument is a local HTTP(S) OTLP collector endpoint. Only allow-listed operational
enums/counters/correlation are logged. Invalid arguments return 2 without echoing them.

Uses `NexaConnectObservabilityExtensions.CreateClientLoggerFactory`, service
`nexaconnect-local-pilot-host`, logger `NexaConnect.LocalPilot.Host`; optional OTLP logging
is disabled by default. No web listener, database, identity, business API or recovery access.
The supervisor pins the published file inventory and rotates private JSON transition logs.
Search `Pilot host` with Phase/AttemptCount/QrStatus/CorrelationId; never add request data.

Build with `dotnet build src/Tools/NexaConnect.LocalPilotTelemetry`; unit
`PilotHostEventTests` guards event/correlation inputs. Installer publication is separate
from actual runtime/reboot/operator acceptance.
