# NexaConnect Observability

Shared operational telemetry for ASP.NET Core services. `AddNexaConnectObservability` configures structured JSON console logs, OpenTelemetry logs/traces/metrics, service/environment resource attributes, and optional OTLP export. `UseNexaConnectRequestLogging` adds safe request completion logs and propagates a validated `X-Correlation-ID`.

The library deliberately does not log request/response bodies, query strings, authorization headers, cookies, tokens, or arbitrary headers. Services must not add secrets, payment data, or unrestricted personal data as log properties.

Configuration:

```json
{
  "Observability": {
    "OtlpEnabled": true,
    "OtlpEndpoint": "http://localhost:4317",
    "ServiceVersion": "optional-release-version"
  }
}
```

Console logging always remains enabled. If OTLP is disabled, no collector is required. If enabled, the endpoint must be an absolute HTTP(S) URI; exporter delivery failures do not stop request handling. Operational logs are not an audit ledger and must not replace service-owned immutable business audit records.
