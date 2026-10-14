# November draft — slice 0 baseline

Date: 14 October 2026 (Asia/Singapore). Scope: one branch, Windows POS, online cash/manual PromptPay, browser Kitchen queue and manager portal. This report implements the investigation/verification slice in the [draft plan](November-2026-First-Draft.md). It adds no business capability or environment configuration.

This is the original slice 0 snapshot. Later [slice 1 implementation/evidence](../Architecture/Evidence/First-Draft-Slice-1.md) changes manual defaults, local deployment and session configuration; original findings and counts below remain historical evidence for the named commit, not unresolved assertions about the latest working tree.

## Baseline and verification provenance

Baseline commit: `7e91480429838f092093b4b142a2b0a64cce4c9f`. Initial `git status --short` contained only untracked `docs/Planning/`, the preceding plan. There were no tracked application changes. A `git archive HEAD` source copy excludes `.env`, generated binaries, frontend dependencies and untracked files. Clean verification runs use that archive; two further independent archive copies verify BFF publication without pre-existing frontend output.

Local tools: .NET SDK `10.0.400`, Node `24.19.0`, npm `11.17.0`, Docker server `29.6.2`. The SDK satisfies `global.json` feature roll-forward and Node satisfies the checked-in frontend engine range. Evidence is ignored under `.runstate/slice0-baseline/20261014-7e914804/evidence/`. These generated artifacts are local verification evidence, not committed release packages. No runtime database, user identity, workload grant or business data was changed.

| Check | Actual outcome | Evidence / qualification |
| --- | --- | --- |
| Clean solution Release build | Passed, zero warnings/errors | `clean-build-authorized.log`; includes `NexaConnect.POS` for `net10.0-windows` |
| Unit suite | 796 passed, five opt-in cases skipped | `unit.log`, `unit.trx` |
| Windows DPAPI recovery opt-in | All five passed, zero skips | `unit-dpapi.log`, `unit-dpapi.trx`; completes the initially skipped unit cases |
| Architecture suite | Six passed, zero skips | `architecture.log`, `architecture.trx` |
| Integration component suite | 338 reported passed, 221 skipped, zero failures | `integration-component.log`/TRX; external acceptance settings cleared in the child process |
| Frontend TypeScript check | Passed | `frontend-check.log`, `frontend-results.json` |
| Frontend Vitest | 76 tests / 22 files passed | `frontend-test.log` |
| Both portal frontend builds | Passed | `frontend-build.log`; existing Vite chunk-size warnings remain |
| Production and full frontend dependency audits | Passed, zero reported vulnerabilities | `audit-production.log`, `audit-all.log`, `audit-results.json`; point-in-time registry result |
| Customer BFF publication from independent clean copy | Passed; published index and entry asset present | `publish-customer.log`, `published-assets.json` |
| Platform Admin BFF publication from independent clean copy | Passed; published index and entry asset present | `publish-platform.log`, `published-assets.json` |
| Current-commit remote CI | Nine successful workflows, one failed | GitHub read-only retrieval on 14 October; details below |
| Live PostgreSQL/broker/OIDC and physical POS acceptance | Not executed in this slice | Classified prerequisites and future gates below |

Integration results establish component verification only. Some existing tests return early when configuration is absent, so even reported passes cannot prove database/broker execution. No skipped or early-return case counts as live acceptance. The five explicitly enabled Windows tests use generated temporary protected local state; they do not demonstrate interactive WPF, printer or OIDC behavior.

Initial sandbox attempts failed: NuGet vulnerability lookup could not reach `api.nuget.org`, npm workspace symlink creation returned `EPERM`, and Docker's named pipe denied access. Retrying with approved execution access succeeded for restore/build, npm installation and the read-only Docker check. No audit policy, warning severity, dependency pin or application source was weakened. An initial working-checkout build also failed under the sandbox; it is not the clean-build evidence. Raw diagnostic logs remain ignored locally.

To reproduce, archive this exact commit into a new empty ignored directory with `git archive --format=zip --output=<archive-path> 7e91480429838f092093b4b142a2b0a64cce4c9f`, then `Expand-Archive` into a new source directory. Do not copy working-tree `.env`, dependencies or build outputs. From that source root run:

```powershell
dotnet build NexaConnect.sln -c Release --verbosity minimal -m:1
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj -c Release --no-build --no-restore
dotnet test tests/Architecture/NexaConnect.ArchitectureTests/NexaConnect.ArchitectureTests.csproj -c Release --no-build --no-restore
# Enable only in a disposable child shell on Windows; these tests own temporary state.
$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE = '1'
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PosPendingSettlementRecoveryTests
```

The component integration invocation uses the Integration project with the same Release/no-build/no-restore options, in a separate child shell with external acceptance/database/provider settings cleared. The actual run derives referenced variable names from the Integration sources and clears `NEXACONNECT_*`, `NEXA_*` and `MINIO_*` inputs; it never prints their values. Do not run the unqualified Integration suite against inherited secrets or assume generated schema names make an existing database disposable.

From the clean source's `src/Frontend`, execute `npm ci --ignore-scripts`, `npm run check`, `npm test`, `npm run build`, `npm run audit:production` and `npm run audit:all`. For BFF publication, extract two independent fresh copies of the same archive and run the applicable command in each, with distinct output directories:

```powershell
dotnet publish src/Gateway/NexaConnect.CustomerBff/NexaConnect.CustomerBff.csproj -c Release -o <customer-output>
dotnet publish src/Gateway/NexaConnect.PlatformAdminBff/NexaConnect.PlatformAdminBff.csproj -c Release -o <platform-output>
```

Replace angle-bracket placeholders with task-owned paths. Confirm each output's `wwwroot/index.html` and every referenced `/assets/` entry file exists. The local machine-readable `verification.json` and `published-assets.json` summarize the executed checks; test logs/TRX provide the detailed results.

## Current remote CI, not historical assumptions

The GitHub connector returned the first page of pull-request-triggered workflow runs for this exact commit. Results were completed on 12 October and retrieved on 14 October 2026; this snapshot does not represent other commits, branches, manual runs or production readiness.

| Workflow | Outcome | Run |
| --- | --- | --- |
| Financial completeness | Success | [38240854694](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854694) |
| Cashier-to-day-close | Success | [38240854722](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854722) |
| Cash-close | Success | [38240854633](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854633) |
| Day-close preparation | Success | [38240854638](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854638) |
| Day-cutoff evidence | Success | [38240854657](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854657) |
| Hosted refund | Success | [38240854754](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854754) |
| Frontend dependency/compatibility | Success | [38240854720](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854720) |
| Day-seal evidence | Success | [38240854688](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854688) |
| Cashier-to-cutoff | Success | [38240854608](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854608) |
| End-of-day | Failure during infrastructure startup | [38240854690](https://github.com/nexaconnect12-lab/nexa-connect/actions/runs/38240854690) |

Failed job `114782806704` completes restore, workload-audience checks, authorization checks and browser preparation. RabbitMQ exits 1 before application/browser acceptance; bounded startup diagnostics report `running=false`, `oomKilled=false`, `reason=filesystem_permission`. This is a diagnostic category, not a proven underlying permission/file cause. The log shows owned resource removal. An acceptance pass still requires a successful rerun and its verified cleanup receipt. Do not weaken permissions, add blanket retries or claim application regression from this startup failure. No remote jobs were rerun or modified in this slice.

## Reconciled implementation boundary

The migration catalog, rather than old introductory deployment paragraphs, defines this commit's targets:

| Service | Latest schema | Minimum app compatibility on latest migration |
| --- | --- | --- |
| Platform Directory | 3 | 0.3.0 |
| Authorization | 15 | 0.34.0 |
| Restaurant | 3 | 0.1.0 |
| Catalog | 4 | 0.5.0 |
| Inventory | 5 | 0.6.0 |
| Kitchen | 3 | 0.8.0 |
| Order | 19 | 0.33.0 |
| Payment | 18 | 0.33.0 |
| POS | 23 | 0.38.0 |
| Reporting | 22 | 0.37.0 |
| Customer | 2 | 0.9.0 |
| Media | 4 | 0.1.0 |
| Notification | 3 | 0.10.0 |

`scripts/migrate-databases.ps1` defaults to application compatibility `0.38.0`; this is not a schema target or a claim that every component is versioned identically. Migration preview/application and persistent runtime grants belong to slice 1, not this baseline. Recorded financial history can prohibit destructive downgrade; use documented forward recovery.

Code/doc observations requiring follow-up:

- Checked-in POS `appsettings.json` selects `card_omise_test` and `EnableOmiseTestCheckout=true`, despite the November manual-tender scope. It also contains a workstation-specific PromptPay QR path. The launcher correctly requires matching card enablement; changing only a flag or reusing defaults would not establish a manual-tender deployment. Slice 1 must supply explicit coherent configuration without touching pending terminal operations.
- `OrderPricingService` requires THB. Customer branch creation defaults to SGD / Asia/Singapore. THB must be explicit; the business timezone remains a branch decision and Asia/Singapore is not intrinsically invalid.
- The checkout and Phase 8 launchers have different host graphs/ports. The current Kitchen runbook requires a matching BFF Kitchen address. Day-close source dependencies may require Payment even with provider checkout disabled.
- Kitchen browser contracts use synthetic BFF replies. A joined POS-to-Kitchen real-identity gate remains work for slice 3.
- Explicit pre-payment cancellation coordinates Kitchen before stock release. Ordinary placement/payment-failure compensation uses a separate path. Joined review must prove completed-preparation conflicts and correct stock custody in both; no confirmed code defect is claimed from this baseline alone.
- Existing manual inventory reservation reduces available stock with idempotent release; this is not recipe consumption, costing or wastage management.
- Provider-captured refunds exist, but manual cash/PromptPay refunds do not. Neither a cash movement nor a late cash correction is a supported refund substitute.
- Local cash replay and protected checkout recovery exist; full offline ordering/KDS, table/modifier contracts, split bills and dedicated peripheral drivers remain excluded. The POS hardware abstractions contain unconfigured adapters; the supported receipt path is generic Windows printing, subject to physical verification.

## Prioritized implementation backlog

P0 is required to enter stable testing or preserve financial/security correctness. P1 is required for the selected draft acceptance. P2 is optional polish. Estimates are focused working days, not fixed delivery promises; aggregate slice estimates include shared work and should not be added per row.

| ID | Priority / slice | Work and proof of completion |
| --- | --- | --- |
| B01 | P0 / 1 | Joined owned stack with consistent ports, endpoints, readiness and start/status/stop; clean setup and owned cleanup pass |
| B02 | P0 / 1 | Explicit THB cash/manual PromptPay config, card disabled, deployment-controlled QR asset, correct terminal scope; launcher validates both manual methods |
| B03 | P0 / 1 | Catalog-derived migration targets, least-privilege runtime grants and consumer-before-producer startup; reconcile stale current-version docs |
| B04 | P0 / 1 | Diagnose end-of-day RabbitMQ `filesystem_permission` startup category; retain successful clean remote rerun and safe evidence |
| B05 | P0 / 2 | Consistent branch/menu/stock/station fixture and scoped users, using owning APIs/tooling; no permanent bypass grants |
| B06 | P1 / 2 | Authorized branch selection and usable existing station selection; no stale data after branch/tenant change |
| B07 | P0 / 3 | Joined actual manual-tender Order/Inventory/Kitchen/receipt commands with real identity and exact duplicate/restart proof |
| B08 | P0 / 3 | Separate cancellation and ordinary failure compensation coverage; completed-food conflicts never fabricate cancellation or release stock twice |
| B09 | P1 / 4 | Reproducible WPF install/update, PKCE registration and protected pending-state preservation; actual terminal verification |
| B10 | P0 / 4 | Lost-response/restart/lock recovery uses original order/operation; no duplicate collection; no unauthorized recovery bypass |
| B11 | P1 / 4 | Windows PDF printer receipt print/reprint, PDF inspection and cancelled/failed print recovery without changing Paid; physical printer deferred |
| B12 | P0 / 5 | Joined cash close, variance review, completed-day evidence/approval/finalization and live role revocation/manager contention |
| B13 | P1 / 5 | Settlement ledger/export preserve totals, corrections, delivery gaps and provenance; safe manual-refund limitation visible in runbook |
| B14 | P0 / 6 | Candidate-wide no-skip required acceptance, isolated backup/restore and retained event recovery; documented financial-history forward recovery |
| B15 | P1 / 6 | Redacted telemetry, operational queries and actual alert-receiver limitations; no sensitive artifacts |
| B16 | P1 / 7 | Staff acceptance script, blocker triage, actual device attestations, final build/runbook/evidence handover |
| B17 | P2 / 6 or later | Frontend chunk splitting / non-blocking cosmetic usability, only after critical gates |

Manual-tender refunds, table/modifier/split-bill workflows and branch-edge offline operation require separately estimated scope decisions. They are not hidden tasks inside B08/B13.

## Traceable draft acceptance matrix

Every case must retain candidate identity, environment, actor role, observed result, sanitized evidence and cleanup where applicable. Baseline component verification is supporting evidence, not a pass for these joined/physical cases.

| Case | Role / scope | Required observable result | Backlog / slice | Required verification level |
| --- | --- | --- | --- | --- |
| A01 | All roles | Fresh complete stack starts with correct schema/configuration and owned stop/cleanup | B01–B04 / 1 | Clean environment |
| A02 | Cashier | PKCE login, active membership, enrolled terminal, shift and cash open succeed | B05, B09 / 2, 4 | Actual OIDC + Windows |
| A03 | Cashier | THB server quote and price-change reconfirmation; accepted total/receipt agree | B07 / 3 | Joined real commands |
| A04 | Cashier | Cash paid once; drawer movement and receipt match exact order | B07 / 3 | PostgreSQL + broker + identity |
| A05 | Cashier | PromptPay requires configured QR, explicit human confirmation/reference; no cash drawer inflation | B02, B07 / 1, 3 | Joined + staff verification |
| A06 | Cashier | Insufficient stock rejects safely; duplicate/replayed order reserves stock once | B07 / 3 | Concurrent PostgreSQL |
| A07 | Kitchen operator | Exactly one ticket per station; Start/Ready/Complete follow permitted transitions | B07 / 3 | Joined actual POS/Order/Kitchen |
| A08 | Two Kitchen screens | Stale version conflicts and authoritative refresh; uncertain transition is not blindly replayed | B07 / 3 | Real BFF + identity + process/transport fault |
| A09 | Cashier/Kitchen | Pre-payment cancel cancels Kitchen before release; completed food retains investigation/custody | B08 / 3 | Joined cross-service race |
| A10 | Workflow failure | Ordinary compensation after dependency/payment failure does not fabricate success or duplicate stock effects | B08 / 3 | Separate failure-path test |
| A11 | Cashier | Response loss / process restart recovers original checkout and settlement identity | B10 / 4 | Actual Windows + joined fault |
| A12 | Cashier | Lock/re-login clears secrets/rendered receipt and preserves recoverable operations; forbidden actions stay denied | B09–B10 / 4 | Physical session policy |
| A13 | Cashier | PDF print/reprint succeeds and receipt layout is inspected; cancelled/failed printing cannot cause recollection | B11 / 4 | Actual Windows PDF printer; physical printer deferred |
| A14 | Cashier/manager | Counted cash, close and variance review use authoritative versions; pending movements block close | B12 / 5 | Joined real commands |
| A15 | Manager | Completed local day passes evidence/approval/fences/finalization only with verified current delivery | B12 / 5 | Existing guarded historical gate + real next-day drill |
| A16 | Two managers | Contention/late change/restart cannot finalize stale approved proof or rewrite original settlement | B12 / 5 | Concurrent sources + restart |
| A17 | Accountant | Read-only reports/ledger allowed; privileged POST denied even if crafted directly | B05, B12 / 2, 5 | Real OIDC/API |
| A18 | Foreign tenant/branch, revoked role | No resource leak or mutation; both BFF and owning service enforce scope/live authorization | B05, B07, B12 / 2–5 | Separate tenant + actual revocation |
| A19 | Manager | Missing/delayed events and dependency outage block readiness; ledger/CSV retain gap/provenance | B13 / 5 | Broker/HTTP fault + export |
| A20 | Operator | Broker/process recovery and isolated restore retain original identities, audit and financial invariants | B14–B15 / 6 | Owned operational drill |
| A21 | User in separate test roles | Full supported day cycle completed with no unresolved financial/security/data-loss blockers | B16 / 7 | Solo user UAT + evidence index; independent staff feedback deferred |

## External integration-test prerequisites

Slice 0 runs no destructive database gate. Slice 1 supplies verified owned infrastructure; later slices run risk-specific gates. Docker is available with approved access, but no existing container/database has been assumed disposable.

| Gate | Representative entry point | Prerequisites / boundary |
| --- | --- | --- |
| Pricing/receipt persistence | `scripts/test-order-pricing.ps1 -ConfirmDisposableInfrastructure` | Owned PostgreSQL and migration runtime; stock/quote/receipt/rollback assertions |
| Cashier/backend protected state | `scripts/test-pos-cashier-sale.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback` | Recognized local Docker context, Windows DPAPI, generated PostgreSQL/RabbitMQ; no existing data target |
| Kitchen persistence/broker | Kitchen-filtered Integration tests | Secret-injected `NEXACONNECT_KITCHEN_INTEGRATION_DB`, Testing environment; RabbitMQ opt-in/URI for broker cases; only proven disposable targets |
| Cashier-to-day-close | `scripts/test-cashier-day-close.ps1 -ConfirmDisposableInfrastructure` | Guarded generated owning databases, Keycloak and actual broker; confirm current script parameters before invocation |
| Day close/cutoff/settlement | Existing `test-day-close-preparation`, `test-day-cutoffs`, `test-day-seals`, `test-cashier-day-cutoff`, settlement runners | Run-owned source/Authorization/Reporting databases, migrations, identity, TLS configuration and safe evidence; no skips accepted |
| Real kitchen OIDC/POS | New joined gate in slice 3 | Existing Kitchen synthetic Playwright suite is insufficient |
| External provider/media/notification gates | Existing opt-in runners | Outside selected manual-tender draft unless needed for its active dependency surface; never enable real provider credentials in the component baseline |

Consult component runbooks for exact current flags, environment names and cleanup ownership rather than applying this table as a shell script.

## Environment and staff prerequisites

| Prerequisite | Baseline status | Required by |
| --- | --- | --- |
| Test-stack host, OS/capacity, local/LAN access and durable test storage | Not supplied; local Docker available for automated verification only | Slice 1 |
| Windows cashier device/account and terminal enrollment identity | User confirmed a Windows PC; Windows account and terminal enrollment still require setup | Slice 2 / 4 |
| Printer path | User confirmed a PDF printer; verify its installed driver and output on the actual PC; physical receipt printer deferred | Slice 4 |
| Branch business timezone | Singapore testing: configure `Asia/Singapore`; THB remains mandated by the implemented quote contract | Slice 2 |
| Manual PromptPay test QR / reference policy | Unconfirmed; current absolute workstation path must be replaced for deployment | Slice 1 / 3 |
| Cashier, Kitchen, manager, accountant test subjects and second tenant | Must be provisioned through approved owners; no reuse assumed | Slice 2 |
| Test secret injection, TLS/certificates and trusted endpoints | Required; no secrets committed or copied into archived source | Slice 1 |
| Test participants and availability | User is the sole draft tester, using separate scoped identities; first-test/UAT targets remain 11/23 November, exact sessions unconfirmed | Slice 4 / 7 |

User clarification received after slice 0 verification: Windows PC, PDF printer, Singapore testing and one tester. This updates prerequisites only; it does not change the recorded verification environment or prove interactive print/login acceptance. Use isolated browser profiles/sessions and coordinated automated requests to preserve two-operator/manager contention tests. Provision restricted role identities and a second tenant rather than using a single unrestricted administrator. Independent staff feedback and physical receipt-printer/peripheral acceptance are deferred. The test-stack host remains unconfirmed. Singapore testing does not enable SGD pricing or certify fiscal compliance.

## Slice 1 handoff and estimate

Local source build/test baseline is green with the stated integration exclusions. Slice 0 may hand off investigation results; it does not admit draft readiness. Before stable joined testing, resolve B01–B04, provision B05 and confirm the external prerequisites above. The outstanding remote end-of-day infrastructure failure remains a release gate; the underlying permission cause is not established.

Keep the original targets as provisional: slice 1 five working days, slice 2 three, slice 3 five, slice 4 four, slice 5 three and slice 6 five (25 focused implementation days), followed by six weekdays for UAT/fixes. Hardware or host availability and the broker investigation can move these dates. Re-estimate after slice 1 instead of shrinking safety gates.

Documentation changes: this baseline and the linked plan. `README.md`, `AI/architecture/project_overview.md`, `docs/Architecture/Project-Architecture.md` and `docs/Architecture/Restaurant-POS-Architecture.md` were reviewed without modification: application capability, contracts, data ownership and architecture did not change. Stale deployment/current-version claims are identified for slice 1. No runtime configuration was edited and no new behavior tests are needed for this documentation/evidence slice.
