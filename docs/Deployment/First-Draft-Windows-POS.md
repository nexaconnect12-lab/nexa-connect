# Windows first-draft terminal (slice 4)

For final testing use the [Slice 7 acceptance and handover](First-Draft-UAT.md). New package manifests include `sourceInputHash`; packaging rejects executable input or HEAD changes during publish. Older packages retain the existing install checks, but cannot bind to a new UAT record without that hash. The record does not replace actual installation, recovery, session or saved-PDF verification below.

Use one Windows x64 PC, PowerShell 7.4+, .NET 10 SDK to package and .NET 10 **Windows Desktop
Runtime** to run. Start/provision the [owned stack](First-Draft-Test-Stack.md) first. The
terminal uses THB with Asia/Singapore branch dates and manual cash/fictional PromptPay.
No real transfer, fiscal receipt, physical receipt-printer, drawer or scanner is certified.

## Package, install and update

```powershell
./scripts/first-draft-terminal.ps1 -Action Package -RunId <run-id>
# Use the exact terminal-packages/<release-id> path printed above:
./scripts/first-draft-terminal.ps1 -Action Install -PackageDirectory <package-path> -RegisterProtocol
./scripts/first-draft-terminal.ps1 -Action Status
./scripts/first-draft-terminal.ps1 -Action Launch
```

Package holds the draft lifecycle lock and requires a provisioned Ready run. It freshly
publishes framework-dependent win-x64 WPF output in a new run-owned release directory,
copies retained settings and any test QR asset, and records source commit/dirty status,
run/release identity, exact scope and SHA-256 file inventory. This is a local review artifact,
not a signed production installer; retain the repository/package provenance. Source/runtime
dependencies and a suitable local certificate must already meet the stack prerequisites.

Install defaults to `%LOCALAPPDATA%\NexaConnect\DraftTerminal`. `-InstallDirectory` may
select another separate path, including a disposable verification path. Links/junctions,
changed/missing/extra package files, unsafe settings and an overlapping recovery/package
directory are rejected. A nonempty destination without the installer ownership marker is
rejected before ACL or application changes; only an owned partial or active installation
may be resumed. Close POS before updating. A per-install lock prevents concurrent
updates. Each immutable release is copied separately; read-only `--validate-install` checks
configuration, current-user DPAPI scope, schema/integrity and pending-checkout compatibility
before an atomic `active.json` pointer replacement. Status and Launch verify file hashes.
Install does not launch the cashier workspace or authenticate. `-RegisterProtocol` registers
the exact executable for this Windows user; normal primary startup also registers it.
Secondary callback processes only forward the bounded callback and do not change registration.
Primary startup registers only after configuration and protected recovery open successfully;
a wrong-scope startup cannot replace the original terminal's callback registration.

Updates refuse changes to organization/restaurant/branch/store/terminal, identity realm,
public client, currency or default tender. Endpoints may be updated within the same local
realm/scope. Preserve the old release and active pointer if validation fails. Do not force
a prior binary after a recovery schema upgrade; review compatibility and roll forward.
This draft has no automatic uninstall, rollback, data purge or local-state portability.
Legacy recovery without a current scoped database requires review with its original client.

`%LOCALAPPDATA%\NexaConnect\POS` stays separate and is never copied, overwritten or deleted
by installation. Its schema-2 SQLite database, WAL, DPAPI-protected payloads and credentials
belong to the current Windows user/device. Missing runtime, corrupt state, wrong scope or
failed protection must block activation/use; never delete recovery to make a test pass.
Resetting the backend run does not clear terminal recovery. Finish/reconcile work first.

## Recovery and receipt behavior

The existing client persists original checkout and settlement identities before sending,
locks replacement work, and offers original-order/payment verification after restart.
Startup now names the pending original Order explicitly. Cash totals/change, bill
confirmation, manual PromptPay reference/confirmation, receipt lookup and reprint reuse
the existing controls. Every new manual settlement now requires [POS terminal admission](../API/Terminal-Admission.md).
An invalid terminal or different cashier's shift cannot create a new Paid receipt.
Exact committed replay remains recoverable after closure. No negative movement substitutes
for a manual-tender refund, which remains unsupported.

Paid receipt printing reloads live authorized server evidence. Select **Microsoft Print to
PDF** in the normal Windows dialog and save outside private recovery/run directories.
Cancelled dialog and submitted-to-Windows states now explicitly preserve payment and
instruct verification/reprint of the same receipt. Spool submission does not prove a PDF
was saved or a printer completed its job. Receipt/print errors never collect another payment.

## Verification and interactive gate

```powershell
./scripts/test-first-draft-terminal-guards.ps1
# Backend-only acceptance is independent of the Windows profile:
./scripts/test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure
# Optional combined package check requires compatible or empty current-user recovery:
./scripts/test-first-draft-checkout.ps1 -ConfirmDisposableInfrastructure -VerifyTerminalPackage
```

The joined runner owns a fresh stack, packages/installs into its run directory, repeats
installation, validates status, performs real-identity checkout/denial/recovery checks and
resets its infrastructure. It never registers a test callback, launches the UI or mutates
existing user recovery. Its actual-executable preflight reads the current user's recovery;
unrelated retained scope or a running POS correctly blocks this disposable install check.
Use a separate approved Windows test profile for fresh-scope package validation when the
normal profile retains another terminal's recovery. Preserve that recovery; do not delete,
copy or replace it to satisfy this check. The recorded isolated application-container
profile install/reinstall/preflight proof is separate from acceptance on the user's PC
profile and supplies no UI, callback or PDF result.
Retained test artifacts reference a retired backend after cleanup and must not be used for
cashier testing. See [executed evidence](../Architecture/Evidence/First-Draft-Slice-4.md).

The user must run and record these interactive checks on the designated PC; authored
steps and backend checks do not count as WPF/device acceptance:

| Check | Expected result |
| --- | --- |
| Launch/sign-in and cancel sign-in | One primary window; PKCE returns through installed callback; cancel/late callback cannot install credentials |
| Active use past access-token expiry | Silent renewal preserves work; no token or password appears in logs |
| Five-minute idle lock, restart while locked and fresh login | Receipt clears; authenticated controls lock; protected checkout/shift survives; interactive login required |
| Checkout interruption before/after send and lost response | Restart offers original Order/payment identity; verify it, never collect again or create a replacement |
| Install an update while pending work exists | Same scope passes read-only preflight; original command survives restart; another scope fails |
| Cash and fictional PromptPay | Exact server bill, cash/change shown; PromptPay needs explicit fictional receipt/reference confirmation |
| PDF print and reprint | Inspect saved PDFs for same receipt number, Order, items, THB amounts and totals; no clipping or omitted lines |
| Cancel print; unavailable printer or cancel PDF Save | Paid remains final; load/reprint the same receipt; no new settlement/collection |
| Accountant/other scope | Unauthorized operations denied by services; no unauthorized receipt access |
| Close and sign out | Wait for projections; reconcile/close drawer and shift; sign out clears credentials, no unresolved work discarded |

Record build/run/release IDs, each pass/fail/pending, sanitized Order/receipt IDs and PDF
inspection result. Never attach credentials, tokens, private journal/database or raw logs.
Device tests remain pending until executed; independent staff feedback is deferred because
the user is the sole tester. Slices 5–7 retain manager/next-day/release/UAT acceptance.
