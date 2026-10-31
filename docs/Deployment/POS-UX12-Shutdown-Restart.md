# POS UX12 — shutdown and restart reliability

UX17 subsequently reproduces and hardens blocking/faulting cleanup paths, adds bounded lifecycle diagnostics and queues display notifications. This does not establish the historical installed UX15 cause. See [current shutdown behavior and diagnostic limits](POS-UX17-Shutdown-Diagnostics.md).

## Scope and status

UX12 configures staff-window-driven application shutdown.
Callback IPC is cancellable and bounded, and authentication disposal cancels outstanding work
without deleting credentials or financial recovery. The complete source gate passed and the
matching package is installed in the retained local pilot on 31 October 2026. All twelve
services are Ready, and the updated POS reopened with a responsive window. Actual operator
close/reopen and both-screen acceptance remain separate.
The later installed UX15 shutdown remnant remains unresolved; see the
[real-environment observation](#installed-shutdown-observation-31-october-2026).
The user reports UX11 working. That report does not complete unrelated acceptance cases.

During UX11 activation an older windowless process exited independently. Its historical cause
was not captured. UX12 addresses verified lifecycle weaknesses and tests their failure paths;
it does not claim a reproduced diagnosis of that earlier process.

## Behavior and boundaries

- `App` explicitly uses WPF `OnMainWindowClose`. The staff window owns application lifetime;
  an auxiliary/hidden window cannot keep POS alive after staff close. Closing only the customer
  display retains the existing display-safety behavior.
- The same-session single-instance mutex remains held until application exit. Startup uses
  zero-wait acquisition, including abandoned ownership. An existing but unowned handle does
  not block restart. An active primary still rejects a second workspace; callback forwarding
  and maintenance guards retain their names and scope.
- The current-user-only callback pipe reads asynchronously outside the UI dispatcher.
  A peer has five seconds to finish a message, bounded to 4,096 characters. Stalled,
  oversized messages and broken-pipe failures are discarded; clean EOF remains a supported terminator.
  Only bounded messages reach the existing authentication validator. IPC never bypasses
  PKCE, state or original-cashier checks.
- Exit cancels pipe accept/read and authentication transport without synchronously waiting
  on the dispatcher. Sign-in is linked to window lifetime. Disposed authentication cancels
  pending completion and fences late credential saves. Refresh waiters cancel without
  disposing their semaphore underneath an active release.
- Window close stops existing timers, cancels window requests and invalidates optional
  gallery/menu callbacks before releasing HTTP/display resources. Status delivery is queued
  and ignored after close instead of synchronously waiting on the dispatcher.

Close is not sign-out, a financial cancellation, settlement or shift closure. Original
SQLite/DPAPI records, idempotency keys, owner binding, queue, preferences and persistent gallery
caches are preserved. Unsaved in-memory cart/edit state gains no new persistence guarantee.
Uncertain requests still require original-record reconciliation after restart. Restart still
requires the original cashier's authentication. No server schema, permission, HTTP contract,
cashier-session timeout policy or hardware-opening authority changes.

## Verification

On 31 October 2026, candidate `d2083135-dcf2-4cfe-9c16-a872268a0d80` passed the complete
gate: 1,382 unit, seven architecture, 127 HTTP and 90 frontend unit tests; 75 QR browser,
15 portal image/category and 45 existing financial browser cases; native lifecycle/input/
printing fixtures; hosting/profile/evidence guards; eleven owning database cases with broker
recovery and cleanup; and four drawer-authority database cases with cleanup. Both dependency
audits report zero vulnerabilities. No skipped/retried tests or operator passes are inferred.
Source-bound POS package `580aff0e41f94d6b9f46aa2ff423e18d` is installed.
Actual close/reopen, both-screen and retained original-payment acceptance remain pending.

Activation completed at 00:44 Singapore time on 31 October. Fresh QR pilot
`5b36e43b8d684dad9a17ac20484b5f75` retains the same HTTPS origin and certificate bytes.
Eight protected local files are unchanged before launch; all eleven pilot databases were
backed up. Comparison of 28 Catalog, 54 POS, ten Restaurant, 31 Order and five Media original
business-table COPY sections is unchanged (migration/outbox/inbox metadata is excluded).
Catalog remains at migration7, with runtime grants verified; existing layout version1 reads
successfully, wrong workload returns403 and guest layout without a cookie returns401. Safe
PNG delivery and prior configuration/credentials/image approvals are preserved. No categories,
approvals, financial actions or physical drawer commands were issued during activation.

Fresh operator pack `8002869caced4439bbbc4dbee472cf7c` has a valid installed binding and all
17 cases pending. `qrCandidateFresh=true` does not change `currentSourceVerified=false`,
`operatorCasesComplete=false`, `releaseReady=false` or `productionVerified=false`.

Run on Windows:

```powershell
pwsh -NoProfile -File scripts/test-pos-acceptance.ps1 -ShutdownOnly
```

Unique named objects, hidden fixture processes, fake identity/business ports and temporary
current-user DPAPI/SQLite state check:

1. Idle listener cancellation, partial-message timeout/cancellation, oversized-message
   rejection and subsequent bounded-message dispatch.
2. Exit after staff close with a hidden auxiliary window, a stalled writer or an in-flight
   identity refresh.
3. Duplicate exclusion, released-mutex acquisition with another handle present, and reopening
   the same recovery scope locked to the original cashier.
4. Byte-identical financial SQLite data, original pending checkout/payment identity and queue.

The full native fixture includes these checks. Authentication unit tests cover disposal during
pending sign-in, delayed code redemption and concurrent refresh/gate waiting, without late
credential installation. The full candidate gate requires at least 1,382 unit tests.
`-ShutdownOnly` and `-SessionExpiryOnly` are mutually exclusive. Receipts under
`.runstate/pos-regression/` remain automated evidence, never operator acceptance.

Fixtures perform no retained pilot writes, real identity/provider requests, printer jobs or
physical drawer commands. They do not certify forced Windows shutdown, power loss, vendor
drivers or actual two-screen hardware. Debug with private native/shutdown test logs and exit
codes; never collect callback URLs, tokens or payment references.

## Installation and operator follow-up

Prepare a fresh source-bound candidate and matching package. Finish the current POS action
and close its window before installation. Confirm the original process exited; do not silently
terminate a live or windowless retained instance. If it persists, record its verified executable
and start identity and investigate before replacement. Back up retained protected state and
databases, refresh matching bindings and preserve certificates, scope, configuration and
recovery. UX12 needs no migration.

After installation, confirm closing/reopening the real POS releases its process, retains
original work and requires the correct cashier to resume. Repeat with both screens and during
an approved read-only refresh. Actual payment interruption testing remains the separately
controlled [operator acceptance procedure](POS-UX9-Acceptance.md). Do not carry old operator
passes to a new installed target or infer release readiness.

## Installed shutdown observation (31 October 2026)

During the UX16 update, the user confirmed the POS window was closed, but the installed UX15
process remained running with no main window. The exact prior-release process was verified
and stopped for the authorized update; all eight protected local files remained byte-identical.
This is an unresolved real-environment shutdown observation, not a defect fixed by the flat
presentation change. Existing synthetic shutdown/session tests pass, but do not complete the
real operator shutdown/reopen case. Investigate retained process lifetime and safe shutdown
separately; do not reset recovery or infer successful closure merely from a hidden window.

See the [UX16 rollout record](POS-UX16-Flat-Cashier.md) for preservation and installed-target status.
