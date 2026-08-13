# Reporting service

This service owns tenant-scoped, eventually consistent read models for customer dashboards and sales reports. It never queries an operational service database. Apply Reporting migrations 1 and 2, then configure `ConnectionStrings__Reporting`, `Services__PlatformDirectory`, and `Services__Authorization`. The development profile uses `https://localhost:51226`.

The dashboard and sales endpoints require `branchId` and accept optional `fromUtc` and `toUtc`. They require exact organization access plus `reporting.dashboard.read` or `reporting.sales.read`. `latestGlobalCheckpointUpdatedAtUtc` is the newest global checkpoint update, not branch-specific freshness. Ranges default to 30 days, are capped at 366 days, and sales returns at most 1,000 newest rows. Invalid ranges return `400`, denials `403`, and dependency/database failures fail closed.

Verify with `dotnet build src/Services/NexaConnect.Services.Reporting/NexaConnect.Services.Reporting.csproj` and focused `ReportingQueriesTests`; apply Reporting migrations 1 and 2 before an authenticated smoke test.

Service name: `nexaconnect-reporting`. Query safe events `Customer reporting authorization denied` and `Customer reporting query failed`, correlated by `correlation_id`. Tokens and report bodies are not logged.
