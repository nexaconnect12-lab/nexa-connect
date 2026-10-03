# Hosted refund acceptance evidence

Local simulator verification passed on 2026-10-03 at 06:34 UTC. Run ID `43781f0df85f4f0cb2ad682429dd7201` exercised the working tree based on revision `fd41e1a28ac6ceaffa02762d85d5cb5b5153a7d2`, with source changes uncommitted. See the [runbook](../../Deployment/Hosted-Refund-Acceptance.md) for the exact seventeen-case simulator and fifteen-case separate Omise boundaries.

All **17/17 hosted cases** passed without skips/retries, plus **6/6 real Authorization persistence cases**. The actual stack used generated Keycloak/PostgreSQL/RabbitMQ, five auxiliary owning HTTP hosts, the restarted Payment host and the HTTPS provider simulator. The matrix verified approval-limit denial, tenant/branch isolation, partial/full receipts, operation replay/conflicts, concurrent over-refund protection, provider response-loss recovery with one command and status lookups, failed-reservation release, exhausted uncertainty reservation, receipt immutability, Paid Order preservation, retained source/audit agreement, real financial consumption, branch totals and live permission/membership revocation.

Five THB 100 fixture captures generated five receipt-backed sales. Completed refunds totaled THB 195; the additional uncertain THB 20 remained reserved for review and was not a completed refund fact. Reporting's allowed branch showed refunds THB 135/net sales THB 265; the second branch showed refunds THB 60/net sales THB 40. Capture/Order fixture setup used owning Application/Infrastructure repositories and the real provider adapter; checkout HTTP, Inventory and Kitchen orchestration were not exercised.

The sanitized matrix reports `verified=true`, `passed=17`, `total=17`, `externalProviderVerified=false`. The launcher reports `passed=true`, `authorizationPassed=true`, `cleanupVerified=true`, `productionVerified=false`. The tool independently recorded exact Payment-child cleanup before the launcher cleaned its recorded hosts, generated Compose resources and certificate export. Ignored artifacts remain at `.runstate/refund-hosted/<run-id>/matrix.json`, `verification.json` and private local diagnostics; CI uploads only the two bounded verification summaries.

Additional checks passed: the complete solution built with zero warnings/errors; 794 .NET tests passed with 108 environment opt-in cases skipped; eight acceptance guards passed; positive realm audience validation, rejection of an invalid Payment audience mapper, and PowerShell parse/diff checks passed. Skips in the general suite do not apply to the fully executed hosted matrix.

The joined test exposed two configuration defects and verified their corrections. Payment now receives SELECT on its own migration-history version column only, while checksum reads and history mutation remain denied. Its workload client explicitly emits the API audience for Order/Restaurant ownership requests. Existing Payment databases and persisted realms require the reviewed grant/mapper updates in the runbook; no production environment was changed.

The separately guarded Omise gate is implemented but was not executed because the required test secret and three fresh tokens are absent. Its completed-response discard/restart evidence is weaker than simulator provider response-loss/status-only fault evidence, and is documented separately. Remote CI execution, production rollout, browser/PKCE/WPF/POS/printing, real checkout, manual-tender refunds, return/restocking, fiscal credit notes and settlement certification remain outside this result.

## Documentation alignment

The project overview, project architecture and restaurant/POS architecture were updated. API, database, identity, deployment and component documentation describe the gate and required rollout corrections. ADR-016 was reviewed; the accepted refund ownership/lease/receipt decisions are unchanged. The documentation inventory for this slice follows.

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docker/financial-portal/README.md](../../../docker/financial-portal/README.md)
- [docker/keycloak/README.md](../../../docker/keycloak/README.md)
- [docs/API/Payment-Refunds.md](../../API/Payment-Refunds.md)
- [docs/Architecture/Evidence/Hosted-Refund-Acceptance.md](Hosted-Refund-Acceptance.md)
- [docs/Architecture/Project-Architecture.md](../Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../Database/Database-Design.md)
- [docs/Deployment/Deployment-Guide.md](../../Deployment/Deployment-Guide.md)
- [docs/Deployment/Hosted-Refund-Acceptance.md](../../Deployment/Hosted-Refund-Acceptance.md)
- [docs/Identity/Claims-Contract.md](../../Identity/Claims-Contract.md)
- [docs/Identity/Client-Matrix.md](../../Identity/Client-Matrix.md)
- [docs/Identity/Production-Runbook.md](../../Identity/Production-Runbook.md)
- [README.md](../../../README.md)
- [src/Services/NexaConnect.Services.Payment/README.md](../../../src/Services/NexaConnect.Services.Payment/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Tools/NexaConnect.DataMigration/README.md](../../../src/Tools/NexaConnect.DataMigration/README.md)
- [src/Tools/NexaConnect.PaymentProviderSimulator/README.md](../../../src/Tools/NexaConnect.PaymentProviderSimulator/README.md)
- [src/Tools/NexaConnect.RefundAcceptance/README.md](../../../src/Tools/NexaConnect.RefundAcceptance/README.md)
