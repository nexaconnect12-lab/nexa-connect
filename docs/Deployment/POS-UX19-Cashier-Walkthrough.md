# POS UX19 — cashier walkthrough and confirmed-defect fixes

## Scope and status

UX19 executes the existing [UX18 worklist](POS-UX18-Cashier-Acceptance.md) in small operator
batches, starting with sign-in, idle lock/resume, one synthetic cash sale, PDF/reprint and
ordinary close/reopen. It adds no acceptance tracker, runtime feature or migration. A
correction is implemented only after a failure is observed and reproduced. Preparing this
guide does not complete UX19 or pass any of the seventeen acceptance cases.

Use the same Windows account and retained installation. Keep the installed target frozen
while testing. Do not delete protected recovery, create replacement payment requests to
escape uncertainty, change session policy or enable physical drawer commands.

## Check the target

From `D:\DevOps\nexa-connect` in PowerShell:

```powershell
$acceptanceId = '28b7ee78ac2245119c0fa1d6b6d260f3'
$work = ./scripts/pos-acceptance.ps1 -Action Worklist -AcceptanceId $acceptanceId | ConvertFrom-Json
$work | Select-Object bindingValid, qrCandidateFresh, next, operatorCasesComplete
```

If `bindingValid` is false, investigate the installed target before recording a passing
observation. After an intentional deployment, prepare a new pack following [UX9](POS-UX9-Acceptance.md).
QR freshness, actual phone acceptance and release readiness remain separate requirements.

To launch the existing installation when it is closed:

```powershell
./scripts/first-draft-terminal.ps1 -Action Launch
```

## First operator batch

Perform one row at a time. Keep identifying evidence privately; report only the case and
result in chat. Do not share passwords, payment references, tokens or unrestricted screenshots.

| Step | Actions and expected result | Recording boundary |
| --- | --- | --- |
| 1. Sign in | Sign in or Resume / Authenticate as the original cashier. Check branch, terminal and retained shift/custody; existing pending work must remain original. | Record `cashier-signin` only after these checks. If authentication is cancelled or fails, stop and report the visible error. |
| 2. Idle lock and resume | Wait for the configured idle interval. Confirm private work is hidden and existing custody is retained (an open session remains open). Tap or press a key to reveal centered authentication; resume as the original cashier. | Record `idle-lock-resume` after the complete case. Seeing one screensaver image does not pass `locked-gallery`, which also requires its own refresh, customer-screen, expiry/failure and privacy observations. |
| 3. Cash sale | Use a dedicated test cash session and one synthetic test sale. Review the exact server total and confirm cash once. Use **Orders → Refresh orders** to review the Paid original order, **Shift & cash** to check its cash movement, and **Receipts** for its receipt. Refresh/review must not collect again. | Record `cash-original-settlement` after verifying one settlement. If an outcome is uncertain, preserve the original request and use existing review/recovery; do not submit a fresh payment. |
| 4. PDF and reprint | For that same paid original order, select the test PDF printer, save and inspect the receipt, then perform an authorized reprint. Check totals, tender, pagination and readability. | Record `receipt-pdf-reprint`; reprinting must not alter payment. This does not pass physical PAISHUO output or `printer-failure-recovery`. |
| 5. Normal close/reopen | Finish the current action, close the staff window normally and reopen using the existing launcher. Authenticate as the original cashier. Check retained shift, settings, original paid order and recovery. | Keep this as separate supplemental lifecycle evidence under [UX17](POS-UX17-Shutdown-Diagnostics.md). It does **not** pass `restart-pending-payment`: that case requires an original saved pending request and verified recovery in a separate test run. If closure hangs, preserve lifecycle diagnostics and report it; do not delete recovery or force-kill automatically. |

## Record an observed result

**Cash review → Load history** lists closed cash sessions for authorized supervisor review;
it is not sales-order history. An empty list can be expected when no eligible closed sessions
exist in the selected date range. Do not close the test shift solely to populate that list,
grant extra permissions or create another sale to look for the original paid order.

Use the existing UX9 Record action after the operator has actually observed the complete
case. Select `passed`, `failed` or `blocked` explicitly; there is no default or automatic pass.
Generate an evidence UUID to index the actual observation privately. A UUID alone is not proof.

```powershell
$caseId = Read-Host 'Observed case ID from the table'
$status = Read-Host 'Observed result: passed, failed or blocked'
$evidenceId = [guid]::NewGuid().ToString('N')
# Run only after checking and privately indexing the actual observation:
./scripts/pos-acceptance.ps1 -Action Record -AcceptanceId $acceptanceId -CaseId $caseId -Status $status -EvidenceId $evidenceId -ConfirmObserved
./scripts/pos-acceptance.ps1 -Action Worklist -AcceptanceId $acceptanceId
```

For a confirmed defect, use the existing UX9 Defect action with an appropriate category and
severity. Preserve the original target and safe diagnostic evidence. Reproduce the issue,
implement the smallest coherent correction, update tests/docs and verify it. Resolve the
defect with new evidence; the case returns to pending and requires an actual retest. If a
new binary/backend is deployed, prepare a new target pack rather than transferring old passes.

## Remaining batches and completion

After the first batch, continue with manual PromptPay, Omise sandbox, QR submission → staff
acceptance → Kitchen → original-order collection, then isolated payment/network recovery
and physical equipment/privacy cases. Use the full UX9 procedures and UX18 prerequisites.
Physical drawer opening stays disabled pending verified printer model, connection and commands.

UX19 remains in progress until the operator batch has actual results and any confirmed
defects are corrected and retested. Passing these first checks does not complete all seventeen
cases, actual phone acceptance, fresh-source verification or production release gates.

## Recorded progress

For installed acceptance pack `28b7ee78ac2245119c0fa1d6b6d260f3`, the operator confirmed
that sign-in passed and the branch, terminal and retained shift were correct. This was
recorded through the existing Record action as `cashier-signin=passed`, with evidence UUID
`4d10eaad97f34ed294000c13fb2c07ff`. It is an operator attestation, not an automated observation.
The operator also confirmed all idle-lock/resume checks passed. This was recorded as
`idle-lock-resume=passed`, with evidence UUID `4b0ec42e05ca4dc9a6131d6e9219f1f0`.
The operator subsequently confirmed all cash-original-settlement checks passed, including
original Paid order review, correct total/receipt, cash movement and no duplicate collection.
This was recorded as `cash-original-settlement=passed`, with evidence UUID
`03a547945bf34ed699fa7748c6e8d132`.
The operator also confirmed all PDF receipt/reprint checks passed on the same paid order:
correct totals/tender, readable layout/pagination and no payment change or duplicate collection.
This was recorded as `receipt-pdf-reprint=passed`, with evidence UUID
`c56b837d78fc4f42a12abc5626a6a64d`. All four results are operator attestations.
The operator confirmed normal close/reopen passed with retained state correct. Supplemental
evidence UUID `d074400180cb40f7a5085e573202fdb1` captures that user report against the valid
installed target without changing the acceptance record or passing pending-payment recovery.
This completes the first walkthrough batch; it does not complete all acceptance cases.

The next batch has begun: the operator confirmed all manual PromptPay original-settlement
checks passed. This was recorded as `promptpay-original-settlement=passed`, with evidence UUID
`84d1abcfd5ce4cab9575d40602532bc6`.
The operator also confirmed all Omise sandbox original-order checks passed, including the
provider/order outcome, amount, receipt and no duplicate charge on review. This was recorded
as `omise-sandbox-original=passed`, with evidence UUID `4f1e01269cf34da0bb0316e061ff3922`.
The operator confirmed all QR cash-collection checks passed: guest-history isolation, staff
acceptance before one Kitchen delivery, original-order cash collection at its server total,
Paid status/receipt and no duplicate collection after refresh. This was recorded as
`qr-cash-collection=passed`, with evidence UUID `ca40ee53a33648dfaa726b53b6b3bda4`.
The operator also confirmed all QR manual PromptPay collection checks passed, including
separate guest histories, staff acceptance before one Kitchen delivery, approved QR/payee,
original-order total, test proof, Paid status/receipt and no duplicate settlement. This was
recorded as `qr-promptpay-collection=passed`, with evidence UUID
`80f95813afb64dc1ab4cc1ac75de55bc`.
The operator confirmed print-cancellation recovery passed on an already-paid order, followed
by successful explicit reprint with payment/recovery preserved. This was recorded as
`printer-failure-recovery=passed`, with evidence UUID `3928f3341ddb4736a6a35a10fb211d56`.
This observation covers cancellation; physical PAISHUO paper output remains unverified.

The operator reported the scanner or test codes are not ready. `barcode-input=blocked`
was recorded with evidence UUID `eda476c720034b2a9647e60e6b25b9e9`; no scanner defect or
pass is inferred. The operator reported both screens are not ready; `dual-display-isolation=blocked`
was recorded with evidence UUID `c6e3926b335b401bacb74bfdedea1d7f`. No dual-display/privacy pass
is inferred from single-screen observations. Current case progress is nine passed, six pending
and two blocked. Remaining pending cases are session-expiry fallback, restart with pending
payment, network-loss retry, locked gallery, drawer-history privacy and permission/scope denial.
Interrupted-payment and network-loss scenarios still require their separate isolated test
setup; real identity expiry/denial and privileged scope tests require appropriate separate
test identities/context. Full staff/customer gallery acceptance remains pending. No application
defect has been confirmed or correction deployed.

The 49 existing acceptance/worklist guard assertions passed, and preflight rechecked the
installed binding without changing the acceptance record. QR candidate freshness remains
false and release/production readiness remains false. The walkthrough documentation passed
its audit and local-link checks. Overview and canonical architecture were reviewed unchanged:
this slice reuses existing operational tooling and changes no runtime or service boundary.
