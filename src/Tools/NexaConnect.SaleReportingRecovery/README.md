# Sale reporting recovery

This operator CLI previews and optionally requeues receipt-backed Order sale events, then checks Reporting against retained publication evidence. It uses Order and Reporting service-owned Infrastructure adapters. It never modifies Reporting or reconstructs a missing receipt. Apply Order 11 / Reporting 19 with application 0.22.0 first.

Supply secret-managed `NEXACONNECT_SALE_RECOVERY_ORDER_DB` and `NEXACONNECT_SALE_RECOVERY_REPORTING_DB` through the process environment. Use read-only credentials for preview/reconciliation. Apply requires Order reads, `UPDATE` privilege on `orders` to acquire the row lock, and narrowly scoped writes for publication, outbox and replay audit. Set `NEXACONNECT_SALE_RECOVERY_ACTOR` to the operator identity asserted by the controlled execution environment; the CLI does not authenticate that value. Database access and administrative process access form the authorization boundary; do not expose the tool to clients. Connection strings and financial bodies are never printed.

```powershell
dotnet build src/Tools/NexaConnect.SaleReportingRecovery
dotnet run --project src/Tools/NexaConnect.SaleReportingRecovery --no-build -- <organization-uuid> <branch-uuid> 2026-10-01T00:00:00Z 2026-10-02T00:00:00Z
# After inspecting the counts and confirming scope, use the same arguments with --apply.
```

The half-open receipt-Paid-time window is limited to 31 days and 10,000 completed orders. Rows without receipts use stored updated time only to identify gaps. Preview is read-only. `--apply` backfills a retained receipt and requeues the immutable publication, committing each Order separately with an append-only run/operator audit record. A failure can leave earlier Orders applied; rerun safely. It neither reconstructs missing payment identities nor overwrites conflicts. A missing identity or conflicting retained-publication/outbox payload fails the run with exit 2 and rolls back that Order transaction; earlier committed Orders remain applied. Preserve and review the conflicting evidence before retrying.

Start the Reporting consumer and establish its durable binding before applying. Reconciliation immediately follows replay and may report missing rows while the asynchronous dispatcher/consumer catches up. Rerun preview until settled. Output contains counts only: candidates, missing receipts, unpublished, new publications, requeues, and expected/matched/missing/conflicting report rows. Exit 0 means retained candidates match within this window; 1 means gaps or lag; 2 means invalid arguments or operational failure. It compares original tenant, identity, method, currency, pricing, times and event hash in a Reporting repeatable-read snapshot. It does not detect unrelated extra Reporting rows or certify global/refund completeness.

Do not reset financial tables or replay a changed payload to repair conflicts. Retain source evidence and investigate. Order publication and Reporting receipt downgrade guards require forward recovery once history exists. See [contract, diagnostics and acceptance](../../../docs/API/Sale-Financial-Reporting.md).
