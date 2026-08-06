# Order Service

The Order bounded context owns order aggregates, line price snapshots, status transitions, and the first restaurant sales workflow.

`Application/Workflow/PlaceOrderWorkflow.cs` coordinates the bounded-context ports in this order:

`Catalog/Menu → Order → Inventory reservation → Kitchen ticket → Payment authorization`

The workflow publishes versioned contracts from `NexaConnect.Contracts.IntegrationEvents` after each accepted step. It does not reference another context's domain entities or tables. Infrastructure adapters are responsible for connecting the ports to the Catalog, Inventory, Kitchen, and Payment services and to the durable event transport.

The current tests use deterministic fakes to verify the complete orchestration and rejection behavior. Payment failure currently leaves the order in `PaymentFailed` after kitchen acceptance; compensating inventory release and kitchen cancellation are not yet implemented. RabbitMQ delivery, transactional outbox persistence, idempotency, and production service adapters are required before this workflow is exposed as a production endpoint.
