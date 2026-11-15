# Review an original cash or PromptPay payment in POS

Windows POS has original-payment review, manager approval and release, and a cashier
workspace for assigned payouts. Pilot activation remains pending.

1. In **Receipts → Cash / PromptPay review**, choose **Choose paid order…**.
2. In **Orders**, select the original Paid sale. Use **Refresh orders** or **Next page**
   when necessary, then choose **Review manual payment**.
3. Check the original tender, paid total, receipt number and local paid time. The
   remaining refundable balance is outside the scope of this review.

The review reuses the scoped Order receipt GET and live branch `order.read` access, including validation of the original
order, organization, restaurant, branch, currency and immutable receipt identity.
Cash and manual PromptPay are identified from that receipt. Captured card payments
retain their separate review. Focus loss, lock, navigation away and privacy reset
clear the evidence. In-flight reads use a 20-second deadline. Receipt lookup failures
are shown in the review. A historical Paid order may lack an immutable receipt.
Find `order.receipt.read` in `nexaconnect-pos-client`, then follow the validated correlation
to `nexaconnect-order`. Keep bodies, credentials and payment
evidence out of logs.

## Approval and payout domain

The POS service now has a domain model for one original manual payment. A manager
approves an exact amount and reason for a named cashier. The model requires a
separate manager identity and reserves that amount against the original paid total.
The named cashier binds the payout to their terminal and shift, plus their cash
session for cash. PromptPay requires the cashier to confirm verification and supply a
return-transfer reference. The model checks that evidence without contacting the bank.
PromptPay carries no drawer. Matching retries retain the first decision, payout custody and completion.
The POS PostgreSQL store now persists those transitions, enforces terminal and drawer
custody, and atomically records the cash movement. The backend API now checks live
manager and cashier authority and exposes scoped operation recovery.
See [POS 27 storage](../Database/POS-Manual-Refunds.md) and [API](../API/POS-Manual-Refunds.md).

## Isolated backend verification

Deploy matching POS, Order and Authorization code with POS 31 and Authorization 18
at compatibility 0.51.0. POS needs direct `Services__Order` and
`Services__PlatformDirectory` addresses alongside its existing Restaurant and
Authorization dependencies. The new POS adapters require direct HTTPS, with
loopback HTTP allowed in Development and Testing.
Provision a scoped THB approval limit for `pos.manual-refund.approve`.
Preserve the POS Data Protection keys with the database.

`ManualRefunds__Enabled` defaults to false and should remain off on the retained
pilot. Enable new work only in an isolated synthetic environment for this slice.
Disabling it pauses preview, cashier registration and selection, approval, and
payout start. An authorized cashier can still record an already started payout.
Authorized participants can read the operation and the original manager can release
an untouched approval. Preserve these paths and permissions until all
started payouts are resolved before considering a rollback.

## Cashier workspace

1. In **Receipts → Cash / PromptPay review**, choose **Load assigned refunds**.
   Select the approved refund and verify its order, tender, amount and reason.
2. Select **Start approved payout**. A fresh successful start tells the cashier to
   return the funds once. Cash requires the cashier's open shift and drawer.
   PromptPay requires an open shift and leaves the drawer out of the payout.
   POS displays the exact approved amount and blocks new payouts for approvals
   containing a fraction of one satang.
3. After returning the approved amount, confirm it was returned. For PromptPay,
   enter the verified transfer reference. Select **Record payout**.
4. If a response is lost, or POS closes mid-payout, return to the same workspace
   and choose **Verify saved refund**. A recovered start is evidence of existing
   work. If the amount was already returned, record it. If the outcome is unclear,
   ask a manager to reconcile it before moving any more funds.

The cashier must use the original terminal and shift, including the original paying
drawer for cash. The client stores the order, operation and cashier in the existing
Windows-protected SQLite state before either write. It clears that reference after
confirmed completion or a read that verifies the operation was still untouched.
Transfer proof stays in memory and clears when leaving the workspace, on lock or on
focus loss. Install and update preflight includes the saved operation as retained work.
The cashier must verify the saved operation before closing the shift or drawer.
Selected and recovered details display the exact original order ID.

## Manager approval and release

1. The cashier opens **Receipts → Cash / PromptPay review** and chooses **Make me
   available for refunds** while their shift is open. The registration lasts five
   minutes. A cash assignment requires that cashier's open THB drawer.
2. The manager selects the original paid sale through **Choose paid order…** and
   **Review manual payment**. Expand **Manager approval and release** and choose
   **Load approval options** to see the paid and available amounts.
3. Select the available cashier by terminal and shift, enter a whole-satang amount
   and reason, then choose **Approve refund**. The manager and cashier use separate
   accounts. Current cashier authority is checked again at payout.
4. To recover an interrupted approval, choose **Verify saved approval**. If it
   remains unconfirmed, **Retry saved approval** sends the same operation and terms.
   If verification stays unavailable and the original shift or manager authority
   cannot be restored, retain the protected request and ask support to reconcile
   it. A generic missing or denied response cannot establish that approval failed.
5. To withdraw an untouched approval, select it in the manager list and choose
   **Release unused approval**. Reload for the restored balance. A started payout
   must be reconciled and remains reserved.

## Reporting and reconciliation

Reporting 23 adds the POS completion projection and separate cash and PromptPay
totals. The day-close draft checks exact projected evidence and shows unresolved
payouts and drawer gaps. Enable the consumer and POS dispatcher only in isolated
verification for this release. [Reporting configuration and evidence](../API/Manual-Refund-Reporting.md)

## Remaining activation work

Use the [physical terminal acceptance runbook](POS-Physical-Refund-Acceptance.md).

- Apply POS 31 at application compatibility 0.51.0 and deploy matching POS service and
  Windows POS builds. Stop older POS service writers during the upgrade and retain the
  POS Data Protection key ring and application identity. The first successful PromptPay
  refund completion per organization indexes historical protected references. If an
  older reference cannot be decrypted, restore the key ring before retrying the original operation.
  For the standalone migration wrapper, pass `-ApplicationVersion 0.51.0`.
- On a duplicate-reference message, ask a manager to compare the transfer evidence with
  the exact pending refund. Keep the payout reserved. Record a different reference only
  if it identifies a verified transfer already made for that payout. Never send another
  transfer to clear the error.
- Deploy Reporting 24 and matching code, then verify protocol-four
  cutoff/seal and V3 settlement evidence plus the drawer flow on the physical
  pilot with synthetic data. Keep new work disabled until rollout prerequisites are verified.

The cashier workspace requires a matching Windows POS build and backend. The
retained physical pilot remains disabled for new refund work.

Run `./scripts/test-qr-entry-persistence.ps1 -ConfirmDisposableInfrastructure`
and `./scripts/test-settlement-reporting.ps1 -ConfirmDisposableInfrastructure`
for disposable verification before the physical checks.

## Cashier cash refunds during the merchant trial

Deploy POS schema 31 and matching POS service, Order service and Windows client.
In the **POS service** configuration, set `ManualRefunds:Enabled` to `true` and
set `ManualRefunds:CashierLimits:<organization GUID>:<branch GUID>` to the approved
positive THB amount, for example `50.00`. Use the exact GUIDs with hyphens. For
container environment configuration use
`ManualRefunds__CashierLimits__<organization GUID>__<branch GUID>`.
An omitted, zero or invalid limit requires a manager. This setting is managed by
deployment operators. The Customer Portal has no editor for it.
For the already-provisioned private LAN pilot, use the launcher option documented in
[physical refund acceptance](POS-Physical-Refund-Acceptance.md).

1. On the physical POS, sign in as the cashier and open the shift and drawer. Complete
   a synthetic cash sale. In Orders, select the paid order and choose **Review manual payment**.
2. With the branch limit enabled, enter an amount within it and select a reason.
   Start the cash refund. Only a fresh successful start authorizes handing out
   cash. Record completion after handing out the cash.
3. Verify the exact operation, one refund cash movement and the corresponding
   settlement total. Attempt a second partial refund whose cumulative amount
   exceeds the cap and confirm the manager path is required.
4. Repeat with the limit omitted or zero, with another branch and with an original
   PromptPay payment. Confirm that the cashier cash option is unavailable where ineligible.
5. Interrupt communication around start, verify the saved original operation and
   retry the exact saved start under its original scope if the response remains ambiguous.
   Preserve the saved reference and use support if the retry is denied or unavailable.
   If it is already started and the cash
   handoff is uncertain, reconcile the physical drawer before completing. Verify
   that closing the drawer with a started payout is blocked and that restoring
   connectivity and replaying completion creates no duplicate movement.

The limit applies per original paid cash sale and includes completed and still
reserved amounts, including manager approvals. Released approvals restore available
capacity. Removing the limit blocks new cashier starts while exact verification
and completion remain available for existing operations.
