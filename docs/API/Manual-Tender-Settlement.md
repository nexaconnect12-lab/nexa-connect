# Manual tender settlement

`POST /api/order/v1/orders/{orderId}/manual-settlement` records Bangkok MVP cash or manually verified PromptPay settlement. It requires bearer authentication with a user subject, matching `X-Nexa-Organization-Id`, `X-Nexa-Application-Code: nexa_connect`, and branch-scoped `order.manual-payment.confirm` authorization.

The JSON request contains `organizationId`, `branchId`, `terminalId`, `idempotencyKey`, `method` (`cash` or `promptpay_manual`), exact Order `amount`, `currency` (`THB`), `receiptConfirmed`, optional `bankReference`, and optional `correlationId`. PromptPay requires `receiptConfirmed=true`; cash rejects a bank reference. Bank references never enter integration or audit events.

A first commit returns `201`; an identical replay returns `200` with `replayed=true` and the original settlement identity. Invalid input returns `400`, an undiscoverable Order returns `404`, denied access returns `403`, and reused idempotency, terminal state, provider binding, or concurrent settlement returns `409`. Order migration 5 and PostgreSQL persistence are required.
