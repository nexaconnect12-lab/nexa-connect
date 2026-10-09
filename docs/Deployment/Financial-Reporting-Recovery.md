# Financial reporting recovery

Apply Order 11, Payment 10 and Reporting 20 with application compatibility **0.23.0** before deploying the financial recovery binaries. Payment completion now requires its retention table and Reporting's completeness API requires its observation table. Payment PostgreSQL readiness requires migration 10 and rejects a version-9 schema; it does not probe the provider. The migration launcher now defaults to 0.25.0 for the additional retained-cutoff migrations; the financial recovery minimum remains 0.23.0. Establish both durable Reporting financial queue bindings and readiness before enabling source outboxes; configure the existing [sale](../API/Sale-Financial-Reporting.md) and [refund](../API/Refund-Financial-Reporting.md) consumers. No new queue or integration event is introduced. All migrations are forward-only after retained evidence; do not delete history to bypass guards.

## Preview, repair and record

Use [NexaConnect.FinancialReportingRecovery](../../src/Tools/NexaConnect.FinancialReportingRecovery/README.md) with explicit organization, branch and closed half-open UTC window. Inject secret-managed connections into:

- `NEXACONNECT_FINANCIAL_RECOVERY_ORDER_DB`
- `NEXACONNECT_FINANCIAL_RECOVERY_PAYMENT_DB`
- `NEXACONNECT_FINANCIAL_RECOVERY_REPORTING_DB`
- `NEXACONNECT_FINANCIAL_RECOVERY_ACTOR` for either write mode (nonempty, maximum 128 characters, no control characters).

Default mode only reads evidence and reports counts. `--apply` retains validated legacy originals, requeues original sale/refund publications with attributed audits, then records the immediate check. It never invokes payment/refund commands, rewrites report facts or changes original source timestamps. `--record` performs inspection and records it without source mutations. There is no automatic loop or waiting for consumers; `--apply` may record gaps while delivery catches up. Rerun preview and then `--record` after queues drain to record the new result.

```powershell
dotnet run --project src/Tools/NexaConnect.FinancialReportingRecovery --no-build -- <organization-uuid> <branch-uuid> 2026-09-01T00:00:00Z 2026-09-02T00:00:00Z
# Same arguments plus --apply for an explicitly attributed source replay.
# Same arguments plus --record for an explicitly attributed observation only.
```

Each source is limited to 10,000 candidates and each Reporting inventory to 20,000 rows; windows are at most 31 days. Narrow an over-limit window. Execution is cancelled after five minutes. Output includes counts, check/range/source times, manifest hash, status, requeue counts and record flag, excluding financial bodies, actor and credentials. Exit 0 means observed complete, 1 means gaps, 2 means invalid input or failed execution. Treat output as restricted operational evidence.

Use read-only credentials for preview. Write mode needs source SELECT, publication/outbox/audit INSERT, and outbox UPDATE, plus PostgreSQL permission to lock `orders` and `refunds` with `SELECT ... FOR UPDATE` (UPDATE privilege on at least one column in each owning aggregate table). The tool does not update those aggregate rows. Recording needs Reporting inventory SELECT and observation INSERT; protect actor/result history. These are privileged operator connections, never customer or portal settings. This slice does not provide or verify a deployed restricted-role grant script. The tool calls separately owned Infrastructure adapters: there is no cross-database SQL or distributed transaction. Each source requeue/audit commits per aggregate, so failure may leave prior repairs committed. An idempotent rerun preserves financial/event identity and appends a new operator audit. Investigation of conflicts and restoration of verified original history is an operator responsibility; never fabricate a lost legacy refund ID or overwrite fact rows.

The [read API](../API/Financial-Reporting-Completeness.md) returns the latest recorded exact-window observation. Unexpected authorization/database failures produce sanitized no-store 503 responses with category-only diagnostics; explicit authorization denial is 403. Retain check UUID, source/check times and manifest hash for investigation. A complete observation can become stale after later source completion/projection changes; it does not authorize end-of-day settlement.

## Customer Portal rollout

Deploy the Customer BFF and its portal bundle together after Reporting 20 is available. The read uses existing `Services__Reporting`, Platform Directory settings and server-held Customer tokens; no new Customer BFF environment variable, permission, browser OIDC client, migration or consumer is required by the portal slice. Keep all recovery database credentials outside BFF/browser configuration.

The Sales page reads only recorded observations for the exact branch and closed UTC period entered. Provision one with the restricted tool's `--record` after inspecting/repairing and allowing asynchronous consumption to catch up. Loading the page does not create a check; absence displays Not checked. Keep source/check timestamps with evidence because historical Observed complete does not certify current totals or settlement.

Before release, verify a live authorized Customer session against the actual BFF/Reporting stack: all three statuses and source/check times; same-window totals; existing-session membership and sales-permission revocation; unavailable dependencies; filter/tenant switching during delayed reads; and no repair requests. Use the [joined disposable portal runner](Financial-Completeness-Portal-Acceptance.md) for repeatable real OIDC/BFF/Reporting, original outbox delivery and operator-recorded status validation. Local synthetic browser and stubbed HTTP checks do not satisfy the joined gate; target-production acceptance remains separate. Run `npm run test:e2e:financial-completeness` and `CustomerFinancialCompletenessBoundaryTests` for repeatable contracts; see [portal evidence](../Architecture/Evidence/Financial-Completeness-Portal-Acceptance.md). BFF diagnostics use `{service_name="nexaconnect-customer-bff"} |= "Financial completeness BFF"`; Reporting diagnostics retain the existing service queries in the API contract.

## Verification

Run unit/API tests normally. Live fixture tests require `NEXACONNECT_ENVIRONMENT=Testing` (or Development/Test) and secret-injected `NEXACONNECT_REPORTING_INTEGRATION_DB` on disposable PostgreSQL. They create and remove three isolated owning schemas. Hosted recovery additionally requires `NEXACONNECT_RABBITMQ_ACCEPTANCE=1`, secret-injected `NEXACONNECT_RABBITMQ_INTEGRATION_URI` and `NEXACONNECT_FINANCIAL_PROCESS_ACCEPTANCE=1`, which authorizes termination of only the Reporting child processes created by that test. Build the solution first; the harness starts the Debug/net10.0 Reporting DLL. It uses generated isolated exchange/queues, an unreachable loopback publisher endpoint, refund-before-sale delivery, queued sale/duplicate refund during Reporting downtime, fresh-process consumption and final reconciliation. It does not stop the shared broker or prove post-commit/pre-ack interruption.

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --no-build --filter FullyQualifiedName~FinancialCompletenessTests
dotnet test tests/Integration/NexaConnect.IntegrationTests --no-build --filter "FullyQualifiedName~FinancialCompletenessHttpTests|FullyQualifiedName~FinancialCompletenessPipelineTests"
```

Actual clean-database migration-runner checks require a disposable `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB` with database-create capability, `NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE=1` and `NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE=1`. Run `PaymentMigrationRunnerAcceptanceTests` and `SaleReportingMigrationAcceptanceTests`. They own generated databases and test empty-history downgrade/re-upgrade plus forward-only guards after evidence. The runner commits each migration separately: a multi-step downgrade can commit an earlier safe step before a later history guard refuses. Inspect the retained migration history and re-upgrade safely before deploying the current binary. Existing broader Order acceptance remains separate. Local passes do not replace production credentials, recovery, load, operator/browser or settlement release gates.

The joined matrix exposed a missing Restaurant hierarchy in Reporting's customer authorization decision. Deploy the corrected Reporting authorization adapter with `Services__Restaurant`, `WorkloadIdentity__Authority`, `WorkloadIdentity__ClientId=nexaconnect-reporting-service` and a distinct secret-managed `WorkloadIdentity__ClientSecret`. Provision the dedicated client/audience in persisted Keycloak realms through a reviewed update; restart/import does not apply it. Restaurant's endpoint-specific branch-scope policy permits this read without granting Reporting generic workload access. Coordinate Restaurant policy and identity configuration before enabling corrected Reporting reads. No schema or customer permission widening is required; see [Reporting setup](../../src/Services/NexaConnect.Services.Reporting/README.md).
