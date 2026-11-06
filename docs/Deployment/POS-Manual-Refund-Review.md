# Review an original cash or PromptPay payment in POS

This source slice adds original-payment review to Windows POS. Recording a refund remains pending.

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
These are in-memory domain rules awaiting durable storage and live authorization.

## Remaining write and reconciliation work

- Validate the original Order-owned settlement server-side and require live branch
  approval with an amount and currency limit. Check that the approved cashier owns
  the open terminal shift and drawer when beginning a cash payout. Authenticate both
  identities separately and bind their actions to the same operation.
- Bind a stable operation to the settlement, refund amount, tender and reason.
  Reserve the remaining balance before payout, serialize competing refunds and allow
  lookup by operation after an uncertain response. Preserve the sale and original
  receipt. Record a separate immutable refund and approval.
- Commit cash completion and its one drawer movement together under the paying session
  lock. Coordinate shift and drawer close with a started payout. An uncertain payout
  must retain its reservation and be reconciled before another payout is attempted.
  Add manager release of untouched reservations, protected PromptPay transfer evidence
  storage and appropriate transfer-reference uniqueness across orders.
- Reconcile both tenders in Reporting and day close using refund-time attribution.
  Verify partial and full refunds, duplicate retries, concurrent attempts, wrong
  branch and drawer closure before physical POS acceptance.

This slice requires a Windows POS build. It adds zero server routes, migrations,
configuration keys, grants or financial writes.
