# Manual tender settlement

`POST /api/order/v1/orders/{orderId}/manual-settlement` records Bangkok MVP cash or manually verified PromptPay settlement. It requires bearer authentication with a user subject, matching `X-Nexa-Organization-Id`, `X-Nexa-Application-Code: nexa_connect`, and branch-scoped `order.manual-payment.confirm` authorization.

The JSON request contains `organizationId`, `branchId`, `terminalId`, `idempotencyKey`, `method` (`cash` or `promptpay_manual`), exact Order `amount`, `currency` (`THB`), `receiptConfirmed`, optional `bankReference`, and optional `correlationId`. PromptPay requires `receiptConfirmed=true`; cash rejects a bank reference. Bank references never enter integration or audit events.

A first commit returns `201`; an identical replay returns `200` with `replayed=true` and the original settlement identity. Invalid input returns `400`, an undiscoverable Order returns `404`, denied access returns `403`, and reused idempotency, terminal state, provider binding, or concurrent settlement returns `409`. Order migration 5 and PostgreSQL persistence are required.

The Order placement workflow accepts `cash_manual` and `promptpay_manual` only with `THB`. These modes complete Catalog, Inventory, and Kitchen work and return `KitchenAccepted` without creating or calling a Payment-provider intent. The cashier must then use the manual-settlement endpoint. They are checkout orchestration values; the settlement endpoint continues to accept only `cash` and `promptpay_manual`.

The WPF cashier persists an uncertain recovery record before its HTTP settlement attempt and clears it only after a successful response and local cleanup. It locks attempted tender fields and restores the saved method across restarts. This is a client recovery/UI change; the endpoint, ownership, authorization, and event contracts are unchanged. See [cashier acceptance](../Deployment/POS-Cashier-Acceptance.md).
