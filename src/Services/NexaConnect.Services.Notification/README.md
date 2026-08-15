# Notification Service

Notification owns queued message state, delivery-facing provider adapters, integration-event consumption, product audit records, and its PostgreSQL inbox/outbox. Other services request notifications through the versioned `NotificationRequestedV1` integration contract; they do not reference Notification domain or persistence models.

## API and tenant boundary

- `POST /api/notification/v1/notifications` accepts `OrganizationId`, `Channel`, `Recipient`, `Subject`, and `Body`. It requires an authenticated trusted workload or an active `nexa_connect` tenant with `notification.send`.
- `GET /api/notification/v1/notifications/{id}` requires `X-Nexa-Organization-Id`, `X-Nexa-Application-Code: nexa_connect`, and `notification.read`. PostgreSQL lookup is organization-leading so an ID cannot select another tenant's record.
- `GET /bff/customer/notifications/{id}` is the browser contract. Customer BFF derives both tenant headers from its protected tenant cookie and forwards the server-held access token.

Tenant access is revalidated through Platform Directory and Authorization. `tenant-admin` receives organization-scoped notification permissions. A `store-manager` permission remains restaurant-scoped and therefore does not satisfy this organization-only API.

## Durable integration mode

Set `Persistence__Provider=PostgreSQL`, `ConnectionStrings__Notification`, and secret-managed `Outbox__ConnectionString`. Migration 2 adds organization/source-event scoping, append-only notification audit records, a lease-based inbox, and the transactional outbox. A successful insert atomically records `notification.queued`, `NotificationQueuedV1`, and `PlatformAuditEventV1`. Repeated `NotificationRequestedV1` delivery is safe: the inbox deduplicates by event/consumer and `notifications.source_event_id` supplies a second durable uniqueness boundary.

Enable the RabbitMQ consumer with `NotificationConsumer__Enabled=true`. Optional settings are `NotificationConsumer__Exchange` (default `nexaconnect.events`), `NotificationConsumer__Queue` (default `nexaconnect.notification.requested.v1`), and `NotificationConsumer__Prefetch` (default `16`). Permanent contract/JSON failures dead-letter using `notification.requested.v1.dead`; transient failures are requeued. Deploy migration 2 before enabling the consumer. For rollback, stop producers, drain or stop the consumer/outbox dispatcher, downgrade Notification to migration 1, then downgrade the application.

The in-memory and HTTP-provider adapters remain development/integration options and do not provide the durable audit/outbox guarantee. Provider delivery is configured with `NotificationProvider__BaseUrl`; production retry, credential, rate-limit, and receipt reconciliation remain provider-specific work.

Structured logs and OTLP use service name `nexaconnect-notification`. Logs include event, organization, and correlation identifiers but never recipient, body, access tokens, or provider secrets. Useful Loki filters include `{service_name="nexaconnect-notification"}` and `|= "Notification event"`; alert on queue/DLQ depth, unpublished outbox age, failed attempts, and inbox lease age.
