# Order Service

The Order bounded context owns order aggregates, line price snapshots, status transitions, and the first restaurant sales workflow.

`Application/Workflow/PlaceOrderWorkflow.cs` coordinates the bounded-context ports in this order:

`Catalog/Menu → Order → Inventory reservation → Kitchen ticket → Payment authorization`

The workflow publishes versioned contracts from `NexaConnect.Contracts.IntegrationEvents` after each accepted step. PostgreSQL mode persists the aggregate, immutable line snapshots, idempotency record, and integration event in one transaction; the shared outbox dispatcher delivers events to RabbitMQ. The public endpoint is `POST /api/order/v1/workflows/place` and requires an idempotency key. It does not reference another context's domain entities or tables. Infrastructure adapters are responsible for connecting the ports to the Catalog, Inventory, Kitchen, and Payment services. The current HTTP adapters are scaffold-only: they do not yet attach workload credentials, so protected cross-service calls require a credential/delegating-handler implementation before production use.

The service also exposes `POST` and `GET /api/order/v1/orders` for the aggregate slice, plus the public place-order workflow endpoint. Payment failure currently leaves the order in `PaymentFailed` after kitchen acceptance; compensating inventory release and kitchen cancellation are not yet implemented. Production provider credentials and operational compensations remain follow-up work.
