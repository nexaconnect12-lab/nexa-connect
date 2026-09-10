# Manual tender settlement

During placement, Order includes organization/application tenant headers on Inventory reservation and release calls. Trusted Inventory calls with both valid headers use tenant persistence; calls with neither retain legacy behavior and partial/invalid context is rejected. Inventory `5xx` responses raise a sanitized dependency failure rather than becoming persisted business rejection text. POS accepts a matching terminal `Rejected` or `PaymentFailed` placement result, including HTTP `409`, as permission to release its checkout lock; it does not unlock on arbitrary conflicts or intermediate states.

`POST /api/order/v1/orders/{orderId}/manual-settlement` records Bangkok MVP cash or manually verified PromptPay settlement. It requires bearer authentication with a user subject, matching `X-Nexa-Organization-Id`, `X-Nexa-Application-Code: nexa_connect`, and branch-scoped `order.manual-payment.confirm` authorization.

The JSON request contains `organizationId`, `branchId`, `terminalId`, `idempotencyKey`, `method` (`cash` or `promptpay_manual`), exact Order `amount`, `currency` (`THB`), `receiptConfirmed`, optional `bankReference`, and optional `correlationId`. PromptPay requires `receiptConfirmed=true`; cash rejects a bank reference. Bank references never enter integration or audit events.

A first commit returns `201`; an identical replay returns `200` with `replayed=true` and the original settlement identity. Invalid input returns `400`, an undiscoverable Order returns `404`, denied access returns `403`, and reused idempotency, terminal state, provider binding, or concurrent settlement returns `409`. Order migration 5 and PostgreSQL persistence are required.

The Order placement workflow accepts `cash_manual` and `promptpay_manual` only with `THB`. These modes complete Catalog, Inventory, and Kitchen work and return `KitchenAccepted` without creating or calling a Payment-provider intent. The cashier must then use the manual-settlement endpoint. They are checkout orchestration values; the settlement endpoint continues to accept only `cash` and `promptpay_manual`.

The WPF cashier persists an uncertain recovery record before its HTTP settlement attempt and clears it only after a successful response and local cleanup. It locks attempted tender fields and restores the saved method across restarts. This is a client recovery/UI change; the endpoint, ownership, authorization, and event contracts are unchanged. See [cashier acceptance](../Deployment/POS-Cashier-Acceptance.md).

POS placement now retains a protected original command before sending and reuses its OrderId as the placement idempotency/correlation identity. Its settlement identity is allocated with that command and retained through the response-to-payment transition. Endpoint contracts are unchanged; a replay returning an intermediate workflow state is held for reconciliation rather than replaced by a new order.
