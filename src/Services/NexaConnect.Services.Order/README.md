# Order Service

The Order bounded context owns order aggregates, line price snapshots, status transitions, and the first restaurant sales workflow.

`Application/Workflow/PlaceOrderWorkflow.cs` coordinates the bounded-context ports in this order:

`Catalog/Menu → Order → Inventory reservation → Kitchen ticket → Payment authorization`

The workflow publishes versioned contracts from `NexaConnect.Contracts.IntegrationEvents` after each accepted step. PostgreSQL mode persists the aggregate, immutable line snapshots, idempotency record, and integration event in one transaction; the shared outbox dispatcher delivers events to RabbitMQ. The public endpoint is `POST /api/order/v1/workflows/place` and requires an idempotency key. Outbound adapters attach a configured development token or obtain a short-lived Keycloak client-credentials token. A provider timeout or unavailable response leaves the order `payment_pending` and retains Inventory/Kitchen work. When `PaymentReconciliationConsumer:Enabled=true`, the durable inbox consumer subscribes to authorization and capture reconciliation. Authorization alone never completes an order. A definitive `PaymentCaptureReconciledV1` captured result atomically marks it paid and publishes completion; definitive failure first performs idempotent Inventory/Kitchen compensation and then atomically records failure. Unknown and operator-review outcomes retain the pending state. Order migration 2 adds the pending-state constraint and durable inbox, and blocks downgrade while financial uncertainty remains.

The service also exposes `POST` and `GET /api/order/v1/orders` for the aggregate slice, plus the public place-order workflow endpoint. Production provider credentials and migration execution remain operational follow-up work; the deployed Kitchen ticket create/cancel API is now part of the workflow boundary.

Customer Portal create, read, and workflow requests are revalidated inside Order: Platform Directory confirms organization access, Restaurant confirms branch ownership, and Authorization evaluates `order.create`, `order.read`, or `order.place`. Request organization IDs must match the protected context. Cross-tenant reads return `404`; commands fail before the workflow executes.

Structured logs use service name `nexaconnect-order`; correlation IDs propagate through tenant checks and registered workflow HTTP adapters.
JSON stdout is always enabled. Enable OTLP with `Observability__OtlpEnabled=true`; use the [observability guide](../../../docs/Deployment/Observability.md) for the endpoint and queries.

In PostgreSQL mode, the `nexaconnect-order` meter reports pending payment-reconciliation inbox work, the oldest expired processing lease, unpublished outbox count/age, and gauge-collection failures. A collection failure means prior gauge samples may be stale. `OperationalMetrics:PollInterval` defaults to 30 seconds. The instruments contain no tenant, order, payment, or message labels. RabbitMQ queue and dead-letter depth are exported by the broker's Prometheus plugin in the local stack.
