# Observability and centralized logging

## Implemented foundation

`NexaConnect.Observability` is the shared ASP.NET Core operational-telemetry library. Platform Directory, Platform Admin BFF, Customer BFF, Catalog, Inventory, Order, Kitchen, Payment, Customer, Authorization, and Restaurant adopt it. It provides:

- structured JSON logs on stdout in every environment;
- optional OTLP logs, traces, and metrics (the local stack stores logs only);
- service name, service version, and deployment environment resource attributes;
- ASP.NET Core and outbound HTTP tracing, plus runtime and HTTP metrics;
- validated `X-Correlation-ID` propagation and request completion/failure logs.
- outbound propagation of the validated correlation identifier through registered Phase 4 HTTP clients;
- anonymous `/health` process-liveness endpoints in the initial adopters; dependency readiness is future service-specific work.

The middleware records method, path, status, duration, correlation ID, and trace ID. It never records request or response bodies, query strings, authorization headers, cookies, tokens, secrets, or arbitrary headers. Application code must not attach payment data or unrestricted personal information to log scopes.

Operational telemetry is diagnostic and may be sampled, delayed, or unavailable. It does not replace service-owned audit records, authorization decisions, transactional outbox records, or other durable business evidence.

## Local stack

The Docker Compose stack runs:

- OpenTelemetry Collector on `127.0.0.1:4317` (gRPC) and `127.0.0.1:4318` (HTTP);
- Loki on `127.0.0.1:3100` with seven-day local retention;
- Grafana on `http://127.0.0.1:3000`, with Loki provisioned as its default data source.

Logs are stored in Loki and queryable through Grafana. The Collector sends traces and metrics to its `debug` exporter only, so those signals appear in Collector logs for pipeline verification but are not retained or queryable in Grafana. Add dedicated production trace and metric backends before relying on those signals operationally.

For Phase 4 debugging, send a safe identifier such as `X-Correlation-ID: phase4-manual-001`, then query Grafana Explore with:

```logql
{service_name=~"nexaconnect-(customer-bff|catalog|inventory|order|kitchen|payment|customer|authorization|restaurant|platform-directory)"} | CorrelationId="phase4-manual-001"
```

Permission denials can be narrowed with `|= "Product permission"`. These events contain permission codes and UUID scopes, never subjects, tokens, customer profile data, payloads, or payment details.

Customer profile authorization can be narrowed with `{service_name="nexaconnect-customer"} |= "Customer authorization denied"`, `|= "Customer organization access lookup failed"`, or `|= "Customer organization access dependency failed"`, then correlated by `CorrelationId`. These events contain organization IDs, permission codes, and dependency status only; they exclude subjects, authorization values, and profile fields.

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

Centralized logging is mandatory for new HTTP services, BFF routes, and materially changed cross-service adapters. Attach `AddNexaConnectCorrelationPropagation()` to outbound `HttpClient` registrations that participate in a request chain. Background workers require equivalent structured JSON and OTLP telemetry; a reusable non-web host extension remains planned.
