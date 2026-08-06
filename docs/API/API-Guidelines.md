# API Guidelines

- Use resource-oriented routes and plural nouns.
- Version externally consumed APIs.
- Return RFC 7807 Problem Details for application errors. Standard authentication challenges (`401`) and authorization denials (`403`) use framework bearer responses unless an API contract explicitly requires a problem body.
- Validate requests at the boundary.
- Support correlation IDs.
- Use idempotency keys for create-payment, submit-order, and POS synchronization operations.
- Publish OpenAPI documents from every HTTP service.
