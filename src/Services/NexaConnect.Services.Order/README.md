# Order Service

The Order bounded context owns order aggregates, line price snapshots, status transitions, and the first restaurant sales workflow.

`Application/Workflow/PlaceOrderWorkflow.cs` coordinates the bounded-context ports in this order:

`Catalog/Menu → Order → Inventory reservation → Kitchen ticket → Payment authorization`

The workflow publishes versioned contracts from `NexaConnect.Contracts.IntegrationEvents` after each accepted step. It does not reference another context's domain entities or tables. Infrastructure adapters are responsible for connecting the ports to the Catalog, Inventory, Kitchen, and Payment services and to the durable event transport.

The service also exposes `POST` and `GET /api/order/v1/orders` for the initial order aggregate slice. The order workflow has a PostgreSQL-backed outbox publisher when `Persistence:Provider=PostgreSQL`, plus optional HTTP adapters for Catalog, Inventory, Kitchen, and Payment. Durable order aggregate persistence and wiring the workflow into the public endpoint remain required. Payment failure currently leaves the order in `PaymentFailed` after kitchen acceptance; compensating inventory release and kitchen cancellation are not yet implemented. RabbitMQ delivery, idempotency, and production Kitchen/provider adapters remain required before this workflow is production-ready.
