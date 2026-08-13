# Reporting service

This service owns tenant-scoped, eventually consistent read models for customer dashboards and sales reports. It never queries an operational service database. Apply Reporting migrations 1 and 2, then configure `ConnectionStrings__Reporting`, `Services__PlatformDirectory`, and `Services__Authorization`. The development profile uses `https://localhost:51226`.

The dashboard and sales endpoints require `branchId` and accept optional `fromUtc` and `toUtc`. They require exact organization access plus `reporting.dashboard.read` or `reporting.sales.read`. `latestGlobalCheckpointUpdatedAtUtc` is the newest global checkpoint update, not branch-specific freshness. Ranges default to 30 days, are capped at 366 days, and sales returns at most 1,000 newest rows. Invalid ranges return `400`, denials `403`, and dependency/database failures fail closed.

Verify with `dotnet build src/Services/NexaConnect.Services.Reporting/NexaConnect.Services.Reporting.csproj` and focused `ReportingQueriesTests`; apply Reporting migrations 1 and 2 before an authenticated smoke test.

Activity uses Reporting migration 3 and `reporting.activity.read`. The customer API provides actor/action filters and opaque cursor pagination. The internal idempotent projection endpoint accepts safe `PlatformAuditEventV1` records only from `system-admin` workload clients listed in `ActivityProjection__AllowedClients`. Service events are `Activity projection client denied` and `Activity projection failed`; correlate them by `correlation_id`. RabbitMQ consumption/publication remains staged, so operators must not interpret an empty projection as proof that no source activity occurred.

Service name: `nexaconnect-reporting`. Query safe events `Customer reporting authorization denied` and `Customer reporting query failed`, correlated by `correlation_id`. Tokens and report bodies are not logged.
