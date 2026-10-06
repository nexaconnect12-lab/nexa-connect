# Joined day-close preparation acceptance

The guarded runner joins real Keycloak Customer sessions, the published Customer BFF/Portal, POS preparation, Reporting, and independently authorized Order/Payment/POS sources. It adds a preparation mode to the existing disposable end-of-day infrastructure; old financial and read-only gates retain their pinned schemas and scenario counts. No preparation response or source body is mocked.

## Run and topology

Prepare .NET 10/Node 20.19+ dependencies and Chromium, then run from repository root:

```powershell
./scripts/test-day-close-portal.ps1 -ConfirmDisposableInfrastructure
```

Run `npm run test:day-close:guards` from `src/Frontend` first. The explicit flag authorizes only generated localhost infrastructure, synthetic fixture transitions, retained-event delivery, exact child-process interruption/restart and cleanup. Local Docker sockets only; the generated project must be empty. `-NoBuild` requires current Debug fixture/service binaries; the recovery tool and published BFF/portal still build. After dependency changes restore before using the runner; end-of-day/preparation modes use `--no-restore`.

The shared `docker/end-of-day-portal` Compose topology creates fresh PostgreSQL 17 application data in tmpfs, separate Keycloak 26.7.0 data and RabbitMQ 4. Seven service databases migrate through the actual runner: Platform Directory 3, Restaurant 3, Authorization 10, Order 11, Payment 10, POS 8, Reporting 20, compatibility 0.24.0. Migration and per-service runtime identities are distinct; broad disposable fixture grants are not production least-privilege certification. Eight application processes run on generated IPv4 loopback ports. Three actual OIDC subjects have distinct passwords and memberships in two organizations: accountant at branch scope, and two store-manager assignments at Restaurant scope. Realm customer-viewer transport roles do not grant product preparation permission.

Customer BFF points to actual POS; POS preparation points to the Reporting TCP proxy, and Reporting's source summary calls actual POS without recursion. The second proxy forwards Reporting-to-Payment. All proxies bind only generated loopback ports, never inspect/fabricate responses, and have no control HTTP route. Browser requests are restricted to generated BFF and identity origins. Shared structured telemetry/correlation stays enabled on the actual hosts. No production debug endpoint, feature switch, new migration or financial contract is added.

## Fixtures and state transitions

The fixture picks yesterday in Asia/Bangkok, retaining the existing THB 100 Order receipt/sale event, captured Payment intent and THB 25 refund receipt/event. Initial historical POS snapshots include one closed drawer with THB -5 variance plus an older open shift/session. Unlike the read-only gate, preparation mode adds no uncertain payment/refund or unresolved Order: its initial operational blockers are POS-owned, and delayed real projection delivery/no recorded check are financial blockers.

`resolve-day-close` uses POS cash-session closure, Shift aggregate/store and cash-review decision repositories to close the older session/shift and approve the current nonzero variance. Initial historical fixture SQL remains confined to freshly initialized run-owned stores; existing financial history is not rewritten. These synthetic repository outcomes do not test cashier close/review HTTP authorization. Preparation itself always runs through real customer BFF/POS HTTP and fresh Authorization decisions.

`deliver` dispatches retained original Order/Payment outboxes through actual RabbitMQ/Reporting consumers; no Reporting facts are inserted by the fixture. `record` invokes the real exact-window financial recovery recording CLI. Ready remains a fresh preparation observation, never a historical check promoted to settlement certification.

`same-total-evidence` loads and saves the original completed Order through its owning repository. This advances owner concurrency/update metadata while preserving amounts, status, immutable receipt and retained publication. It tests conservative fingerprint invalidation after a metadata change, not another financial event or an identity swap. `late-cash` projects a synthetic valid manual-tender integration event through the POS-owned settlement projection repository into the matching historical closed drawer; it bypasses an Order command/event-production/broker delivery test. That recipient projection increments the financial version and changes variance to THB -6, requiring renewed review. Separate cashier/manual-tender/provider gates cover event production and financial commands. `approve-cash` approves the new current version through its owning repository.

## Eleven mandatory browser scenarios

1. Real manager prepares Blocked, resolves repository/source blockers, delivers retained events and records historical evidence, then refreshes to Ready.
2. Accountant can read but cannot prepare; missing CSRF, denied branch and foreign tenant cannot mutate state or display readiness.
3. Exact completed replay preserves state/audit; changed payload under the same operation conflicts.
4. Two independently authenticated managers race at one version; one succeeds and the other conflicts.
5. Killing/restarting the retained POS process preserves Ready snapshot/version and exact replay.
6. Holding actual Reporting transport proves Preparing committed before killing POS. The original actor resumes the persisted command after lease expiry.
7. A second manager replaces expired interrupted work; the original operation is permanently superseded.
8. Same-total Order metadata evidence and late closed-drawer cash each invalidate saved readiness; explicit review/refresh recovers it.
9. Real Payment outage and held Reporting transport exercise Blocked/no stale Ready, source deadline and recovery.
10. Revoking a manager's live owning-source permission invalidates the existing Ready observation; restoring the synthetic grant enables explicit refresh.
11. Revoking preparation/read product grants or suspending membership rejects existing sessions without additional preparation audit.

The browser uses only session-cookie BFF requests and antiforgery tokens. No bearer reaches the browser or command line. Each fixture command validates opt-in, run ID, distinct UUID subjects, exact localhost database names and fixed repository executable paths. New actions refuse non-preparation modes.

## Restart control and database assertions

The runner alone retains POS process handles/startup configuration. The fixture writes run-scoped `pos-control/request.json` with an exact run ID, 32-hex request ID and only `stop-pos`/`start-pos`. The parent validates a 4096-byte bound and exactly three fields, deduplicates requests, then stops its sole live retained POS handle or starts the same fixed assembly/configuration/port. A matching acknowledgement staged through a temporary file permits the browser to continue. No PID, executable, port, connection string, token or arbitrary command is accepted. Every restarted handle joins the normal finally cleanup list. The browser child is bounded to 12 minutes; individual fixture commands are bounded to 3 minutes and browser scenarios to 180 seconds. Actual 30-second leases are exercised without clock bypass.

`preparation-proof` privately checks state version equals the retained audit count and the number of referenced decision entries, and every referenced distinct decision is a real granted decision for the run-owned organization. It returns only bounded state/version/count/lease-remaining metadata. Browser comparisons verify no duplicate replay/conflict/denial audit, one race winner, retained frozen snapshots, resumed pending operation completion and abandoned replacement history. Broader immutable-audit/downgrade/hash-cap controls retain their separate PostgreSQL tests.

## Evidence and release limits

All six Authorization persistence regressions and all eleven named browser scenarios must pass without skips, retries, duplicates or replacement titles. The safe reporter and four settings guards fail closed. Require successful runner exit plus `passed`, `authorizationPassed`, `cleanupVerified` in `.runstate/day-close-portal/<run-id>/verification.json`, and `verified=true/11 passed/11 total` in `src/Frontend/test-results/day-close-live/<run-id>/summary.json`.

Cleanup terminates exact retained application/browser/restarted POS handles, removes the exact generated Compose project/volumes and certificate, verifies no project containers remain, and restores runner environment. Failure fails the gate. Raw fixture/control state, TRX, service/child logs, environment and certificate are local restricted diagnostics and excluded from CI upload; only bounded verification/summary JSON is retained. Traces, screenshots and video are disabled. Forced external interruption can bypass finally; never reuse a partial fixture.

The CI workflow uses Ubuntu, .NET 10 and Node 22 and uploads only bounded summaries for 14 days. Remote execution/branch protection remain separate evidence. Production TLS/least privilege/capacity/timezone/clock/latency, external providers, physical/offline POS, financial source command/event-production proof, distributed cutoff certification and settlement approval/locking remain outside this acceptance. See [preparation API](../API/Day-Close-Preparation.md) and [rollout](Day-Close-Preparation.md).

Local publish preparation reported four npm dependency advisories (one moderate, one high and two critical) and existing Vite chunk-size warnings. This slice changes no packages or lockfile. Dependency triage and bundle optimization remain separate follow-up work; these warnings do not establish production exploitability or a production vulnerability assessment.

## Local execution evidence — 2026-10-06

Windows run `14f563dc51d44f929d9bb2bcef37ee60` passed all eleven named browser scenarios without skips/retries and all six Authorization persistence regressions. Its bounded `verification.json` records `passed=true`, `authorizationPassed=true`, `cleanupVerified=true` and `productionVerified=false`; browser summary records `verified=true`, `passed=11` and `total=11`. Verification completed at `2026-10-06T03:14:52.9504923Z` against base commit `31f2476e46bf8b46212b9997a8425fa4bf0e4161` with `sourceDirty=true`: this is working-tree evidence, not a committed-release or production pass. The matching local files use the exact run paths described above. Remote CI remains unexecuted.

See [the joined acceptance evidence handoff](../Architecture/Evidence/Day-Close-Joined-Acceptance.md) for the exact preparation and read-regression run records, broader verification, documentation inventory and remaining release gates.
