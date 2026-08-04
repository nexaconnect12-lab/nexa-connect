# API Guidelines

- Use resource-oriented routes and plural nouns.
- Version externally consumed APIs.
- Return RFC 7807 Problem Details for errors.
- Validate requests at the boundary.
- Support correlation IDs.
- Use idempotency keys for create-payment, submit-order, and POS synchronization operations.
- Publish OpenAPI documents from every HTTP service.
