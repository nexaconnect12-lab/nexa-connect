# API Guidelines

Order, POS and Reporting expose anonymous `GET /health/live` returning only `{ "status": "Healthy" }`. These liveness routes do not certify databases, consumer delivery, tenant permissions or financial readiness. The [draft runbook](../Deployment/First-Draft-Test-Stack.md) describes separate initialization checks and the existing Payment readiness endpoint. Shared correlation/structured request telemetry applies; probes contain no credentials or financial response data.

- Use resource-oriented routes and plural nouns.
- Version externally consumed APIs.
- Return RFC 7807 Problem Details for application errors. Standard authentication challenges (`401`) and authorization denials (`403`) use framework bearer responses unless an API contract explicitly requires a problem body.
- Validate requests at the boundary.
- Support correlation IDs.
- Use idempotency keys for create-payment, submit-order, and POS synchronization operations.
- Publish OpenAPI documents from every HTTP service.

## Dedicated-server pilot edge

The [Phase 6 artifact tool](../../docker/pilot/README.md) generates separate HTTPS
Customer/Product Owner portal origins and POS, Order, Catalog, Payment and Media API
origins. Portal hosts forward their same-origin BFF/static routes. API hosts forward
only the matching `/api/<service>/` prefix; any path containing an `/internal` segment
and unmatched paths return `404`. Edge `/health`, `/metrics`, `/openapi` and `/swagger`
paths also return `404`; use private management access for operations.

The proxy verifies upstream TLS/SNI, sets the exact external Host, removes forwarded
headers and suppresses URL access logs. Existing service authentication, tenant and
operation authorization still apply. Generated routing introduces no business route,
DTO, permission or financial behavior. Target `nginx -t`, authenticated/denial checks
and real browser/media smoke tests remain required; anonymous probes certify only
their bounded public-edge observations. See the
[hosting guide](../Deployment/Next-Iteration-Phase-6.md) for deployment gates.
