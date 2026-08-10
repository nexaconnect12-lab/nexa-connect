# Order Service

The Order bounded context owns order aggregates, line price snapshots, status transitions, and the first restaurant sales workflow.

`Application/Workflow/PlaceOrderWorkflow.cs` coordinates the bounded-context ports in this order:

`Catalog/Menu → Order → Inventory reservation → Kitchen ticket → Payment authorization`

The workflow publishes versioned contracts from `NexaConnect.Contracts.IntegrationEvents` after each accepted step. PostgreSQL mode persists the aggregate, immutable line snapshots, idempotency record, and integration event in one transaction; the shared outbox dispatcher delivers events to RabbitMQ. The public endpoint is `POST /api/order/v1/workflows/place` and requires an idempotency key. Outbound adapters attach a configured development token or obtain a short-lived Keycloak client-credentials token. Payment failure releases Inventory reservations and requests Kitchen cancellation through idempotent compensation hooks.

The service also exposes `POST` and `GET /api/order/v1/orders` for the aggregate slice, plus the public place-order workflow endpoint. Production provider credentials and migration execution remain operational follow-up work; the deployed Kitchen ticket create/cancel API is now part of the workflow boundary.

Customer Portal workflow requests are revalidated inside Order: Platform Directory confirms organization access using the customer bearer token, and Restaurant confirms that the requested branch belongs to that organization using the Order service workload identity. The BFF's tenant context is not treated as the final authorization decision. `OrderTenantAuthorizationTests` protects the deny-before-workflow behavior.
