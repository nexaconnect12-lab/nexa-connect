# POS UX18 — guided cashier acceptance and targeted hardening

## Delivered scope

UX18 adds `Worklist` to the existing installed-target POS acceptance tool. It projects the
same seventeen cases into six groups with prerequisites, procedures, expected results and
a suggested next action. It creates no new pack, records no observations, and writes no
report or application data. Existing target validation still runs; lifecycle lock files may
be opened by the existing helpers. No POS installation, service restart, migration, driver
activation, payment or authentication-policy change is required.

This slice prepares guided testing. Actual cashier, phone and hardware observations remain
required; confirmed defects must be corrected and retested before claiming acceptance.
The existing [UX9 procedures and evidence rules](POS-UX9-Acceptance.md) remain authoritative.

## Start the installed-target worklist

From the repository root in PowerShell, using the current pilot Windows account:

```powershell
$acceptanceId = '28b7ee78ac2245119c0fa1d6b6d260f3'
$work = ./scripts/pos-acceptance.ps1 -Action Worklist -AcceptanceId $acceptanceId | ConvertFrom-Json
$work | Select-Object bindingValid, qrCandidateFresh, next, operatorCasesComplete
$work.cases | Format-Table id, status, group, nextAction
$work.cases | Where-Object id -eq $work.next.caseId | Format-List
```

This ID belongs to the retained UX17 installation. After an intentional deployment, prepare
a new pack using UX9 and substitute its ID; do not rebind or overwrite historical evidence.
An invalid installed binding produces `repair-binding` with no selected case. Restore/check
the target before resuming passing observations. A stale QR candidate is reported separately
and does not silently invalidate historical installed observations or grant fresh-source readiness.

The suggestion prioritizes open defects, failed cases, pending observations, then blocked
prerequisite reviews, retaining the documented case order within each priority. It is guidance,
not permission to interrupt services or charge a payment. Other prerequisites still apply.
Resolved defects return to pending through the existing Resolve action and require a new retest.
When all cases pass, `next` is null; release/production readiness still remains false.

## Execute in manageable groups

[UX19's first walkthrough batch](POS-UX19-Cashier-Walkthrough.md) provides ordered sign-in,
idle lock/resume, synthetic cash, PDF/reprint and supplemental normal close/reopen checks.
It uses this same pack; ordinary close/reopen does not pass interrupted-payment recovery.

| Group | Cases and preparation |
| --- | --- |
| Access | Cashier sign-in, idle lock/resume and genuine session expiry/fallback. Use a separate identity test context for expiry; preserve retained policy and recovery. |
| Payments | Cash, manual PromptPay and Omise sandbox original-order settlement. Use synthetic orders and established authorized test proof; no production cards or real bank transfer is requested by this guide. |
| QR ordering | Cash and manual PromptPay collection after staff acceptance, one Kitchen delivery and separate iPhone/Android guest histories. |
| Recovery | Restart with pending payment and controlled network uncertainty in a separate isolated test run/stack. Preserve original method, amount and idempotency identity; verify one final outcome. |
| Receipts | PDF/reprint and print-failure recovery; confirm Paid state survives printer failure. Physical paper output requires a separate observation. |
| Equipment and privacy | Actual scanner, two screens, locked galleries, history privacy and permission/scope denial. Block unavailable equipment checks. Drawer opening remains disabled. |

Start with **cashier-signin**. Open POS normally, authenticate as the intended original cashier,
and check the configured branch/terminal and retained custody. Only after observing the result,
record it using UX9's Record command and a private UUID evidence reference. Generate a UUID
to index actual evidence; generating a UUID alone is not proof. Record failed or blocked when
appropriate, open defects for confirmed issues, and rerun Worklist after each observation.
Do not share credentials, payment tokens, bank references, guest QR tokens or unrestricted
screenshots in the record or chat.

## Verification and remaining work

Run `pwsh -NoProfile -File scripts/test-pos-acceptance-guards.ps1` for synthetic projection,
priority, binding, retest, record-immutability and export-privacy checks. These do not mark any
operator case passed. No runtime code changed, so UX18 does not request a replacement POS
package or relabel UX17's earlier passing source-gate receipt as current-source verification.
Tooling source changes can make QR candidate freshness false; a fresh source candidate remains
a separate requirement before release.

All actual operator cases, phone acceptance, printer/scanner/display observations and any
confirmed-defect correction remain separate from the delivered worklist. Physical drawer
opening still requires confirmed printer model, drawer connection and compatible commands.

Local verification on 31 October 2026 passed all 49 acceptance/worklist guard assertions,
PowerShell syntax checks and 657 local links across the seven reviewed documentation files.
The installed-target Worklist check returned a valid binding, seventeen pending cases and
`cashier-signin` as the next observation. The acceptance record SHA-256 remained unchanged.
`qrCandidateFresh=false` accurately reflects the tooling source change; no operator pass or
fresh source-gate result was recorded.
