# OpenTelemetry Collector

Local OTLP ingress for NexaConnect on ports 4317 (gRPC) and 4318 (HTTP). Logs are batched and exported to Loki. Traces and metrics use the debug exporter only and are not retained. This development configuration has no ingestion authentication or TLS; never expose it publicly. Production requirements are documented in [Observability](../../docs/Deployment/Observability.md).
