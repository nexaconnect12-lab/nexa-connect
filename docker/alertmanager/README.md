# Alertmanager

Local alert routing UI on `127.0.0.1:9093`. The checked-in receiver intentionally sends no external notifications; it exists to validate Prometheus-to-Alertmanager routing without leaking development alerts. State persists in `alertmanager-data`.

Every release environment must configure an authenticated, reviewed on-call receiver and rehearse delivery before claiming production alerting. Never expose this unauthenticated local endpoint publicly. See [Observability](../../docs/Deployment/Observability.md).
