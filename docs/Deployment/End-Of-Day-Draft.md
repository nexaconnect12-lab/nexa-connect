# End-of-day draft deployment and verification

Deploy the Restaurant calendar route, Order/Payment/POS source reads, Reporting orchestrator, Customer BFF and Customer Portal together. Keep the existing Order 11, Payment 10, POS 7, Reporting 20 and Authorization 9 baselines; no new schema or permission migration is needed for the read-only draft. Enabling the separate [POS preparation workflow](Day-Close-Preparation.md) additionally requires POS 8 / Authorization 10 and new POS/BFF dependencies. Order and Payment must use `Persistence__Provider=PostgreSQL`; in-memory development adapters return unavailable for this draft rather than invented totals. Existing service runtime SELECT privileges cover these owned tables.

Reporting needs `Services__Order`, `Services__Payment`, `Services__POS`, `Services__Restaurant`, Platform Directory/Authorization addresses and its existing dedicated `nexaconnect-reporting-service` workload credentials. Development uses Order `https://localhost:7020/`, Payment `https://localhost:7115/`, POS `https://localhost:7120/`. Production addresses belong to the actual independently deployed HTTPS hosts; keep TLS validation enabled. Existing realm/client/audience provisioning from the financial portal remains required. Customer tokens, not Reporting workload credentials, authorize financial summaries. Confirm all permissions in the [contract](../API/End-Of-Day-Draft.md); default reporting access alone is insufficient.

Align clocks across source and Reporting hosts: observations later than the Reporting clock fail the draft. Owner transport/read failures return sanitized `503`; existing Directory membership adapters may instead deny with `403` on a non-success access lookup. Update host timezone data and verify branch timezone/currency before selecting a date. Historical dates use current configuration. Obtain an exact-window check with the existing [financial recovery tool](Financial-Reporting-Recovery.md) when needed; the page never runs it. Reload after resolving open shifts, payment/refund uncertainty or cash reviews. Source reads cannot prevent a later financial event or new operation; do not use the draft as settlement approval.

## Verification

Run from repository root:

```powershell
dotnet build NexaConnect.sln --no-restore --verbosity quiet
dotnet test NexaConnect.sln --no-restore --verbosity quiet
dotnet test tests/Unit/NexaConnect.UnitTests --no-restore --filter "FullyQualifiedName~EndOfDay|FullyQualifiedName~PaymentTenantAuthorizationTests" --verbosity quiet
dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter "FullyQualifiedName~EndOfDay" --verbosity quiet
```

The three `EndOfDaySourcePostgresTests` require `NEXACONNECT_ENVIRONMENT=Testing` and `NEXACONNECT_REPORTING_INTEGRATION_DB` pointing at disposable PostgreSQL. They create/drop uniquely generated schemas for each owning service using its actual migrations. Never point this fixture at production. The remaining HTTP tests use test authentication/transport fixtures and real Application/controller/BFF paths.

Run from `src/Frontend`:

```powershell
npm run check
npm test
npm run build
npm run test:e2e:end-of-day
```

The six Chromium cases use synthetic BFF replies and the actual portal on a test-owned Vite host; they do not prove live OIDC or provider execution. Verify denied/unavailable reads remove old totals, filter/tenant changes discard delayed responses, wrong scopes fail and no financial command is sent.

Local Windows verification on 2026-10-03 passed eight draft unit cases plus the Payment branch-authorizer case, 16 new HTTP/database cases (including all three PostgreSQL fixtures without skips), and six synthetic browser cases. Broader verification and documentation audit are recorded in the [evidence handoff](../Architecture/Evidence/End-Of-Day-Draft.md). The [joined live OIDC/owning-host gate](End-Of-Day-Portal-Acceptance.md) subsequently passed eight browser and six Authorization persistence cases with verified cleanup locally on 2026-10-03; production workloads/latency/timezone verification, settlement approval/locking and exports remain open. Provider refund acceptance retains its separate [release gate](Hosted-Refund-Acceptance.md).

Run the [joined portal gate](End-Of-Day-Portal-Acceptance.md) to validate real OIDC, owning HTTP summaries and retained-event convergence. Component/synthetic verification remains separate from this gate and target-production acceptance.
