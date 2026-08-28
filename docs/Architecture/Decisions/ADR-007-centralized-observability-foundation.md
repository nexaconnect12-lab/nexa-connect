# ADR-007: Centralized observability foundation

- Status: Accepted
- Date: 2026-08-11
- Updated: 2026-08-26 (local metrics retention and Payment readiness increment)

## Context

NexaConnect services had inconsistent console logging and no shared, searchable operational log pipeline. Phase 3 adds security-sensitive platform control-plane operations whose failures must be diagnosable across the BFF and service boundary. Operational telemetry must remain separate from immutable business audit records and must not expose credentials, payment data, or unrestricted personal information.

## Decision

ASP.NET Core services adopt the shared `NexaConnect.Observability` building block. It configures structured JSON console output, validated `X-Correlation-ID` propagation, safe request completion/failure logs, OpenTelemetry ASP.NET Core and HTTP instrumentation, runtime metrics, and optional OTLP export.

Console output is always enabled and OTLP export is opt-in. An enabled invalid endpoint fails at startup; collector delivery failure does not participate in request or business transactions. Platform Directory and Platform Admin BFF are the initial adopters and expose anonymous `/health` liveness endpoints.

The local observability topology sends logs from the OpenTelemetry Collector to Loki and exposes Collector metrics to Prometheus. Grafana provisions Loki and Prometheus data sources. Prometheus evaluates checked-in development alert rules and routes them to Alertmanager; the local receiver intentionally sends no external notifications. Loki and Prometheus are single-node development stores. Traces go only to the Collector debug exporter and are not retained or queryable in Grafana. Production deployments must select and operate appropriate durable telemetry backends and authenticated alert receivers.

Payment is the first service-specific readiness increment. It exposes separate anonymous `/health/live` and `/health/ready` endpoints. PostgreSQL readiness verifies connectivity and migration 7 or newer but excludes payment-provider availability so a provider incident does not induce orchestrator restart loops. Other services retain their existing liveness behavior until they define their own dependency-readiness contracts.

Logging code and enrichment must not capture request/response bodies, query strings, authorization headers, cookies, tokens, secrets, payment data, or unrestricted personal information. Operational telemetry is diagnostic and never replaces append-only audit trails, authorization decisions, transactional outbox records, or other durable business evidence.

## Consequences

- Services gain consistent correlation, resource attributes, instrumentation, and console fallback through two shared registration calls.
- Services can migrate incrementally without requiring the collector for startup.
- Local developers gain centralized log search and Payment metric inspection in Grafana, plus local Prometheus-to-Alertmanager rule-routing validation.
- The shared package becomes cross-cutting infrastructure and must remain free of service-specific business rules.
- Production requires authenticated TLS ingestion, encrypted durable storage, retention and access policies, capacity limits, reviewed external alert delivery, and a selected trace backend.
- Dependency readiness remains service-owned and must not expose sensitive details or couple process restart behavior to optional external providers.

## Alternatives considered

- Direct service-to-Loki logging was rejected because it couples applications to a backend and bypasses the vendor-neutral OTLP boundary.
- Console logs collected only by the container runtime were insufficient for a consistent local searchable experience and explicit correlation behavior.
- Requiring OTLP in every environment was rejected because telemetry infrastructure failure must not prevent service startup or local development.
- Treating operational logs as the platform audit ledger was rejected because logs can be sampled, delayed, expired, or unavailable.
