# Customer product configuration, reporting, and media

All routes require an authenticated Customer session. The BFF derives `organizationId` from its protected active `nexa_connect` tenant selection, forwards the server-held bearer token, query/body, status, and JSON, and never accepts organization from browser input. Missing context returns `401`; owning-service denial returns `403`; dependency/database failures propagate.

## Product configuration

Restaurant owns `GET/PUT /api/restaurant/v1/customer/organizations/{organizationId}/configuration/branches/{branchId}`; the BFF surface is `/bff/customer/configuration/branches/{branchId}`. GET returns branch/restaurant/organization IDs, `dineInEnabled`, `takeawayEnabled`, `requireTableForDineIn`, `serviceChargePercent`, and `concurrencyVersion`. PUT accepts the four settings plus positive `expectedVersion`.

At least one service mode is required, table requirement needs dine-in, and service charge is 0–100. GET returns `404` for missing, cross-tenant, closed, or inactive hierarchy. Invalid PUT returns `400`; unavailable/cross-tenant/closed/stale writes collapse to `409`; success returns `200`. Lifecycle and configuration edits share the branch concurrency token. Writes append `branch.configuration.updated` transactionally.

## Reporting

Reporting owns dashboard and sales endpoints under `/api/reporting/v1/customer/organizations/{organizationId}`; BFF paths are `/bff/customer/dashboard` and `/bff/customer/reports/sales`. `branchId` is required. Optional `fromUtc`/`toUtc` form a half-open `[from,to)` range, default to 30 days, and cannot exceed 366 days. Invalid ranges return `400`; denial returns `403`. Currency detection scans the full selected range (including dashboard sales and payment facts), returns `409` rather than combining incompatible totals, and is independent of the 1,000-row response cap.

Dashboard returns completed orders, gross sales, net paid, refunded, currency, and `latestGlobalCheckpointUpdatedAtUtc`. Sales returns range, up to 1,000 items, the full-range completed-order total/currency, and the same checkpoint field; items are newest-first and capped at 1,000. The checkpoint is the newest global projector update, not proof the selected branch/range is current. Empty projections return zeros/empty items and null currency.

## Media metadata

Media writes are a hardened preview pending production acceptance. Upload completion requires the object provider's SHA-256, validates JPEG/PNG/WebP signatures, and scans the bounded object with ClamAV. Unsafe objects are quarantined and transactionally queued for deletion. Quotas, expired-upload cleanup, generated variants, and broader reconciliation remain required. Delete commits the soft-delete/audit/deletion job before asynchronous object removal, so stale versions cannot remove live bytes.

Media owns list, upload-start, completion, signed-download, and deletion routes; the BFF exposes only these explicit routes, derives organization from its tenant cookie, and bounds POST bodies to 16 KiB even when chunked. Start validates the Catalog product through a Media-only workload endpoint; unknown/cross-tenant products return `404` and dependency failure returns `503`. It accepts filename, content type, size, and lowercase SHA-256 and returns `201` with the asset, ten-minute PUT URL, and expiry. JPEG/PNG/WebP files are limited to 10 MiB. The PUT sends signed content type and `x-amz-checksum-sha256`; completion verifies provider-returned size/SHA-256, reads no more than the declared size, validates the file signature, and scans with ClamAV. Unsafe content is persisted as `quarantined`, cannot be downloaded, and completion returns `409`. Scanner/storage failure fails closed as `5xx` without making the asset ready. Download is ready-only. Delete requires `expectedVersion`. Reads require `media.asset.read`; mutations require `media.asset.manage`. Invalid requests return `400`, denial `403`, absence `404`, expired/stale/quarantined transitions `409`, and storage/dependency failures `5xx`. Upload quotas/expiry cleanup and variants remain staged; real MinIO and ClamAV acceptance tests are opt-in.

## Activity and audit history

Reporting owns `GET /api/reporting/v1/customer/organizations/{organizationId}/activity`; the BFF path is `/bff/customer/activity`. Optional exact-match `actorSubjectId` and `action` filters are supported. `limit` defaults to 50 and must be 1–200. `cursor` is an opaque continuation token; malformed cursors return `400`. Results are newest-first and return `{ items, nextCursor }`. Every item contains only event ID, organization/application, source service, actor subject, action, resource type/ID, outcome, occurrence time, and projection time.

The read requires `reporting.activity.read`, active organization access, and the active `nexa_connect` context. Platform Directory membership, Restaurant branch/configuration, and successful Media completion/delete mutations enqueue `PlatformAuditEventV1` with their PostgreSQL mutation/local audit; dispatch is asynchronous and conditional on publisher outbox configuration. Reporting consumes `*.audit.v1` with manual acknowledgement, durable inbox deduplication, and permanent-failure dead-lettering. The feed covers only these successful mutations from enablement onward and is not a complete compliance record.

Optional internal ingestion maps workload `azp` to one server-derived source and fixes application to `nexa_connect`. Repeated identical event IDs are idempotent; conflicting reuse fails. Action, resource, outcome, identifier lengths, and control characters are bounded, and arbitrary metadata is not accepted.
