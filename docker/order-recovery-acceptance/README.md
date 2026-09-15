# Isolated Order workflow-recovery acceptance infrastructure

This test-only Compose project supplies a fresh PostgreSQL 17 database and RabbitMQ 4 broker for `scripts/test-order-workflow-recovery-live.ps1`. Both services publish dynamic IPv4-loopback ports and keep data in temporary filesystems. The launcher generates the password in memory, creates a unique `nexa-order-recovery-it-<uuid>` Compose project, and removes that exact project and its volumes in `finally`.

The integration test starts its own loopback-only dependency fixture and real Order child processes. No Keycloak, Inventory, or Kitchen production service is contacted. The fixture returns one stable reservation or ticket identity per Order, can hold a response until Order is terminated, and records only identifier/count/correlation evidence.

Run from the repository root:

```powershell
pwsh -NoProfile -File scripts/test-order-workflow-recovery-live.ps1 -ConfirmDisposableInfrastructure
```

The confirmation authorizes creation, migration, process termination, and deletion of this generated environment. Sanitized TRX and JSON evidence remains under `.runstate/order-workflow-recovery-live/<run-id>/`; credentials, connection strings, tokens, request bodies, and raw service logs are not retained.
