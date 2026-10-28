# Windows first-draft terminal (slice 4)

Current UX5 clients optionally save a Windows printer and 80 mm layout under Terminal & sync.
Standard layout and Ask each time remain defaults. Use the labelled non-financial test
receipt before printing a paid receipt; driver/PDF/output acceptance is separate. See
[printer setup and custody](POS-UX5-Receipt-Printing.md).

Current POS builds include [Orders history and QR row selection](POS-Order-List.md).
Use matching Order/POS versions and the existing immutable package/source/scope checks.
Viewing is available with branch read access; collecting still requires the original
cashier shift/admission and retained-operation rules. Updating preserves the selected
default or isolated local-pilot recovery directory.

Next-iteration source includes [Phase 1 authenticated idle resume](Next-Iteration-Phase-1.md).
The original first-draft package is unchanged. New builds require provider UserInfo and
retain the original operator in a lock marker; legacy retained work without that binding
must be reconciled with the compatible original installation before adoption. Existing
configuration/database install preflight does not certify operator binding or actual UI use.

Slice 8 corrects clean-checkout packaging: empty `git status` output is represented by the shared candidate's Boolean dirty flag rather than a nullable string's length. Before/after revision, executable hash and dirty state must agree. The terminal guards exercise a real disposable clean/changed Git checkout; local sandbox restrictions may require running that fixture with normal Git filesystem access. See [current preparation evidence](../Architecture/Evidence/First-Draft-Slice-8.md).

For final testing use the [Slice 7 acceptance and handover](First-Draft-UAT.md). New package manifests include `sourceInputHash`; packaging rejects executable input or HEAD changes during publish. Older packages retain the existing install checks, but cannot bind to a new UAT record without that hash. The record does not replace actual installation, recovery, session or saved-PDF verification below.

Use one Windows x64 PC, PowerShell 7.4+, .NET 10 SDK to package and .NET 10 **Windows Desktop
Runtime** to run. Start/provision the [owned stack](First-Draft-Test-Stack.md) first. The
terminal uses THB with Asia/Singapore branch dates and manual cash/fictional PromptPay.
No real transfer, fiscal receipt, physical receipt-printer, drawer or scanner is certified.

## Package, install and update

### QR pilot in the current Windows account

A newly provisioned pilot must not open an earlier terminal's default recovery. The
explicit QR stack profile now generates `Pos:UseIsolatedLocalPilotStorage=true`. It selects
`%LOCALAPPDATA%\NexaConnect\POS-Pilots\<scope-hash>` for database, outbox and protected
tokens/lock markers, leaving `%LOCALAPPDATA%\NexaConnect\POS` untouched. The hash is stable
for organization/branch/store/terminal; see [storage details](../Database/POS-Local-SQLite.md).
The setting is restricted to the generated loopback draft realm and manual tender. It
does not clear or reconcile the earlier terminal's pending work.

For a new isolated pilot, package its provisioned Ready run as below. If the normal
DraftTerminal installation already represents another scope/storage selection, choose a
separate owned installation, for example:

```powershell
./scripts/first-draft-terminal.ps1 -Action Install -PackageDirectory <new-package-path> -InstallDirectory "$env:LOCALAPPDATA/NexaConnect/QrPilotTerminal" -RegisterProtocol
./scripts/first-draft-terminal.ps1 -Action Status -InstallDirectory "$env:LOCALAPPDATA/NexaConnect/QrPilotTerminal"
./scripts/first-draft-terminal.ps1 -Action Launch -InstallDirectory "$env:LOCALAPPDATA/NexaConnect/QrPilotTerminal"
```

Close the previous POS first. Both profiles retain the same existing single-instance and
callback ownership controls; this does not support simultaneous cashier windows. A package
update cannot change storage selection. Install directories cannot overlap either `POS`
or `POS-Pilots` recovery roots. After the client passes read-only preflight, primary startup
can register its own callback and request fresh cashier sign-in. Never move/copy/delete
the earlier data to bypass a scope error. Hosted installation cannot enable this option.

Source changes require the [same-scope QR refresh procedure](QR7-Pilot.md) before a new
candidate is configured/activated. Do not use stale source/certificate/activation evidence.

### Normal package workflow

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
public client, currency, default tender or local-pilot storage selection. Endpoints may be updated within the same local
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

Those original normal-draft checks keep their default recovery path. The explicit QR
pilot option above is an alternative for the current Windows account, with its own new
source/configuration/package proof; historical default-profile receipts do not certify it.
`verify-pos-cashier-live-acceptance.ps1` and `verify-pos-cash-review-live-acceptance.ps1`
still inspect the original `POS` directory. For isolated pilot diagnostics invoke the
[read-only inspector](../../src/Tools/NexaConnect.PosLocalStateInspector/README.md) with the
selected `POS-Pilots/<scope-hash>/pos-state.db` path, then separately perform/record the
interactive QR cashier/PDF checklist. A successful diagnostic is not a financial/device pass.

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
