# Customer product configuration, reporting, and media

All routes require an authenticated Customer session. The BFF derives `organizationId` from its protected active `nexa_connect` tenant selection, forwards the server-held bearer token, query/body, status, and JSON, and never accepts organization from browser input. Missing context returns `401`; owning-service denial returns `403`; dependency/database failures propagate.

## Product configuration

Restaurant owns `GET/PUT /api/restaurant/v1/customer/organizations/{organizationId}/configuration/branches/{branchId}`; the BFF surface is `/bff/customer/configuration/branches/{branchId}`. GET returns branch/restaurant/organization IDs, `dineInEnabled`, `takeawayEnabled`, `requireTableForDineIn`, `serviceChargePercent`, and `concurrencyVersion`. PUT accepts the four settings plus positive `expectedVersion`.

At least one service mode is required, table requirement needs dine-in, and service charge is 0–100. GET returns `404` for missing, cross-tenant, closed, or inactive hierarchy. Invalid PUT returns `400`; unavailable/cross-tenant/closed/stale writes collapse to `409`; success returns `200`. Lifecycle and configuration edits share the branch concurrency token. Writes append `branch.configuration.updated` transactionally.

## Reporting

Reporting owns dashboard and sales endpoints under `/api/reporting/v1/customer/organizations/{organizationId}`; BFF paths are `/bff/customer/dashboard` and `/bff/customer/reports/sales`. `branchId` is required to prevent mixed-currency aggregation. Optional `fromUtc`/`toUtc` form a half-open `[from,to)` range, default to 30 days, and cannot exceed 366 days. Invalid ranges return `400`; denial returns `403`.

Dashboard returns completed orders, gross sales, net paid, refunded, currency, and `latestGlobalCheckpointUpdatedAtUtc`. Sales returns range, items, total/currency, and the same checkpoint field; items are newest-first and capped at 1,000. The checkpoint is the newest global projector update, not proof the selected branch/range is current. Empty projections return zeros/empty items and null currency.

## Media metadata

Media owns `GET /api/media/v1/customer/organizations/{organizationId}/assets`; BFF path `/bff/customer/media`. It returns up to 500 non-deleted rows newest-first with asset ID, owner, filename/type/size, processing status/times, and version. Upload, signed download, deletion, variants, and workers remain staged in Media.

## Activity and audit history

Reporting owns `GET /api/reporting/v1/customer/organizations/{organizationId}/activity`; the BFF path is `/bff/customer/activity`. Optional exact-match `actorSubjectId` and `action` filters are supported. `limit` defaults to 50 and must be 1–200. `cursor` is an opaque continuation token; malformed cursors return `400`. Results are newest-first and return `{ items, nextCursor }`. Every item contains only event ID, organization/application, source service, actor subject, action, resource type/ID, outcome, occurrence time, and projection time.

The read requires `reporting.activity.read`, active organization access, and the active `nexa_connect` context. The projection is eventually consistent and rebuildable. `POST /api/reporting/v1/internal/activity-projection` is restricted to the an authenticated workload `azp` mapped server-side to one source service; it accepts the versioned `PlatformAuditEventV1`; application is fixed to `nexa_connect` and source is derived from the workload mapping. Repeated event IDs are idempotent. A RabbitMQ projector/consumer and production event publication from each owner remain follow-up work, so an empty page is expected until bounded audit events are delivered.
