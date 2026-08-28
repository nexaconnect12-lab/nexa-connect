# OpenTelemetry Collector

Local OTLP ingress for NexaConnect on ports 4317 (gRPC) and 4318 (HTTP). Logs are batched and exported to Loki. Metrics are exposed to the Compose Prometheus service and also reach the debug exporter; traces remain debug-only. This development configuration has no ingestion authentication or TLS; never expose it publicly. Production requirements are documented in [Observability](../../docs/Deployment/Observability.md).
