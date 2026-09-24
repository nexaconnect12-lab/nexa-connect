# Prometheus

`rules/pos-order-settlement.yaml` monitors the POS manual-tender durable queue, dead-letter queue, and retry telemetry. Backlog becomes critical after two minutes and any dead letter is critical. Receiver delivery remains release-environment evidence.

Local metrics retention and rule evaluation on `127.0.0.1:9090`. Prometheus scrapes the OpenTelemetry Collector's internal Compose endpoint and RabbitMQ's internal `:15692` endpoint, then loads checked-in rules from `rules/`. State persists in `prometheus-data`.

The eleven Payment/Order rules cover capture-recovery failures, terminal `requires_action`, missing worker telemetry, stale capture/inbox/outbox work, reconciliation queue/dead-letter depth, and operational-metrics collection failures. They are development defaults, not production SLOs. Validate changes with `promtool check config /etc/prometheus/prometheus.yaml` and `promtool check rules /etc/prometheus/rules/payment-capture-recovery.yaml` inside the container. Never expose this unauthenticated local endpoint publicly. See [Observability](../../docs/Deployment/Observability.md).

`rules/cash-close-reporting.yaml` adds six cash-close rules for outbox age, metric collection failure, consumer queue/dead letters, retries and publication failures. Export POS/Reporting meters through OTLP and supply RabbitMQ queue metrics; change queue selectors for custom queue names. Use `max` for database backlog gauges duplicated by replicas. Validate with `promtool check rules /etc/prometheus/rules/cash-close-reporting.yaml`; promtool/rule evaluation and live receiver delivery have not been verified in the current environment. See [runbook](../../docs/Deployment/Cash-Close-Recovery.md).
