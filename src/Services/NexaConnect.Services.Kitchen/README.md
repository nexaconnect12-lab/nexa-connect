# NexaConnect Kitchen Service

Kitchen owns tenant-scoped preparation tickets and ticket-level lifecycle state. Only `nexaconnect-order-service` may create or compensate tickets; operators use tenant-authorized branch routes with `kitchen.ticket.read` or `kitchen.ticket.transition`.

- `POST /api/kitchen/v1/tickets` creates one station ticket.
- `POST /api/kitchen/v1/tickets/{orderId}/cancel?branchId=...` performs Order compensation.
- `GET /api/kitchen/v1/branches/{branchId}/tickets/{ticketId}` reads an authorized ticket.
- `POST /api/kitchen/v1/branches/{branchId}/tickets/{ticketId}/transitions` accepts target status, expected version, and optional bounded reason.

Legal transitions are queued → in-progress/cancelled, in-progress → ready/cancelled, and ready → completed/cancelled. A same-target request with the current expected version is a no-op; stale or illegal transitions return `409`. Completed/cancelled are terminal.

PostgreSQL mode requires migration 3. New writes atomically persist ticket/items, append-only status history and audit, a Kitchen lifecycle event, and `kitchen.audit.v1`. Migration 1 owns the outbox, migration 2 the inbox, and migration 3 tenant/fingerprint/audit state plus multi-station uniqueness. Enable dispatch only after Reporting migration 5 and its compatible consumer. Legacy rows use the empty organization UUID and require authoritative Order reconciliation.

Service name is `nexaconnect-kitchen`. All five coordinated Kitchen/Reporting acceptances passed locally against PostgreSQL 17 and RabbitMQ. Item-level workflows, station queues, adjustments, offline KDS, canonical station IDs, and established-dispatcher reconnection remain planned.

The service-specific production template is [`docs/Deployment/kitchen.production.env.example`](../../../docs/Deployment/kitchen.production.env.example). PostgreSQL deployment requires `ConnectionStrings__Kitchen`, `Persistence__Provider=PostgreSQL`, `Services__PlatformDirectory`, `Services__Restaurant`, `Services__Authorization`, and the `nexaconnect-kitchen-service` `WorkloadIdentity__Authority`, `WorkloadIdentity__ClientId`, and secret-managed `WorkloadIdentity__ClientSecret`. Authorization migration 3 must precede operator traffic. Enable outbox delivery with `Outbox__Enabled`, `Outbox__ConnectionString`, and the normal exchange settings only after Reporting migration 5. JSON stdout is always enabled; set `Observability__OtlpEnabled=true` and use the Kitchen-inclusive correlation query in the [observability guide](../../../docs/Deployment/Observability.md).
