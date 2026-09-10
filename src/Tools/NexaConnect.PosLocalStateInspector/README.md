# NexaConnect POS local-state inspector

This read-only acceptance helper opens an existing POS SQLite database with `query_only=ON` and emits JSON containing only integrity, schema version, active operational-row count, unresolved outbox count, and interrupted-send count. It never decrypts or prints payloads.

Run it directly when diagnosing the local gate:

```powershell
dotnet run --project src/Tools/NexaConnect.PosLocalStateInspector --configuration Release -- `
  --database "$env:LOCALAPPDATA\NexaConnect\POS\pos-state.db"
```

The normal entry point is `scripts/verify-pos-cashier-live-acceptance.ps1`. Stop the POS client before copying or restoring the database. A missing database, SQLite error, or incompatible schema fails the acceptance gate.
