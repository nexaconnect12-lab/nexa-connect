# Reporting service

This service owns tenant-scoped, eventually consistent read models for customer dashboards and sales reports. It never queries an operational service database. Apply Reporting migrations 1 and 2, then configure `ConnectionStrings__Reporting`, `Services__PlatformDirectory`, and `Services__Authorization`. The development profile uses `https://localhost:51226`.

The dashboard and sales endpoints require `branchId` and accept optional `fromUtc` and `toUtc`. They require exact organization access plus `reporting.dashboard.read` or `reporting.sales.read`. `latestGlobalCheckpointUpdatedAtUtc` is the newest global checkpoint update, not branch-specific freshness. Ranges default to 30 days, are capped at 366 days, and sales returns at most 1,000 newest rows while `totalSales` covers the full selected range. Invalid ranges return `400`, mixed-currency ranges return `409` without aggregating totals, denials return `403`, and dependency/database failures fail closed.

Verify with `dotnet build src/Services/NexaConnect.Services.Reporting/NexaConnect.Services.Reporting.csproj` and focused `ReportingQueriesTests`; apply Reporting migrations 1 and 2 before an authenticated smoke test.

Activity uses Reporting migration 3 and `reporting.activity.read`. Enable its durable consumer with `ActivityConsumer__Enabled=true` and secret-managed `ActivityConsumer__ConnectionString`; it binds durable queue `nexaconnect.reporting.activity.v1` to `*.audit.v1`, manually acknowledges, and deduplicates through `inbox_messages`. Platform Directory membership and Restaurant branch/configuration mutations enqueue into transactional outboxes when `Outbox__Enabled=true` and `Outbox__ConnectionString` is configured. Media has no mutation publisher yet, so this is not a complete compliance record.

Optional internal ingestion maps workload `azp` to one `ActivityProjection__Clients__{client-id}__SourceService`; application is server-fixed. Application and migration 3 enforce bounded vocabularies and identifiers. Operational events cover denial, accepted/duplicate projection, consumer success, and failure without logging payloads.

Service name: `nexaconnect-reporting`. Query safe events `Customer reporting authorization denied` and `Customer reporting query failed`, correlated by `correlation_id`. Tokens and report bodies are not logged.
