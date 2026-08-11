# Loki

Single-node local operational-log store on port 3100. Data persists in the `loki-data` Docker volume and is retained for seven days. It has no authentication and is bound to loopback by Compose; it is not a production topology. See [Observability](../../docs/Deployment/Observability.md).
