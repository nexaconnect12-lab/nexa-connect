# Joined financial completeness acceptance

Local verification passed on 2026-10-03 at 05:24 UTC for the working tree based on revision `7a6523e3f2e30b24a5b2aa65c1f43bb09c3f0f11` (uncommitted source changes included). Run ID: `7f50d32adafa40b9ab84aea231f2b048`. The [runbook](../../Deployment/Financial-Completeness-Portal-Acceptance.md) defines prerequisites, commands and evidence paths.

The disposable stack exercised real Keycloak OIDC login, Customer BFF/Portal, Reporting, Authorization, Platform Directory and Restaurant scope metadata, independent PostgreSQL service databases, owning source repositories, actual RabbitMQ/outbox consumers and the existing operator recording CLI. All seven browser scenarios passed with zero skips/retries:

- Historical evidence progressed from Not checked to Gaps detected to Observed complete after retained sale/refund delivery and explicit operator recording.
- Branch and foreign-tenant access were denied and visible financial evidence cleared.
- Reporting transport failure cleared evidence and recovered after connectivity returned.
- Filter and tenant changes rejected delayed downstream responses.
- Accountant reads used identical windows and exposed no financial write route or controls.
- Revoked sales permission rejected an already authenticated accountant.
- Suspended organization membership rejected an already authenticated manager.

Six real Authorization persistence cases passed. The sanitized browser summary reports `verified: true`, `passed: 7`, `total: 7`; the runner reports `passed: true`, `authorizationPassed: true`, `cleanupVerified: true`, `productionVerified: false`. Generated processes, exact Compose project resources and certificate export were cleaned up. Machine-readable artifacts remain in ignored `.runstate/financial-portal/<run-id>/verification.json` and `src/Frontend/test-results/financial-completeness-live/<run-id>/summary.json`; CI uploads only these allow-listed summaries.

The joined run exposed a legitimate branch authorization defect: Reporting omitted the branch's restaurant ID when requesting a permission decision. Application authorization now resolves and validates authoritative branch ownership through Restaurant using a dedicated workload identity, then evaluates customer permission with the full organization/restaurant/branch scope. Its workload identity is admitted only by the branch-scope metadata policy, not the general service-workload policy. Nine focused tests cover hierarchy mismatches, membership denial, token separation and endpoint policy confinement.

Additional verification: the full solution built with zero warnings/errors; .NET tests passed 794 cases with 108 opt-in cases skipped; frontend type checking and 23 unit tests passed; four acceptance guard/evidence tests passed; realm validation and PowerShell syntax checks passed. Production preflight accepted a generated valid configuration and rejected missing, short and reused Reporting secrets. The live joined matrix itself had no skipped cases. The BFF build reported existing npm audit advisories and a large-bundle warning; this slice did not change package dependencies.

Fixtures use synthetic provider outcomes through owning repositories. Real provider funds, checkout/refund HTTP flows, cashier UI, production deployment, remote CI execution, global certification and settlement accounting are outside this evidence. A completeness observation remains historical evidence for an exact closed UTC window; later changes can invalidate it. Reads never run repair. Production rollout must provision the dedicated Reporting confidential client/secret and Restaurant configuration, including explicit updates to persisted Keycloak realms.

## Documentation alignment

The project overview and canonical project architecture were updated for the joined acceptance and scope-resolution dependency. Database documentation records disposable ownership without a production schema change. ADR-019 was reviewed and remains applicable without amendment. Updated/new documentation files in this change set:

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docker/financial-portal/README.md](../../../docker/financial-portal/README.md)
- [docker/keycloak/README.md](../../../docker/keycloak/README.md)
- [docs/API/Business-Service-API-Slices.md](../../API/Business-Service-API-Slices.md)
- [docs/API/Customer-Product-Configuration-and-Reporting.md](../../API/Customer-Product-Configuration-and-Reporting.md)
- [docs/API/Financial-Reporting-Completeness.md](../../API/Financial-Reporting-Completeness.md)
- [docs/Architecture/Evidence/Financial-Completeness-Joined-Acceptance.md](Financial-Completeness-Joined-Acceptance.md)
- [docs/Architecture/Portal-Implementation-Phases.md](../Portal-Implementation-Phases.md)
- [docs/Architecture/Project-Architecture.md](../Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../Database/Database-Design.md)
- [docs/Deployment/Deployment-Guide.md](../../Deployment/Deployment-Guide.md)
- [docs/Deployment/Financial-Completeness-Portal-Acceptance.md](../../Deployment/Financial-Completeness-Portal-Acceptance.md)
- [docs/Deployment/Financial-Reporting-Recovery.md](../../Deployment/Financial-Reporting-Recovery.md)
- [docs/Identity/Claims-Contract.md](../../Identity/Claims-Contract.md)
- [docs/Identity/Client-Matrix.md](../../Identity/Client-Matrix.md)
- [docs/Identity/Production-Runbook.md](../../Identity/Production-Runbook.md)
- [README.md](../../../README.md)
- [src/BuildingBlocks/NexaConnect.Infrastructure/README.md](../../../src/BuildingBlocks/NexaConnect.Infrastructure/README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/financial-completeness-live/README.md](../../../src/Frontend/e2e/financial-completeness-live/README.md)
- [src/Frontend/e2e/financial-completeness/README.md](../../../src/Frontend/e2e/financial-completeness/README.md)
- [src/Frontend/README.md](../../../src/Frontend/README.md)
- [src/Gateway/NexaConnect.CustomerBff/README.md](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Services/NexaConnect.Services.Restaurant/README.md](../../../src/Services/NexaConnect.Services.Restaurant/README.md)
- [src/Tools/NexaConnect.FinancialPortalAcceptance/README.md](../../../src/Tools/NexaConnect.FinancialPortalAcceptance/README.md)
- [src/Tools/NexaConnect.FinancialReportingRecovery/README.md](../../../src/Tools/NexaConnect.FinancialReportingRecovery/README.md)
