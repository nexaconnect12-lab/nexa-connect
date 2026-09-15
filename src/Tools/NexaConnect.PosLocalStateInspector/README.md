# NexaConnect POS local-state inspector

This read-only acceptance helper opens an existing POS SQLite database with `query_only=ON` and emits JSON containing only integrity, schema version, active operational-row count, `pendingCashReviewCount`, unresolved outbox count, and interrupted-send count. It never decrypts or prints payloads, including the protected supervisor reason and identity.

Run it directly when diagnosing the local gate:

```powershell
dotnet run --project src/Tools/NexaConnect.PosLocalStateInspector --configuration Release -- `
  --database "$env:LOCALAPPDATA\NexaConnect\POS\pos-state.db"
```

The normal entry points are `scripts/verify-pos-cashier-live-acceptance.ps1` for cashier checkout and `scripts/verify-pos-cash-review-live-acceptance.ps1` for supervisor recovery. Stop the POS client before copying or restoring the database. A missing database, SQLite error, or incompatible schema fails either acceptance gate.
