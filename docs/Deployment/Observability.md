# Observability and centralized logging

## Implemented foundation

`NexaConnect.Observability` is the shared ASP.NET Core operational-telemetry library. Platform Directory and Platform Admin BFF are the first adopters. It provides:

- structured JSON logs on stdout in every environment;
- optional OTLP logs, traces, and metrics (the local stack stores logs only);
- service name, service version, and deployment environment resource attributes;
- ASP.NET Core and outbound HTTP tracing, plus runtime and HTTP metrics;
- validated `X-Correlation-ID` propagation and request completion/failure logs.
- anonymous `/health` process-liveness endpoints in the initial adopters; dependency readiness is future service-specific work.

The middleware records method, path, status, duration, correlation ID, and trace ID. It never records request or response bodies, query strings, authorization headers, cookies, tokens, secrets, or arbitrary headers. Application code must not attach payment data or unrestricted personal information to log scopes.

Operational telemetry is diagnostic and may be sampled, delayed, or unavailable. It does not replace service-owned audit records, authorization decisions, transactional outbox records, or other durable business evidence.

## Local stack

The Docker Compose stack runs:

- OpenTelemetry Collector on `127.0.0.1:4317` (gRPC) and `127.0.0.1:4318` (HTTP);
- Loki on `127.0.0.1:3100` with seven-day local retention;
- Grafana on `http://127.0.0.1:3000`, with Loki provisioned as its default data source.

Logs are stored in Loki and queryable through Grafana. The Collector sends traces and metrics to its `debug` exporter only, so those signals appear in Collector logs for pipeline verification but are not retained or queryable in Grafana. Add dedicated production trace and metric backends before relying on those signals operationally.

Copy `.env.example` to `.env`, set a strong `GRAFANA_ADMIN_PASSWORD`, and start the stack:

```powershell
docker compose up -d loki otel-collector grafana
```

Enable export for a locally launched service:

```powershell
$env:Observability__OtlpEnabled = 'true'
$env:Observability__OtlpEndpoint = 'http://localhost:4317'
```

Containers should use `http://otel-collector:4317` instead. Search in Grafana Explore by the `service_name` resource label, then narrow using correlation or trace identifiers.

## Failure behavior and production requirements

Console logging remains active when OTLP is disabled or the collector is temporarily unavailable. Export is batched and does not participate in request or business transactions. An invalid enabled endpoint fails during application startup to prevent silent misconfiguration.

The checked-in Loki/Grafana configuration is a single-node development foundation. Before production, define authenticated TLS ingestion, encrypted durable storage, per-environment retention, access control, backup requirements, capacity limits, alerting, and trace/metric production backends. Do not expose collector, Loki, or Grafana ports publicly.

## Adopting another ASP.NET Core service

Reference `NexaConnect.Observability`, call `builder.AddNexaConnectObservability("stable-service-name")` immediately after creating the builder, and call `app.UseNexaConnectRequestLogging()` before authentication and endpoint mapping. Add the standard `Observability` configuration section and verify console fallback, OTLP delivery, correlation propagation, and redaction expectations.
