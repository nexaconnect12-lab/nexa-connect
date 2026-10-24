# Sale and payment financial reporting acceptance

## Implemented scope

The 2026-10-03 working-tree slice adds receipt-backed `order.sale-completed.v1` publication from provider checkout, recovery/reconciliation, cash and manual PromptPay completion. Order commits immutable publication evidence and outbox state together. Reporting commits sales/payment facts, normalized financial hashes and a checkpoint atomically before acknowledgement. The opt-in durable consumer handles duplicates, out-of-order delivery, restart and conflict dead letters. The administrative recovery CLI previews/backfills retained receipts, attributes requeues and reconciles expected publications without Reporting writes.

Apply **Order 11 and Reporting 19 with minimum application 0.22.0** before deploying these binaries. Establish the Reporting sale consumer's durable binding/readiness before Order publication. See [contract, configuration and recovery](../../API/Sale-Financial-Reporting.md).

## Local verification

The implementation agent reported these completed checks against the final working tree:

| Check | Result |
| --- | --- |
| `dotnet build NexaConnect.sln --no-restore` | Passed; zero warnings and errors |
| `dotnet test NexaConnect.sln --no-build` | Architecture: 5 passed; Unit: 583 passed, 5 skipped; Integration: 185 passed, 99 skipped; total 773 passed, 104 skipped |
| Focused real PostgreSQL/RabbitMQ matrix | 9 passed, zero skipped |
| Historical backfill/PromptPay/replay-conflict case after the final conflict fix | 1 passed, zero skipped |

The full 773-pass suite was rerun successfully after the final outbox-conflict change. Its 104 skips are not acceptance evidence for their gated scenarios. The focused live matrix consists of `SaleFinancialProjectionPostgresTests` (3), `PaidOrderReceiptPostgresTests` (2), `OrderPricingPostgresTests` (2), `OrderMigrationRunnerAcceptanceTests` (1), and `SaleReportingMigrationAcceptanceTests` (1). It exercises atomic rollback, concurrent/format-equivalent replay, identity conflicts, tenant isolation, refund-before-sale, broker duplicate/out-of-order/restart handling, retained receipt time, manual tender identity, historical gaps and attributed replay. Actual clean-database runners verify Order 11 and Reporting 19 upgrade, empty-history downgrade/re-upgrade and evidence guards. The separate final replay case proves conflicting outbox evidence fails and rolls back the affected Order.

Live checks require disposable infrastructure and the opt-in environment variables documented in the [API verification instructions](../../API/Sale-Financial-Reporting.md). This record summarizes local execution reported by the implementation agent; it is not a machine-signed release record or production certification. The documentation audit inspected implementation, tests, configuration and migrations; relative links resolve and `git diff --check` passes.

## Documentation handoff

All documentation destinations changed in this slice:

- [Root README](../../../README.md) and [project overview](../../../AI/architecture/project_overview.md).
- API: [sale financial reporting](../../API/Sale-Financial-Reporting.md), [refund financial reporting](../../API/Refund-Financial-Reporting.md), [paid receipts](../../API/Paid-Order-Receipts.md), [business service slices](../../API/Business-Service-API-Slices.md), and [customer configuration/reporting](../../API/Customer-Product-Configuration-and-Reporting.md).
- Architecture: [Project Architecture](../Project-Architecture.md), [Restaurant POS Architecture](../Restaurant-POS-Architecture.md), [Phase-10 matrix](../Phase-10-Product-Integration.md), [Portal phases](../Portal-Implementation-Phases.md), subsequent-implementation notes in [ADR-016](../Decisions/ADR-016-durable-provider-refunds.md) and [ADR-017](../Decisions/ADR-017-refund-time-financial-reporting.md), new [ADR-018](../Decisions/ADR-018-receipt-backed-sale-projection.md), and this evidence record.
- [Database Design](../../Database/Database-Design.md), [Deployment Guide](../../Deployment/Deployment-Guide.md), and [production environment example](../../Deployment/production.env.example).
- Component READMEs: [Contracts](../../../src/BuildingBlocks/NexaConnect.Contracts/README.md), [Order](../../../src/Services/NexaConnect.Services.Order/README.md), [Reporting](../../../src/Services/NexaConnect.Services.Reporting/README.md), [Data Migration](../../../src/Tools/NexaConnect.DataMigration/README.md), and [Sale Reporting Recovery](../../../src/Tools/NexaConnect.SaleReportingRecovery/README.md).

The overview and both canonical architectures now describe receipt-backed financial ownership, atomic projection/replay boundaries, implementation status and completeness limits. Database Guidelines, identity client/claims contracts, and PostgreSQL/RabbitMQ component READMEs were reviewed unchanged because ownership, live HTTP permissions, provisioning and broker requirements remain valid.

## Limits and release gates

Historical Paid Orders without retained receipts remain explicit gaps. Scoped reconciliation compares expected retained publications and does not detect unrelated extra Reporting rows or certify every financial source. Refund historical replay, global completeness, item-sales projection, discounts, split tenders, manual refunds, fiscal credit notes and settlement accounting remain follow-up work. Net paid retains its existing payment-fact semantics; new sale facts preserve original received amounts independently of refund-period reporting. Operator attribution is asserted by the controlled administrative process, with database/process access as its authorization boundary. Production rollout, production identity/provider/POS interaction and complete accounting acceptance remain separate release gates.
