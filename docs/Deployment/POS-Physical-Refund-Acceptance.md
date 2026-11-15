# Physical POS refund acceptance

Use the separate LAN pilot with synthetic orders, test cash and simulated PromptPay evidence. Preserve its database volumes, POS Data Protection key ring and Windows terminal profile.

1. Close POS on the physical device. Use the existing plan with `physical-pos-pilot.ps1 -Action Stop`, then `-Action Start -ConfirmTestResources -EnableRefundAcceptance`. Supply the same `-PlanPath` for both. The launcher applies POS 31, Reporting 24 and Authorization 18 at compatibility 0.51.0. It checks the Reporting consumers before enabling new refunds and provisions a THB 100 limit for the test store manager while preserving any existing limit or revocation.
2. Bundle the matching installer and verify the bundle and HTTPS on the physical PC. Install under the same Windows account. Follow the [onboarding guide](POS-Physical-Device-Onboarding.md).
3. Keep the cashier signed in on the physical terminal with the drawer open. Run the matching POS on the development PC under a separate test Windows account with the same physical pilot profile. The staff edge permits the development host and the configured terminal. Install or open the manager client in that separate account to keep its local recovery state separate. Use the provisioned manager for approvals without opening a second drawer. Make a synthetic cash sale and follow the [refund workflow](POS-Manual-Refund-Review.md) to approve and record a partial refund. Return test cash once. Verify one cash movement, the original Paid receipt and the expected drawer balance. After reopening cashier POS, verify the original Paid receipt and have the original manager reload the order refund history. If the completion response was interrupted, use **Verify saved refund** on the cashier terminal.
4. Repeat with synthetic manual PromptPay evidence. Verify the completed result in manager history and no drawer movement. Try the same reference on a different synthetic order. Verify the manager review message, then select **Verify saved refund** and confirm the second payout remains started. For this exercise only, reconcile it using a distinct reference for a separate simulated transfer.
5. Close the test drawer and shift. After the Singapore business date ends, prepare the cutoff, seal, approve, prepare finalization and finalize in the Customer Portal. Check the settlement ledger and CSV for exact separate cash and PromptPay refund totals and one settlement receipt. Record case outcomes without transfer references or credentials.

For single-till acceptance, stop the provisioned pilot and add `-CashierCashRefundLimit 50.00` to the usual `Start -EnableRefundAcceptance` command on the development PC. The launcher scopes the cap to the organization and branch in the retained POS profile. Omit the limit on a subsequent Start to restore manager approval for new cashier cash refunds. Follow the [cashier cash refund test](POS-Manual-Refund-Review.md#cashier-cash-refunds-during-the-merchant-trial).

Starting without the acceptance switch disables new refund work. Authorized reads and completion of started payouts remain available and Reporting continues consuming their events. Record any unresolved payout before ending acceptance.

If the manager limit provisioning step fails after the services start, stop the pilot
after closing POS and retry the same owned Start. Verify the limit before a cashier
starts a test payout.

Provisioning assigns the existing `customer-viewer` entry role to the verified
test store manager in the owned realm for day-close access. Sign in again to obtain
the new role. The original scoped product grants remain required.
