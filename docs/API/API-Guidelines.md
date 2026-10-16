# API Guidelines

Order, POS and Reporting expose anonymous `GET /health/live` returning only `{ "status": "Healthy" }`. These liveness routes do not certify databases, consumer delivery, tenant permissions or financial readiness. The [draft runbook](../Deployment/First-Draft-Test-Stack.md) describes separate initialization checks and the existing Payment readiness endpoint. Shared correlation/structured request telemetry applies; probes contain no credentials or financial response data.

- Use resource-oriented routes and plural nouns.
- Version externally consumed APIs.
- Return RFC 7807 Problem Details for application errors. Standard authentication challenges (`401`) and authorization denials (`403`) use framework bearer responses unless an API contract explicitly requires a problem body.
- Validate requests at the boundary.
- Support correlation IDs.
- Use idempotency keys for create-payment, submit-order, and POS synchronization operations.
- Publish OpenAPI documents from every HTTP service.
