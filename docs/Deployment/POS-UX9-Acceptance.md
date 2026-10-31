# POS UX9 — end-to-end acceptance and recovery hardening

## Delivered scope

UX9 adds a repeatable automated POS regression command and a separate installed-release
operator acceptance pack. It extends coverage beyond the original sixteen-case first-draft
UAT without modifying that tool or its release requirements. No application binary, service
contract, permission, migration, financial policy or physical driver changes in this slice.
The installed UX8D pilot continues running; no reinstall or service restart is required.

The pack starts with seventeen pending cases. Source tests never mark operator cases passed.
Observations, defects and verified resolutions are recorded in sequence, with UUID evidence
references and bounded status/category fields. Opening a defect fails its case; resolution
resets that case to pending, requiring a new passing observation. Any open defect or pending,
failed or blocked case prevents operator completion. All severities remain visible.

## Automated regression

[UX20](POS-UX20-Isolated-Recovery-Access.md) combines this full regression with guarded
disposable cashier and Order recovery fixtures. Its source-bound receipt requires verified
cleanup and cannot record operator cases or grant live identity/release readiness.

UX12 adds `scripts/test-pos-acceptance.ps1 -ShutdownOnly` for isolated close/reopen,
stalled callback, refresh cancellation, duplicate instance and recovery-preservation checks.
It also runs in the complete native fixture. This mode is mutually exclusive with
`-SessionExpiryOnly`; neither records an operator pass. See [UX12](POS-UX12-Shutdown-Restart.md).

Run on the Windows development PC:

```powershell
pwsh -NoProfile -File scripts/test-pos-acceptance.ps1
```

This runs the acceptance-tool guards, all unit tests with Windows DPAPI enabled, relevant
drawer/PromptPay/barcode/Orders HTTP boundary tests, and the existing isolated native input
and receipt-layout fixtures. These cover session expiry/lock policy, original payment-method
recovery, duplicate suppression, exact amounts, caller/scope isolation, gallery/display privacy,
input and printing failure behavior. WPF fixtures use temporary synthetic state and fake ports.
No retained account sign-in, provider request, real spool job, device command, retained database
write or backend interruption is performed. It does not certify an actual card payment, scan,
touchscreen, printed receipt, authentication session or network outage.

Each run writes private diagnostics and `verification.json` under `.runstate/pos-regression/`.
The receipt binds the current executable source inventory before/after the run and records
actual executed/passed counts; failed runs remain failed. It is `automated-pos-regression`,
not operator acceptance, CI or a QR6/public release candidate. No automatic retry or promotion.

### Isolated session expiry and fallback

To run only the automated session checks:

```powershell
pwsh -NoProfile -File scripts/test-pos-acceptance.ps1 -SessionExpiryOnly
```

This writes a separate source-bound `automated-pos-session-expiry` receipt under the same
`pos-regression` directory. The default full command also runs these scenarios as part of
the Windows input fixture. The fixture invokes the production session timer and real Resume
button/authentication callback paths in hidden, non-activating test windows. It uses a fake
identity HTTP handler and captured browser-launch delegate, never a real browser/protocol
registration or Keycloak request. All tokens, PKCE callbacks, custody and original pending
payment are synthetic. DPAPI/token storage and financial SQLite state use unique temporary
directories; successful cleanup is confined to the fixture parent.

Coverage includes absolute expiry while access is still valid; expired access without a
refresh token; provider refresh rejection400/401; identity503 with valid-access grace and
backoff, followed by lock after access expiry; and successful same-operator refresh without
extending the absolute boundary. Expiry must hide/disable private work and financial actions,
save a credential-free locked original-cashier marker, and retain exact shift, cash session,
pending method/proof/idempotency and financial-store bytes. A new authentication instance must
restore the locked owner. Cancellation and another cashier's callback keep it locked; the
original cashier's interactive PKCE callback restores the workspace. Any business/device HTTP
request fails the fixture. The production policy/timeouts and retained pilot remain untouched.

These are deterministic production-code tests with simulated identity responses. They do not
prove expiry/revocation in a real identity-provider session or real browser authentication.
The operator `session-expiry-fallback` case remains separate and is never marked passed by
this command. No installation, service refresh, real financial action or device output occurs.

## Prepare an installed-target pack

Use the retained run and current configured pilot IDs from `.runstate/qr9-current.json`:

```powershell
$pilot = Get-Content .runstate/qr9-current.json -Raw | ConvertFrom-Json
./scripts/pos-acceptance.ps1 -Action Prepare -RunId $pilot.runId -PilotId $pilot.pilotId
```

Prepare prints an acceptance ID. The default install path is the current Windows user's
`NexaConnect/DraftTerminal`; a separate owned test install can be selected with
`-InstallDirectory`. The private record is under `.runstate/pos-acceptance/<acceptance-id>/`.
The command checks installed file hashes, active release/manifest, configuration, scope and
profile against the owned Ready draft. It also checks service process start identity,
executable/listener ownership, backend publication hash, configured QR activation/files,
five-ID scope and origin. It never opens financial recovery or reads login secrets.

Subsequent actions recheck the installed binding. Changed active release, package, endpoints,
profile, configuration, backend artifacts or QR activation prevent a new passing observation
and invalidate current completion. Failed/blocked observations and defects can still be
recorded during an outage. Create a new pack when deliberately deploying another target.
The record retains observations for its original target; it never silently rebinds them.

The target is the **installed release**, which can predate the new acceptance tooling.
QR7's report can therefore identify `fresh-matching-candidate` as pending after tooling changes
or candidate expiry. This is exported separately as `qrCandidateFresh=false`, never converted
to a QR9 pass. Other configured-file/prerequisite failures invalidate the binding. Operator
observations about the installed release remain distinct from current-source verification:
`currentSourceVerified`, `releaseReady` and `productionVerified` always remain false. Use the
existing QR6/QR9 and release gates for fresh-source/device/public-release evidence. This tool
does not weaken or replace those gates or the [first-draft UAT](First-Draft-UAT.md).

## Execute the cases

[UX18](POS-UX18-Cashier-Acceptance.md) adds read-only `-Action Worklist` to this same pack:
grouped prerequisites/procedures and a next-action suggestion. It records no observations,
retains installed binding checks and never promotes source/release readiness.

One tester can use the existing cashier, manager, Kitchen and restricted identities. Use
dedicated synthetic test orders and record their private evidence separately. Save UUID
evidence references here, not credentials, card tokens, bank references, order bodies or
screenshots containing unrestricted data. Observe actual behavior before recording a pass.

| Case ID | Procedure and expected result |
| --- | --- |
| `cashier-signin` | Sign in as the intended cashier through the normal browser flow; verify configured branch/terminal and preserved custody/pending work. |
| `idle-lock-resume` | Allow the configured idle interval to elapse; confirm lock/screensaver without closing custody. Tap/key reveals authentication; only authenticated original-cashier resume restores private work. |
| `session-expiry-fallback` | Exercise a genuinely expired/denied session in a separate test context. Private work locks and interactive sign-in is available; saved ownership/recovery remains. Do not shorten the retained policy or edit tokens to claim this check. |
| `cash-original-settlement` | Open the intended test cash session, place one test cash sale, check exact server total and cash movement, Paid order and receipt; refresh/review must not collect again. |
| `promptpay-original-settlement` | Check the approved PNG/payee and amount, enter verified test proof, confirm once; refresh/review keeps the same original paid order with no duplicate settlement. |
| `omise-sandbox-original` | Choose the existing sandbox method before placement, use a fresh test token through the established flow, verify the original provider/order outcome and receipt. No production key or real card. |
| `qr-cash-collection` | A guest submits, staff accepts, Kitchen receives once; select the original QR order in POS and collect cash once at its server total. |
| `qr-promptpay-collection` | Repeat with a different synthetic request and verified manual PromptPay. Keep device guest histories separate and the original order identity throughout collection. |
| `restart-pending-payment` | In a separate dedicated test run, interrupt after saving an original pending request; restart/re-authenticate and explicitly recover its same method, amount and idempotency identity. Verify one final outcome. Never delete recovery to repeat it. |
| `network-loss-original-retry` | In an isolated test stack interrupt the owning dependency at a controlled uncertainty point; restore it, review/retry the original request and verify exactly one settlement/capture. Do not stop shared retained services while others use them. |
| `receipt-pdf-reprint` | Save a PDF for a paid original order, then authorized reprint. Check exact totals, tender, pagination and readability; reprint changes no payment. |
| `printer-failure-recovery` | In a test printer/profile cancel/fail printing; Paid state and recovery remain, with an explicit later reprint. Actual PAISHUO paper output remains a separate physical observation. |
| `barcode-input` | With the purchased scanner, scan a registered product once, repeat intentionally, try unknown/disabled codes and scan while entering proof/authentication. Only deliberate scan-to-cart affects the correct cart; typing and tender fields remain separate. Block if hardware is unavailable. |
| `dual-display-isolation` | Check actual Windows resolution/scaling and display assignment on both screens. Customer view shows only permitted sale/idle content; no staff authentication, proof, history or diagnostics. Check disconnect/reconnect and one-screen fallback. |
| `locked-gallery` | Wait for approved staff/customer media refresh, then lock. Confirm appropriate full-screen images, no private-work disclosure, centered authentication on interaction and safe expiry/failure behavior. |
| `drawer-history-privacy` | Load own terminal/actor history without typing UUIDs, including closed custody. Empty history is valid. Check lock/sign-out/cashier change and denied/outage clearing. Opening remains disabled; do not manufacture drawer attempts. |
| `permission-scope-denial` | Use restricted/wrong-branch identities in a separate test context; privileged data/actions are denied and delayed responses cannot restore the original cashier's private view. |

## Record observations and defects

Replace the IDs with those printed by Prepare and your privately indexed evidence:

```powershell
$acceptanceId = '<acceptance-id>'
$evidenceId = [guid]::NewGuid().ToString('N')
./scripts/pos-acceptance.ps1 -Action Record -AcceptanceId $acceptanceId -CaseId cashier-signin -Status passed -EvidenceId $evidenceId -ConfirmObserved
./scripts/pos-acceptance.ps1 -Action Report -AcceptanceId $acceptanceId
```

Use `failed` or `blocked` for an observed failure/missing prerequisite. There is no skipped,
not-applicable or automatic pass status. Do not run the example pass before observing it.

```powershell
./scripts/pos-acceptance.ps1 -Action Defect -AcceptanceId $acceptanceId -CaseId cashier-signin -Category security -Severity high -EvidenceId $evidenceId -ConfirmObserved
# After a verified correction, use the printed defect ID and new evidence:
./scripts/pos-acceptance.ps1 -Action Resolve -AcceptanceId $acceptanceId -CaseId cashier-signin -DefectId '<defect-id>' -EvidenceId '<new-evidence-id>' -ConfirmObserved
# Resolution makes the case pending; repeat its procedure and record the retest separately.
```

`Record`, `Defect` and `Resolve` require `ConfirmObserved`; `Prepare` only captures the installed
target and initializes pending cases. The switch is an operator attestation, not measured
proof or a signature. Records are current-user protected, editable local files, bounded to
256 sequential events and 4MiB input. The exported report allowlists case/status, the latest
case observation's evidence UUID/time, defect ID/category/severity/status and target IDs.
Full sequential defect/resolution evidence remains in the private record. It excludes private paths, endpoints, scope,
notes and arbitrary attachments. Binding checks are observations rather than a transaction
against concurrently changing services; keep the target frozen while testing. Legacy UAT,
QR9, hardware and release acceptance remain independently required.

## Verification and observed status

The session-expiry extension passed standalone run `1febb30059204699a11bdfe613622dbf`
on 30 October 2026 at 10:33 Singapore time. Full regression
`8c4ae242c3d542ba9c077366a83b5fd9` passed at 10:34 with 1,332 unit tests, 49 HTTP cases,
34 guard assertions and both native input/receipt-layout fixtures; its native log includes
the new session scenarios. Both source-bound receipts record no retained-pilot mutation,
provider request or operator acceptance, and retain false release/production readiness.
The fixture exercises production timer/Resume/callback code with synthetic identity responses,
not a real identity-provider/browser session. No operator case was updated, timeout policy
changed or retained pilot installed/refreshed. Earlier diagnostic `14045` remains historical
and was not relabelled as the final source run. Earlier UX9 measurements below remain historical.

Regression `939bdb2832134b6386b7cafaf420324e` completed on 30 October 2026 at 09:44
Singapore time. Its source-bound receipt passed all 1,332 unit tests, 49 HTTP boundary cases,
34 acceptance-tool guard assertions and both native input/receipt-layout fixtures. It records
`retainedPilotMutated=false`, `providerRequests=false` and `operatorAcceptance=false`;
`releaseReady` and `productionVerified` remain false. The earlier successful regression
`ea21e59e5000471fb6110f8304474364` is historical: the HTTP minimum was subsequently raised
from25 to49 and the final source was rerun completely rather than relabelling that receipt.

Prepared operator pack `a07814cc056d40519b45603e08ea711a` targets installed release
`e594bd4e2b924cdfa1e02d28529a5823` and pilot `731bea81817848838d20f5c0f38c4f3b`.
Its observed report has `bindingValid=true`, all seventeen cases pending and
`operatorCasesComplete=false`. `qrCandidateFresh=false` remains separate; it does not promote
current-source, QR9 or public-release readiness. No reinstall or retained service mutation was
performed. Real signed-in/financial/PDF/display/peripheral acceptance remains pending until
observed and recorded by the operator.
