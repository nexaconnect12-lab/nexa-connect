# Manual tender and branch QR policy

`ManualTenderReadiness` provides input guidance without granting server financial authority.
`ManualTenderIntent` preserves an uncertain original request in full instead of rebuilding
it from refreshed totals or editable UI fields. New PromptPay proof requires a current QR,
bounded reference and receipt checkbox. Order/cashier/shift/idempotency fences remain in
the native orchestration and authoritative backend.

`PromptPayQrPolicy` validates organization/restaurant/branch, asset identity, hash, bounded
PNG dimensions and a maximum 60-second delivery lease. `MainWindow.PromptPay` handles memory
only refresh, stale response fences and readiness; it never uses a gallery-reader token.
Tests cover policy, original-intent preservation and WPF proof changes. Actual bank receipt
validation is manual. See [operator guide](../../../../docs/Deployment/POS-Branch-PromptPay-QR.md).
