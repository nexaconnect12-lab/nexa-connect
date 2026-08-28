# Grafana

Local observability UI on port 3000 with Loki provisioned as the default data source and Prometheus as the metrics source. Set `GRAFANA_ADMIN_PASSWORD` in `.env`; startup fails if it is absent. State persists in `grafana-data`. The checked-in setup is local-only and must not be publicly exposed. See [Observability](../../docs/Deployment/Observability.md).
