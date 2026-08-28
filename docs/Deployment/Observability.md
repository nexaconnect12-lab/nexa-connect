# Observability and centralized logging

## Implemented foundation

`NexaConnect.Observability` is the shared ASP.NET Core operational-telemetry library. Platform Directory, Platform Admin BFF, Customer BFF, Catalog, Inventory, Order, Kitchen, Payment, Customer, POS, Authorization, and Restaurant adopt it. It provides:

- structured JSON logs on stdout in every environment;
- optional OTLP logs, traces, and metrics (the local stack stores logs and metrics);
- service name, service version, and deployment environment resource attributes;
- ASP.NET Core and outbound HTTP tracing, plus runtime and HTTP metrics;
- validated `X-Correlation-ID` propagation and request completion/failure logs.
- outbound propagation of the validated correlation identifier through registered Phase 4 HTTP clients;
- anonymous `/health` process-liveness endpoints in the initial adopters. Payment additionally exposes `/health/live` and `/health/ready`; PostgreSQL readiness requires a reachable database at migration 7 or newer and deliberately excludes provider availability. Dependency readiness remains service-specific work elsewhere.

The middleware records method, path, status, duration, correlation ID, and trace ID. It never records request or response bodies, query strings, authorization headers, cookies, tokens, secrets, or arbitrary headers. Application code must not attach payment data or unrestricted personal information to log scopes.

Operational telemetry is diagnostic and may be sampled, delayed, or unavailable. It does not replace service-owned audit records, authorization decisions, transactional outbox records, or other durable business evidence.

## Local stack

The default Docker Compose stack runs:

- OpenTelemetry Collector on `127.0.0.1:4317` (gRPC) and `127.0.0.1:4318` (HTTP);
- Loki on `127.0.0.1:3100` with seven-day local retention;
- Prometheus on `http://127.0.0.1:9090`, scraping the Collector's internal `:8889` Prometheus exporter and RabbitMQ's Prometheus endpoint;
- RabbitMQ Prometheus metrics on `http://127.0.0.1:15692/metrics`, bound to loopback for local development;
- Alertmanager on `http://127.0.0.1:9093`, with a local observation-only receiver that sends no notifications;
- Grafana on `http://127.0.0.1:3000`, with Loki as its default data source and Prometheus as its metrics source.

Logs are stored in Loki and queryable through Grafana. Metrics are retained locally in Prometheus and remain duplicated to the Collector `debug` exporter for pipeline verification. Traces reach only the debug exporter and are not retained or queryable in Grafana. The twelve checked-in Payment/Order rules evaluate recovery failures, `requires_action`, missing worker telemetry, stale capture/inbox/review work, unpublished outbox age, reconciliation queue/dead-letter depth, and PostgreSQL operational-metrics collection failures. The checked-in thresholds are development defaults rather than production SLOs, and local Alertmanager does not page anyone.

For Phase 4 debugging, send a safe identifier such as `X-Correlation-ID: phase4-manual-001`, then query Grafana Explore with:

```logql
{service_name=~"nexaconnect-(customer-bff|catalog|inventory|order|kitchen|payment|customer|authorization|restaurant|platform-directory)"} | CorrelationId="phase4-manual-001"
```

Permission denials can be narrowed with `|= "Product permission"`. These events contain permission codes and UUID scopes, never subjects, tokens, customer profile data, payloads, or payment details.

Customer profile authorization can be narrowed with `{service_name="nexaconnect-customer"} |= "Customer authorization denied"`, `|= "Customer organization access lookup failed"`, or `|= "Customer organization access dependency failed"`, then correlated by `CorrelationId`. These events contain organization IDs, permission codes, and dependency status only; they exclude subjects, authorization values, and profile fields.

POS offline cash replay can be narrowed with `{service_name="nexaconnect-pos"} |= "POS offline cash movement"`, then correlated by `CorrelationId`. The events contain cash-session, terminal, and client-operation UUIDs and the accepted/replayed/denied/conflict outcome; they exclude subjects, tokens, request bodies, reason codes, and cash values.

Copy `.env.example` to `.env`, set a strong `GRAFANA_ADMIN_PASSWORD`, and start the stack:

```powershell
docker compose up -d loki otel-collector alertmanager prometheus grafana
```

Enable export for a locally launched service:

```powershell
$env:Observability__OtlpEnabled = 'true'
$env:Observability__OtlpEndpoint = 'http://localhost:4317'
```

Containers should use `http://otel-collector:4317` instead. Search logs in Grafana Explore by the `service_name` resource label, then narrow using correlation or trace identifiers. For Payment metrics, select the Prometheus data source and query `payment_capture_recovery_failures_total`, `payment_capture_recovery_backlog`, `payment_capture_recovery_oldest_age_seconds`, `payment_outbox_unpublished`, `payment_outbox_oldest_age_seconds`, or `payment_operational_metrics_collection_failures_total`. Order exposes `order_payment_reconciliation_inbox_pending`, `order_payment_reconciliation_oldest_expired_lease_age_seconds`, `order_payment_review_open`, `order_payment_review_oldest_age_seconds`, equivalent outbox gauges, and `order_operational_metrics_collection_failures_total`. A collection failure means prior gauges may be stale. RabbitMQ supplies `rabbitmq_queue_messages` for the reconciliation and dead-letter queues. The Collector normalizes OTLP dots to underscores and adds `_total` to counters. The guarded payment-review rehearsal submits synthetic `OrderPaymentReviewStale` firing and resolved states through an isolated Alertmanager receiver; it validates routing plumbing, not Prometheus rule evaluation or a production paging destination.

## Failure behavior and production requirements

Console logging remains active when OTLP is disabled or the collector is temporarily unavailable. Export is batched and does not participate in request or business transactions. An invalid enabled endpoint fails during application startup to prevent silent misconfiguration.

The checked-in Loki/Grafana/Prometheus/Alertmanager configuration is a single-node development foundation. Prometheus persists local OTLP metrics exported by the Collector, and Alertmanager evaluates routing without an external receiver. Before production, define authenticated TLS ingestion, encrypted durable storage, per-environment retention, access control, backup requirements, capacity limits, reviewed alert routing, and a production trace backend. Do not expose collector, Loki, Prometheus, Alertmanager, or Grafana ports publicly.

For isolated delivery testing, the opt-in `alert-rehearsal` profile adds a second Alertmanager and an in-memory webhook receiver. Run it only through `scripts/test-payment-capture-recovery-operations.ps1`, which verifies a synthetic firing notification and its resolved notification before removing the containers. It does not alter the default observation-only receiver or validate production receiver authentication, paging, acknowledgement, or escalation.

## Adopting another ASP.NET Core service

Reference `NexaConnect.Observability`, call `builder.AddNexaConnectObservability("stable-service-name")` immediately after creating the builder, and call `app.UseNexaConnectRequestLogging()` before authentication and endpoint mapping. Add the standard `Observability` configuration section and verify console fallback, OTLP delivery, correlation propagation, and redaction expectations.

Centralized logging is mandatory for new HTTP services, BFF routes, and materially changed cross-service adapters. Attach `AddNexaConnectCorrelationPropagation()` to outbound `HttpClient` registrations that participate in a request chain. Background workers require equivalent structured JSON and OTLP telemetry; a reusable non-web host extension remains planned.
