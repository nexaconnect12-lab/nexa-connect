# Alertmanager

`scripts/test-payment-review-isolated.ps1` additionally runs the same rehearsal receiver/configuration in a generated project using dynamic loopback ports. It does not reset the root Compose alert containers. See [isolated acceptance infrastructure](../payment-review-acceptance/README.md).

Local alert routing UI on `127.0.0.1:9093`. The checked-in receiver intentionally sends no external notifications; it exists to validate Prometheus-to-Alertmanager routing without leaking development alerts. State persists in `alertmanager-data`.

Every release environment must configure an authenticated, reviewed on-call receiver and rehearse delivery before claiming production alerting. Never expose this unauthenticated local endpoint publicly. See [Observability](../../docs/Deployment/Observability.md).

The `alert-rehearsal` Compose profile starts a separate Alertmanager on `127.0.0.1:19093` and an in-memory webhook receiver on `127.0.0.1:19094`. `scripts/test-payment-capture-recovery-operations.ps1` and `scripts/test-payment-review-operations.ps1` reset and use this isolated profile to prove current-ID firing and resolved webhook delivery, then require removal of both containers. The latter submits a synthetic `OrderPaymentReviewStale` alert; it does not exercise Prometheus rule evaluation or threshold timing. The receiver normalizes only synthetic alert status, name, service, severity, and rehearsal ID and does not persist payloads. This is pipeline evidence, not production paging, acknowledgement, or escalation evidence.
