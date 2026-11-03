# POS UX24 — guided operator acceptance and targeted fixes

The subsequent [native QR review slice](POS-QR-Request-Review.md) changes application source;
it is separate from the UX23 target below. That update is now installed with a fresh
target-bound pack; this document's earlier pack and eight passes remain historical.
Do not transfer these passes or treat native simulated tests as operator acceptance.

## Scope and historical target

UX24 continues the existing [UX18 worklist](POS-UX18-Cashier-Acceptance.md) and
[UX9 observation/defect tracker](POS-UX9-Acceptance.md) on the activated
[UX23 supervised pilot](POS-UX23-Background-Hosting.md). It adds no second tracker,
automatic operator passes, speculative application changes or new financial command.
Operators perform only the explicitly requested synthetic test actions through existing workflows.
The [product expansion backlog](../Planning/Product-Expansion-Roadmap.md) remains planned.

| Binding | Historical value |
| --- | --- |
| Run | `c30b628ab23141f389119daa565f894f` |
| Installed release | `34a7f88b4991413aaa3dd7028c67223a` |
| Background host | `34cd14322c6648cabe08ee31be0101e4` |
| QR pilot | `179d8478ce3940b885745604cfc2320a` |
| Acceptance pack | `6b511123c5ec4351bbb6984e1fdd4f2e` |

These bindings describe the superseded target; do not record new observations against it.
Use the fresh bindings in the [native QR review rollout](POS-QR-Request-Review.md#retained-local-pilot-activation--2-november-2026).
Previous release and lab evidence cannot pass the fresh cases. Installed binding, QR candidate freshness,
source regression, operator completion and release readiness are different gates.
The initial preflight on 2 November 2026 confirms binding valid, fresh QR proof and the
owned background/QR worker running on that earlier target.

```powershell
# From D:\DevOps\nexa-connect: inspect the historical pack only.
$acceptanceId = '6b511123c5ec4351bbb6984e1fdd4f2e'
./scripts/pos-acceptance.ps1 -Action Worklist -AcceptanceId $acceptanceId
```

Recheck the worklist/binding before observations. A failed binding requires investigation,
not a passing record or silent pack replacement. A deliberate deployment needs a fresh
matching pack; prior observations stay attached to their original target.

## Guided batches

Use [UX9's complete expectations](POS-UX9-Acceptance.md) and the worklist's exact case IDs.
Present one case at a time, with its prerequisite, steps and expected outcome. Only an
explicit operator observation or actual appropriate evidence can advance a case. A partial
check, generated UUID, test suite or historical usability report does not pass a whole case.

| Batch | Cases and operating boundary |
| --- | --- |
| Access | `cashier-signin`, `idle-lock-resume`; then `session-expiry-fallback` in its separately approved identity test context. Check original-cashier custody and cancellation/wrong-cashier denial without changing retained session policy. |
| Original sales | `cash-original-settlement`, `promptpay-original-settlement`, `omise-sandbox-original`. Use a dedicated test session and synthetic orders; confirm once, then review the original Paid result and receipt. Use authorized test proof/test tokens only; no real bank transfer or production card. |
| Receipts | `receipt-pdf-reprint`, `printer-failure-recovery`. Microsoft Print to PDF/cancel/reprint is distinct from actual PAISHUO paper output. Reprinting never collects again. |
| Guest ordering | `qr-cash-collection`, `qr-promptpay-collection`. Actual iPhone Safari and Android Chrome; separate histories, table scope, server totals, staff acceptance before one Kitchen delivery and original-order collection. |
| Recovery | `restart-pending-payment`, `network-loss-original-retry`. Dedicated isolated stack/context and controlled uncertainty; same saved original method/amount/identity. Normal close/reopen or automatic host recovery does not establish interrupted-payment recovery. |
| Equipment/privacy | `barcode-input`, `dual-display-isolation`, `locked-gallery`, `drawer-history-privacy`, `permission-scope-denial`. Confirm actual prerequisites when reached; record unavailable hardware as blocked. No physical drawer activation. |

Do not interrupt the retained supervised graph to manufacture a fault. Its host may repair
an exited dependency and its lifecycle fence intentionally blocks direct maintenance.
Use an explicitly prepared isolated context for expiry/revocation, network and delayed-
response/scope tests; do not reuse an old source-bound lab whose binding has changed.
Observations in a separate lab remain attached to that lab and do not silently pass the
installed normal target. If a case cannot safely meet its prerequisite, record the reason
as blocked/pending and resolve the appropriate test setup first.

For a synthetic cash review, use **Orders → Refresh orders** for paid orders, **Shift & cash**
for the movement and **Receipts** for the receipt. Supervisor **Cash review** is not the paid
order history; an empty review list alone does not prove a failed sale or lost payment.
Never collect again to repair an empty list or uncertain receipt.

## Evidence and defect/retest loop

1. Check current binding and case prerequisites. Ask for the actual result, without passwords,
   tokens, bank references, guest QR capabilities or unrestricted screenshots.
2. Keep bounded evidence under the existing current-user protected acceptance directory;
   index observations by UUID and record only safe case/result/check facts. Do not copy
   payment proof or raw identity credentials into chat or documentation.
3. Use existing Record with `-ConfirmObserved`. Record failed/blocked when appropriate;
   do not infer missing checks or carry forward older passes.
4. On a reported problem, capture the exact visible behavior and minimal safe reproduction.
   Preserve original order/settlement/recovery. Open the existing Defect record with its
   category/severity/evidence; a suspected cause is not a confirmed diagnosis.
5. Reproduce in an appropriate isolated fixture, inspect adjacent financial/authorization
   boundaries and implement the smallest coherent correction. Add meaningful regression
   coverage and update component/API/database/identity/deployment documents as affected.
6. Run the relevant tests and documentation audit. Follow supervised maintenance and
   matching-package rules for rollout; do not replace or stop an open cashier window.
   Resolve the defect only with actual verification evidence. Resolution returns its case
   to pending for explicit retest; deployment/target changes must keep binding provenance.

No code change is required solely to make this milestone appear implemented. Application
corrections require a reproduced defect. No new migration, API, role, timeout or hardware
permission is introduced by the walkthrough. Review the project overview/architecture after
each correction and update affected sections; unchanged boundaries need no cosmetic edits.

## Progress and completion

On 2 November 2026, the operator confirms cashier sign-in/resume, idle-lock/resume,
cash-original-settlement, receipt-PDF/reprint, print-cancellation recovery and original
manual-PromptPay settlement. The operator also reports the Omise test account working in response
to the fresh-token sandbox payment check. QR cash collection is also operator-confirmed:
the initially missing request had not been accepted; it appeared after staff acceptance,
and the operator then reported the QR cash test working. This is an operator attestation,
not an independent transaction inspection. Checks cover retained scope/custody,
menu loading, hidden private work, one original cash settlement/movement/receipt, readable
PDF/reprint, cancellation recovery, approved PromptPay QR/payee and authorized test-proof
verification, with no duplicate collection. These eight cases pass; nine remain pending until
separately observed. Physical PAISHUO output remains unverified; cancellation tests do not
certify hardware. The Omise token helper was stopped and has been restarted at
`https://localhost:54443`; trusted HTTPS returned 200. Helper startup alone restores only a
test prerequisite; the subsequent operator reply supports the sandbox-case pass, without
independent provider verification. Use the existing
[helper instructions](../../src/Tools/NexaConnect.OmiseTokenHelper/README.md) for fresh tokens.
The private pack is the authoritative progress source;
this guide is not a second results database.

Automatic Windows sign-in startup remains off. Actual Windows reboot, device/printer output
and dedicated-server/production readiness are supplemental prerequisites, never inferred
from these seventeen cases or earlier automated host tests. No reboot is performed during
the walkthrough without explicit coordination. Complete all required cases, resolve/retest
defects and report remaining blockers accurately; operator completion never grants public
release, clean-source CI or production verification by itself.
