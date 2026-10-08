# Joined cashier-to-cutoff acceptance

Current gate includes fourteen scenarios, adding real manager approval against the loaded ready seal version and real accountant approval denial, followed by supersession after an actual historical cash sale. Current targets: Order 16 / Payment 15 / POS 14 / Authorization 11 / Reporting 20, compatibility 0.30.0. Local run `1b4635280a744bb18dcdc17b9dc4d935` passed with Authorization checks and verified cleanup on 2026-10-08. See [approval rollout](Day-Close-Approvals.md). Earlier run/version counts below are historical evidence.

Current cutoff/approval mode uses Order 16 / Payment 15 / POS 14 / Authorization 11 and compatibility 0.30.0. It now requires fourteen distinct real-OIDC browser passes, including reviewed source sealing, manager approval/accountant denial and verification that a subsequent actual historical sale supersedes approval and invalidates readiness while preserving original evidence. Six Authorization persistence cases and exact cleanup remain required. Earlier twelve-case runs are historical; current execution is recorded in docs/Architecture/Evidence/Day-Close-Seals.md. Original non-cutoff modes retain their schema targets.

The earlier revision-protocol-two mode used Order 13 / Payment 12 / POS 10 at compatibility 0.26.0 and required exact-set Reporting delivery proof. Its twelve-case execution and prior 0.25.0 acceptance remain historical. Current seal execution is recorded in [seal evidence](../Architecture/Evidence/Day-Close-Seals.md).

The guarded gate joins actual cashier PKCE and Customer BFF sessions with owning Catalog/Inventory/Kitchen/Order/Payment/POS/Reporting APIs, PostgreSQL and RabbitMQ. It verifies [retained cutoff evidence](../API/Day-Close-Cutoffs.md); it does not finalize settlement or provide source-write fences, delivery watermarks, provider certification, WPF/printing or offline acceptance.

## Run and topology

Requires PowerShell 7, .NET 10, Node 22.12+ on 22.x or 24+, installed Playwright Chromium and a local Docker socket. From repository root:

```powershell
dotnet restore NexaConnect.sln
Push-Location src/Frontend
npm ci --ignore-scripts
npm run audit:production
npm run audit:all
npm run test:cashier-day-cutoff:guards
npx playwright install chromium
Pop-Location
./scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure
```

`-NoBuild` requires current Debug binaries. The runner still builds ancillary services/recovery tooling, publishes the BFF/portal and runs all six Authorization persistence cases. It requires fourteen distinct browser passes with no skips/retries and verified cleanup. No missing setting permits a skip. CI retains only bounded verification/summary JSON for 14 days; local service logs and credentials are never uploaded.

The mode reuses the ten-context [cashier Docker topology](../../docker/cashier-day-close/README.md) under its own random `nexa-cashier-day-cutoff-<run>` project. It pins Order 16, Payment 15, POS 14, Reporting 20, Authorization 11 and compatibility 0.30.0. POS receives Order, Payment, self-source and Reporting addresses; its Payment dependency traverses the owned fault proxy. Older preparation/cashier launchers retain their pinned schemas and controls.

## Real commands and controlled faults

Provisioning creates reference store/hierarchy/membership/roles only. A fourth disposable subject receives the existing branch accountant role. The first manager's explicit enrollment reference grant remains separate from normal manager permissions. Cashier PKCE and all bearer commands use real Keycloak validation; tokens stay in the test process. Portal controls use actual BFF cookies/CSRF and current tenant/member checks. Successful financial/report responses come from the owning APIs; browser routing only restricts allowed origins. The fault proxy may return 503 after upstream transport failure.

The acceptance-only Order/POS host accepts either exact run kind `cashier-day-close` or `cashier-day-cutoff`, a run-scoped clock/database/realm and loopback port. Production hosts expose no clock switch. Financial commands use yesterday at 10:00 Asia/Bangkok, followed by system time for completed-day capture. The late-sale case briefly restores the same historical command instant, then returns to live time. Existing persisted timestamps and immutable receipts/publications are never rewritten. This is deterministic historical command acceptance, not real midnight/clock-skew certification.

Order starts with its actual dispatcher batch size zero. Authorized settlement retains original financial events/outbox/receipt while delivery is paused. The cash drawer closes through the owning API and its manager reviews the provisional variance. Cutoff capture retains sources but blocks on missing Reporting facts. The bounded file-control protocol terminates only the retained Order child, then restarts that same configured host with batch size 100. Actual workers deliver the retained originals to Reporting and POS. Late cash changes the closed drawer version and requires another review; explicit cutoff refresh obtains new generations and a fresh identity/hash comparison.

The owned Payment HTTP proxy can hold only its source-cutoff POST, while other reads continue. This lets Order capture commit before POS termination without forwarding the held Payment capture. Exact original-actor resume after lease expiry reuses the Order manifest; competing-manager replacement obtains a new operation and fences the abandoned request. Control requests accept only fixed stop/start POS and stop/start-delivery Order actions in cutoff mode, never caller-selected PIDs/executables/endpoints. All locks remain local; process recovery does not establish a distributed transaction.

## Required matrix

| Case | Evidence |
| --- | --- |
| Real cashier placement | PKCE, tenant/branch denial, authoritative quote, Inventory/Kitchen and placement replay |
| Paused delivery | Actual settlement replay/conflict and immutable receipt; zero initial POS/Reporting delivery |
| Close/review | Stale close rejected; pending review blocks; manager-only decision; undelivered evidence remains gaps |
| Actual delivery | Retained outbox restart, true consumption, late drawer review and refreshed generations |
| Accountant/security | Read-only controls, CSRF, tenant and independently authorized owning-manifest routes |
| Replay/contention | Exact completed replay, changed-payload conflict and two real managers with one winner |
| Partial restart | One Order capture persists, Payment remains uncaptured, original command resumes after expiry |
| Replacement | Second manager replaces expired partial work and old operation cannot complete |
| Late actual sale | Second quote/place/settlement causes late cash/projection, invalidates readiness and preserves old manifests |
| Source seals and late changes | Manager seals reviewed Ready cutoff; subsequent historical sale journals changes and blocks readiness while preserving original source seals |
| Manager approval | Manager approves the explicitly loaded ready seal; accountant POST is denied; the later historical sale supersedes approval while preserving its original decision/snapshot |
| Transport outage | Source failure clears displayed readiness; explicit recapture restores it |
| Source revocation | Live Payment read denial blocks evidence; restored authority permits new capture |
| Live revocation/proof | Prepare/read/membership revoked; no extra POS writes; durable private counts and granted decisions verified |

Private `cutoff-proof` reads only run-owned tables and verifies cutoff version/audit equality, operation/lease counts, retained source counts and historical granted Authorization decisions. Final `cashier-proof` in cutoff mode requires two Paid receipt-backed Orders, settlements, actual POS/Reporting projections and sale movements, one closed drawer and three real reviews. It exports bounded counts/status only, not actors, decisions, source IDs or financial bodies. Synthetic delivery/late-cash fixture actions remain rejected. Proof is independently owned reads, not a global completeness certificate.

Services keep shared safe JSON/OTLP and validated correlation. Query `nexaconnect-order`, `nexaconnect-pos`, `nexaconnect-reporting`, `nexaconnect-payment`, `nexaconnect-customer-bff` for `Day-cutoff`/`Day-close`, then narrow by `CorrelationId`. Direct commands use `cashier-<run>`. Portal requests retain normal BFF correlation. Logs never contain tokens, cookies, financial bodies, unrestricted subjects or secrets.

## Verification and remaining gates

Four launcher/settings/evidence guards and nine cutoff-host preflight rejection cases accompany the browser matrix. Control preflight covers four owned actions, two old-mode Order denials and six malformed-request denials without infrastructure. The original eight host-preflight cases remain valid. Required checks include cutoff unit/domain/HTTP tests, the original cashier/preparation guard suites and the full joined execution. See [execution evidence](../Architecture/Evidence/Cashier-Day-Cutoff-Acceptance.md) for actual results, separately from the implemented CI workflow.

Monotonic source revisions, immutable seals and late-change journals are implemented. Financial write prohibition, global delivery watermarks, finalization/approval policy, production least privilege/TLS/backup/capacity, provider commands and physical/offline POS remain separate work. This gate validates online manual-cash retained evidence, source sealing and authenticated recovery paths.
