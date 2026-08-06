# Order Service

The Order bounded context owns order aggregates, line price snapshots, status transitions, and the first restaurant sales workflow.

`Application/Workflow/PlaceOrderWorkflow.cs` coordinates the bounded-context ports in this order:

`Catalog/Menu → Order → Inventory reservation → Kitchen ticket → Payment authorization`

The workflow publishes versioned contracts from `NexaConnect.Contracts.IntegrationEvents` after each accepted step. PostgreSQL mode persists the aggregate, immutable line snapshots, idempotency record, and integration event in one transaction; the shared outbox dispatcher delivers events to RabbitMQ. The public endpoint is `POST /api/order/v1/workflows/place` and requires an idempotency key. Outbound adapters attach a configured development token or obtain a short-lived Keycloak client-credentials token. Payment failure releases Inventory reservations and requests Kitchen cancellation through idempotent compensation hooks.

The service also exposes `POST` and `GET /api/order/v1/orders` for the aggregate slice, plus the public place-order workflow endpoint. Production provider credentials, migration execution, and a deployed Kitchen cancellation API remain operational follow-up work.
