# Alertmanager

Local alert routing UI on `127.0.0.1:9093`. The checked-in receiver intentionally sends no external notifications; it exists to validate Prometheus-to-Alertmanager routing without leaking development alerts. State persists in `alertmanager-data`.

Every release environment must configure an authenticated, reviewed on-call receiver and rehearse delivery before claiming production alerting. Never expose this unauthenticated local endpoint publicly. See [Observability](../../docs/Deployment/Observability.md).

The `alert-rehearsal` Compose profile starts a separate Alertmanager on `127.0.0.1:19093` and an in-memory webhook receiver on `127.0.0.1:19094`. `scripts/test-payment-capture-recovery-operations.ps1` resets and uses this isolated profile to prove current-ID firing and resolved webhook delivery, then requires removal of both containers. The receiver normalizes only synthetic alert status, name, service, severity, and rehearsal ID and does not persist payloads. This is pipeline evidence, not production paging, acknowledgement, or escalation evidence.
