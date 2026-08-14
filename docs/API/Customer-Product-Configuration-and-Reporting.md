# Customer product configuration, reporting, and media

All routes require an authenticated Customer session. The BFF derives `organizationId` from its protected active `nexa_connect` tenant selection, forwards the server-held bearer token, query/body, status, and JSON, and never accepts organization from browser input. Missing context returns `401`; owning-service denial returns `403`; dependency/database failures propagate.

## Product configuration

Restaurant owns `GET/PUT /api/restaurant/v1/customer/organizations/{organizationId}/configuration/branches/{branchId}`; the BFF surface is `/bff/customer/configuration/branches/{branchId}`. GET returns branch/restaurant/organization IDs, `dineInEnabled`, `takeawayEnabled`, `requireTableForDineIn`, `serviceChargePercent`, and `concurrencyVersion`. PUT accepts the four settings plus positive `expectedVersion`.

At least one service mode is required, table requirement needs dine-in, and service charge is 0â€“100. GET returns `404` for missing, cross-tenant, closed, or inactive hierarchy. Invalid PUT returns `400`; unavailable/cross-tenant/closed/stale writes collapse to `409`; success returns `200`. Lifecycle and configuration edits share the branch concurrency token. Writes append `branch.configuration.updated` transactionally.

## Reporting

Reporting owns dashboard and sales endpoints under `/api/reporting/v1/customer/organizations/{organizationId}`; BFF paths are `/bff/customer/dashboard` and `/bff/customer/reports/sales`. `branchId` is required to prevent mixed-currency aggregation. Optional `fromUtc`/`toUtc` form a half-open `[from,to)` range, default to 30 days, and cannot exceed 366 days. Invalid ranges return `400`; denial returns `403`.

Dashboard returns completed orders, gross sales, net paid, refunded, currency, and `latestGlobalCheckpointUpdatedAtUtc`. Sales returns range, items, total/currency, and the same checkpoint field; items are newest-first and capped at 1,000. The checkpoint is the newest global projector update, not proof the selected branch/range is current. Empty projections return zeros/empty items and null currency.

## Media metadata

Media writes are a development preview and must not accept untrusted production content. SHA-256 is caller-declared object metadata, not a provider-verified digest. Owner validation, quarantine/scanning, quotas, cleanup, and cross-store reconciliation remain required. Delete commits the soft-delete/audit before object removal, so stale versions cannot remove live bytes but storage failure may leave an orphan.

Media owns list, upload-start, completion, signed-download, and deletion routes; the BFF exposes them below `/bff/customer/media` and derives organization from its tenant cookie. Start accepts Catalog product ownership, filename, content type, size, and lowercase SHA-256 and returns `201` with the asset, ten-minute PUT URL, and expiry. JPEG/PNG/WebP files are limited to 10 MiB. The PUT sends the signed content type and `x-amz-meta-sha256`; completion accepts `expectedVersion` and verifies size/checksum metadata. Download is ready-only and returns a five-minute URL. Delete requires `expectedVersion`. Reads require `media.asset.read`; mutations require `media.asset.manage`. Invalid requests return `400`, denial `403`, absence `404`, expired/stale transitions `409`, and storage/dependency failures `5xx`. Variants, malware scanning, owner validation, and orphan reconciliation remain staged.

## Activity and audit history

Reporting owns `GET /api/reporting/v1/customer/organizations/{organizationId}/activity`; the BFF path is `/bff/customer/activity`. Optional exact-match `actorSubjectId` and `action` filters are supported. `limit` defaults to 50 and must be 1â€“200. `cursor` is an opaque continuation token; malformed cursors return `400`. Results are newest-first and return `{ items, nextCursor }`. Every item contains only event ID, organization/application, source service, actor subject, action, resource type/ID, outcome, occurrence time, and projection time.

The read requires `reporting.activity.read`, active organization access, and the active `nexa_connect` context. Platform Directory membership and Restaurant branch/configuration mutations enqueue `PlatformAuditEventV1` atomically with their mutation and local audit; dispatch is asynchronous. Reporting consumes `*.audit.v1` with manual acknowledgement and durable inbox deduplication. Media mutation publication remains future work, so this is not a complete compliance record.

Optional internal ingestion maps workload `azp` to one server-derived source and fixes application to `nexa_connect`. Repeated identical event IDs are idempotent; conflicting reuse fails. Action, resource, outcome, identifier lengths, and control characters are bounded, and arbitrary metadata is not accepted.
