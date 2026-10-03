# Financial completeness portal acceptance

The Customer Portal Sales report now reads recorded reconciliation observations through the authenticated Customer BFF. Explicit branch and closed UTC filters select the same window for fresh sales totals and recorded financial evidence. The view shows Not checked, Gaps detected or Observed complete, source/check timestamps, independent sales/payment/refund inventories and source retention/evidence gaps. It does not run checks or repair records.

## Verification scope

Executed locally on Windows on 2026-10-03:

- `dotnet build NexaConnect.sln --no-restore -v quiet`: zero warnings/errors.
- `dotnet test NexaConnect.sln --no-build -v quiet`: 785 passed, 108 opt-in tests skipped, zero failures (5 architecture, 585 unit and 195 integration passes). This includes nine `CustomerFinancialCompletenessBoundaryTests` cases for session/protected scope, current membership/subject, invalid windows, sanitized failures and actual outbound adapter correlation/tenant headers.
- Frontend `npm run check` and `npm test`: type checks and all 23 unit tests passed, including three financial status/scope/window validation tests.
- `npm run build --workspace @nexaconnect/customer-portal`: passed; Vite reports the portal's approximately 1 MB bundle exceeds its 500 kB advisory chunk threshold. Bundle splitting remains follow-up work.
- `npm run test:e2e:financial-completeness`: all nine Chromium cases passed. These cover the three statuses, inventories/provenance, identical read filters, denial/dependency failure, stale filter/tenant responses, invalid windows, scope mismatch and a 25-second browser timeout followed by recovery. The test-owned Vite server closed cleanly.
- Final `git diff --check`: passed.

BFF tests exercise the real customer session and protected tenant cookie with stubbed dependencies. Playwright browser tests use synthetic BFF fixtures. Neither replaces a joined live OIDC/BFF/Reporting/database/browser rollout check. No live database/broker acceptance was rerun for this read-only BFF/SPA change; persistence and source recovery are unchanged.

Financial observations remain historical snapshots. Current sales totals may reflect later changes even for the same window; the UI labels the recorded check time and its limits. All filter/tenant changes, reloads and denials clear old financial results. Delayed responses and timed-out requests cannot restore a previous observation. Dependency failure leaves independently successful fresh sales totals available while marking reconciliation unavailable. Reconciliation/repair remains in restricted operator tooling.

No migration, permission, OIDC client or financial mutation is added. Reporting 20/application compatibility 0.23.0 is required for observations; the existing combined recovery workflow also uses Order 11 and Payment 10. Deploy Customer BFF and its portal bundle together. Target-environment identity/permission revocation, record provisioning and live visual/operator acceptance remain release evidence.

## Documentation inventory

Updated documentation:

- [Root README](../../../README.md) and [AI project overview](../../../AI/architecture/project_overview.md).
- [Project Architecture](../Project-Architecture.md), [Restaurant POS Architecture](../Restaurant-POS-Architecture.md) and [Portal implementation phases](../Portal-Implementation-Phases.md).
- [Financial completeness contract](../../API/Financial-Reporting-Completeness.md), [Customer reporting contract](../../API/Customer-Product-Configuration-and-Reporting.md) and [Business API inventory](../../API/Business-Service-API-Slices.md).
- [Claims Contract](../../Identity/Claims-Contract.md), [recovery runbook](../../Deployment/Financial-Reporting-Recovery.md) and [Deployment Guide](../../Deployment/Deployment-Guide.md).
- [Customer BFF](../../../src/Gateway/NexaConnect.CustomerBff/README.md), [Frontend](../../../src/Frontend/README.md), [Customer Portal](../../../src/Frontend/apps/customer-portal/README.md), [Reporting](../../../src/Services/NexaConnect.Services.Reporting/README.md) and [recovery tool](../../../src/Tools/NexaConnect.FinancialReportingRecovery/README.md) READMEs.
- [Browser suite README](../../../src/Frontend/e2e/financial-completeness/README.md) and this evidence record.

ADR-019 and database ownership/schema documentation were reviewed: the accepted read-only observation/operator-repair boundary and existing Reporting-owned persistence are unchanged, so no ADR or database changes are needed. Earlier backend acceptance records remain historical evidence for their executed scope.
