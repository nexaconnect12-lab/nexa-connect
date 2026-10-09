# Paid order receipts

Order issues one version-1 ordinary sales receipt when an Order first reaches `Paid`. The receipt is an immutable snapshot of the accepted line names, quantities and unit prices; accepted subtotal, service charge, tax and total; currency; tender; Order identity; tenant scope; and payment-completion timestamp. Its stable number is `R-{ORDER_ID_N_FORMAT}`. It is not a jurisdiction-specific tax invoice.

Every production application path that changes an Order to Paid issues the snapshot before committing: foreground provider checkout, provider workflow recovery, authorization/capture reconciliation, and manual cash or visually verified PromptPay settlement. PostgreSQL stores `receipt_snapshot` on the Order row in the same transaction as Paid and the completion outbox record. A failed transaction exposes neither state nor receipt. Concurrent completion can construct multiple candidates, but row serialization and first-value preservation retain one snapshot; deterministic payment-event identity suppresses duplicate completion records. Reloading or replaying a Paid Order never regenerates it.

`GET /api/order/v1/orders/{orderId}/receipt?branchId={branchId}` requires an authenticated customer token, `X-Organization-ID`, `X-Application-Code: nexa_connect`, and live branch-scoped `order.read`. Service-workload bypass is deliberately unavailable. A caller outside the stored organization or branch receives no receipt. Responses use `Cache-Control: no-store`; unpaid and pre-migration Paid Orders return `404`, authorization denial returns `403`, and authorization dependency failure returns `503`. Logs never contain receipt contents.

The WPF POS automatically attempts a preview after Paid and supports retrieval by Order ID. It validates version, stable number, terminal scope, line arithmetic, currency and bill totals before rendering. It stores only a protected last-Order reference; receipt contents stay server-authoritative and are cleared on session lock or sign-out. Print / reprint first reloads and reauthorizes the receipt, then opens the standard Windows print dialog. Preview, retrieval, local-reference and print failures do not change payment state and instruct the cashier not to collect again.

Apply Order migration **9** with application version **0.18.0** before deploying this Order binary. It adds the receipt column and immutability/scope/amount trigger. It does not backfill historical Paid Orders. Downgrade to 8 refuses once receipt history exists; roll forward instead.

Verify locally with:

```powershell
./scripts/test-order-pricing.ps1 -ConfirmDisposableInfrastructure
dotnet test NexaConnect.sln --no-build
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj --no-restore
```

The disposable PostgreSQL matrix covers atomic rollback, concurrent completion, one retained snapshot/event, tenant scoping, rehydration, immutability, manual settlement and downgrade refusal. HTTP tests cover unpaid, allowed, denied, wrong-scope, anonymous and workload calls. Device-specific printer models, fiscal numbering/signing, tax-invoice fields, refunds/void receipts, delivery and historical backfill remain separate work.

Order 11/application 0.22.0 also retains one immutable receipt-backed sale publication in the Paid transaction. Concurrent completion publishes the persisted winning receipt time/pricing, and replay uses retained evidence. This does not backfill receiptless historical orders. See [sale reporting](Sale-Financial-Reporting.md).
