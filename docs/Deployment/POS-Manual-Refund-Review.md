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

Deploy matching POS, Order and Authorization code with POS 28 and Authorization 18
at compatibility 0.47.0. POS needs direct `Services__Order` and
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

- Add transfer-reference uniqueness across orders. Reconcile uncertain payouts before
  allowing another payout attempt.
- Extend durable cutoff, seal and settlement evidence to certify POS-owned manual
  refunds. POS blocks finalization for dates with completed manual refunds until
  that protocol is ready. Then verify refunds and the drawer flow on the physical
  pilot with synthetic data.

The cashier workspace requires a matching Windows POS build and backend. The
retained physical pilot remains disabled for new refund work.
