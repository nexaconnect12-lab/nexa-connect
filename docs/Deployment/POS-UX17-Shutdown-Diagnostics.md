# POS UX17 — bounded shutdown and lifecycle diagnostics

## Status and diagnosis boundary

UX17 hardens confirmed synchronous-cleanup failure paths and adds bounded local lifecycle
diagnostics. The complete source gate passes and the matching local pilot is activated on
31 October 2026 with twelve Ready services. A controlled startup-authentication close/reopen
check passes; actual operator/device acceptance and the historical root-cause diagnosis remain
separate.

An isolated reproducer against the prior implementation hangs when an HTTP cleanup dependency
waits for application exit while its disposal runs on the closing dispatcher. Separate tests
inject throwing disposal and blocked/throwing cancellation callbacks. These establish concrete
failure paths; they do not establish which dependency caused the previously observed installed
UX15 windowless process. The original real-environment diagnosis remains open until an actual
observed failure is correlated with the new diagnostics. No process is automatically killed.

## Current behavior

- The actual staff workspace's Closed event requests application shutdown independently of
  auxiliary windows or a reassigned WPF MainWindow. Closing an auxiliary window alone does
  not shut down the staff workspace. Cancelling an ordinary staff Close request retains the running application.
- Window close requests cancellation immediately using asynchronous cancellation callbacks,
  fences optional gallery/menu/history reads and detaches their cancellation sources before
  clearing UI projections. Existing generations reject late results. Timers and subscriptions
  stop on the dispatcher. No cleanup path deletes or resets financial recovery or credentials.
- Non-UI client/resource disposal runs as independent thread-pool tasks. Failure or blocking
  of one resource does not hold the dispatcher or skip unrelated cleanup. Display disposal
  remains on the UI thread; customer cleanup runs even if hook removal fails.
- Application exit fences authentication before asynchronously disposing it. Disposed/closing
  identity cannot install late credentials; the original cashier binding remains. Callback
  accept/read is cancelled without waiting on the dispatcher. Single-instance ownership is
  released through its existing UI-thread lease.
- There is one 500 ms background-cleanup grace period and one 250 ms diagnostic flush budget.
  Incomplete background cleanup is recorded as Pending; these are not deadlines for every
  possible Windows/driver/UI operation. There is no Environment.Exit, watchdog kill, automatic
  financial retry, sign-out, shift closure or payment cancellation.
- Display-change notifications queue dispatcher work instead of waiting synchronously on it.
  Disposed/shutting-down coordinators ignore late notifications; display safety checks remain.

Closing does not make an uncertain payment unsuccessful. Reopen with the original cashier
and reconcile the original saved order/payment identity. Existing SQLite/DPAPI records,
idempotency keys, outbox work, preferences and gallery caches retain their formats/ownership.
Unsaved in-memory carts gain no new persistence guarantee. No service route, schema, role,
session timeout, payment policy or physical drawer authority changes.

## Safe lifecycle diagnostics

The current terminal's storage directory contains `diagnostics/pos-lifecycle-<pid>-<random>.log`.
Logs use JSON lines with only version, service `nexaconnect-pos-client`, category `lifecycle`,
processId, fixed enum stage/outcome, elapsedMilliseconds and observedAtUtc. No exceptions,
messages, tokens, cookies, URLs, business identifiers, names, bank references or financial
payloads are serialized. The records are diagnostic metadata, not an audit ledger or proof of
financial completion.

At most 96 records are queued per process, a file is capped at 64 KiB, and best-effort retention keeps the current file plus the newest seven previous matching
lifecycle files per terminal directory when writable. Writes are serialized off the dispatcher;
link paths are rejected. Unavailable storage fails silently and cannot prevent closing. There
is no automatic export or upload. Installer preflight does not start tracing or change recovery.

Inspect the newest local file with `Get-Content -LiteralPath <log-path> -Tail 40`. Filter stage
WindowClosing/WindowClosed/ApplicationExit and outcomes Begin/Complete/Fault/Pending; compare
processId with the OS process list. The last Begin can identify the incomplete stage. A completed
ApplicationExit record means the hook finished, not proof that the OS process terminated.
Verify disappearance externally. A missing record can also mean unavailable storage or the
bounded flush expiring; it is not evidence of successful/failed financial work. Preserve logs
and original recovery before investigating a remaining process; never dump credential memory.

## Verification and rollout

`scripts/test-pos-acceptance.ps1 -ShutdownOnly` runs isolated child processes with temporary
synthetic owner/shift/checkout/payment/outbox state. Cases cover idle, stalled callback, refresh,
sign-in, blocked/faulted disposal and cancellation, queued display notification, reassigned
main window, auxiliary-only close, unavailable diagnostic storage, and a cancelled close followed by an explicit close. They verify normal
exit, original financial SQLite bytes/identities/queue, duplicate exclusion and mutex reacquisition.
The diagnostic tests validate bounded records, the exact field allow-list and exit/fault phases.
Existing authentication tests cover cancelled sign-in/refresh and rejected late credentials.

Use the full source-bound release gate before packaging and a matching local pilot refresh.
Finish active work and close POS before installation. Preserve original configuration, orders,
shifts, approvals, certificate bytes and protected recovery; no migration is required. A controlled
locked close/reopen check can verify installation without business commands, but cannot replace
actual original-cashier, long-running, both-display and real-driver operator acceptance.

## Measured source verification

Candidate `b0a60ca1-f02e-4e9b-9718-94d0825ffee0` passes the complete source-bound release gate:
1,390 unit, seven architecture, 136 HTTP and 90 frontend unit tests; 75 QR, 34 portal menu/gallery
and 45 financial browser cases; native POS build/input/session/shutdown/printing fixtures;
hosting/profile/evidence guards; thirteen owning persistence cases with broker recovery/cleanup
and four drawer-authority cases with cleanup. Both dependency audits pass. The expanded native
fixture passes thirteen shutdown scenarios plus field/record/size/rotation checks; all 28 focused
authentication tests also pass. No live POS, identity, financial or device actions were performed
by these fixtures. The pre-fix controlled blocking-disposal failure is retained separately in
`.runstate/ux17-reproduce.private.log` and is not a historical production root-cause claim.

## Retained local activation and controlled verification

Matching package `163e5f2adc5e45cea60e42cebb368479` is installed and validated against the
current terminal inventory; the complete gate independently matches its full source inventory.
Fresh QR pilot `cb940b70b30940e780ca6c5c604a0bde` retains the original HTTPS origin and
certificate bytes. All eleven application databases were backed up. Eight protected local files
remain byte-identical before initial POS launch. Original business columns/data compare
unchanged across 30 Catalog, 54 POS, 10 Restaurant, 31 Order and five Media tables (migration/outbox/
inbox metadata excluded). Scope, service URLs, terminal configuration and decrypted credential
values match the backup; original encrypted secret bytes were restored after comparison.
Catalog9 runtime grants, safe PNG delivery and wrong-workload denial pass. No migration,
preference reset, public DNS/certificate change, financial command or physical device action
was required. The prior UX16 process was already absent after user-confirmed closure; no
process termination was needed for this activation.

The user authorized an initial startup-authentication close/reopen check without orders or
payments. The tested installed process accepted a normal window-close request, disappeared
with exit code 0 in 109 ms, produced 33 allow-listed lifecycle records including WindowClosed
and ApplicationExit completion, and reopened responsively requiring authentication. Four
checked protected recovery/session/preference files remained byte-identical through both
close and reopen. A process handle was retained before closing so the OS exit status remained
queryable. The first temporary harness attempt had misclassified an unavailable exit code;
that harness behavior was reproduced with an unrelated normal-exit process and corrected.
No application source change or force-kill was used to obtain the passing result.

These are measured results for this controlled cycle, not a universal close-time guarantee.
They do not establish the cause of the earlier long-running installed UX15 remnant or pass
real original-cashier, interrupted-payment, display/driver or long-running acceptance.
Fresh operator pack `28b7ee78ac2245119c0fa1d6b6d260f3` remains bound and fresh after reopen,
with all seventeen cases pending; phone packs also remain pending. Production readiness is
not inferred. Preserve relevant lifecycle logs if a remaining process is observed again.
