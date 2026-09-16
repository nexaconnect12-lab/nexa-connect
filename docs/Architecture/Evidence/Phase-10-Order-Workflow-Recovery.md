# Order pre-payment workflow recovery evidence

The guarded live acceptance passed locally on 2026-09-15 against a generated PostgreSQL 17 and RabbitMQ 4 Compose project. The runner built an isolated real Order host, applied Order migrations through version 6, started a loopback-only idempotent Inventory/Kitchen/token fixture, terminated two Order child processes at controlled dependency-response boundaries, and verified recovery after restart.

The single coordinated matrix established:

- a Submitted manual-tender Order remained Submitted when Order was terminated while its first Inventory response was withheld, then reused the original Order identity, received the fixture's stable reservation identity on retry, and reached KitchenAccepted;
- an InventoryReserved Order remained InventoryReserved when Order was terminated while its first Kitchen response was withheld, then reused the original Order identity and received the fixture's stable ticket identity on retry;
- three consecutive Inventory `503` responses exhausted the HTTP adapter retry boundary, released the durable recovery claim, and succeeded on the next claimed attempt;
- each recovered Order produced exactly one accepted transition event for each completed stage, despite repeated downstream attempts;
- every downstream replay carried the original durable correlation identifier; and
- a recovered KitchenAccepted Order completed the existing exact-total THB cash settlement path.

Sanitized evidence records two process interruptions, Inventory attempts `2` for the Submitted interruption, Kitchen attempts `2` for the InventoryReserved interruption, dependency attempts `4`, zero duplicate transitions, successful final settlement, and verified project cleanup. Credentials, connection strings, bearer tokens, request bodies, and raw child-process logs are not retained.

Run the gate from the repository root:

```powershell
pwsh -NoProfile -File scripts/test-order-workflow-recovery-live.ps1 -ConfirmDisposableInfrastructure
```

This evidence covers the manual-tender pre-payment stages owned by Order migration 6. It does not prove provider-payment recovery, production dependency behavior, a remote broker, live-traffic rollback, or cashier interaction with a physical terminal.

## Concrete-provider interruption gate

The migration-7 provider gate is implemented in `scripts/test-order-provider-recovery-live.ps1` with its isolated Compose definition under `docker/order-provider-recovery-acceptance/` and opt-in integration matrix `OrderProviderPaymentRecoveryLiveAcceptanceTests`. It creates three independent Order/Payment pairs and exercises these boundaries:

- the Payment intent exists durably before any provider command;
- the provider returned a successful authorization before Payment committed the result; and
- the provider returned a successful capture before Payment committed the result.

The latter two cases kill the exact acceptance process after the real HTTPS response. Recovery waits for the expired Payment lease, queries the provider by the stable Payment-intent identity, commits the authoritative state, and sends the production reconciliation contract through a generated RabbitMQ broker to an isolated real Order host. Verification requires exactly one authorization and capture command per scenario, the expected status lookup only for the uncertain boundary, one stable Order and Payment intent, one Payment-completed Order event, and final captured/Paid state. Success evidence contains only counts, state labels, and booleans.

Run it only with an API-compatible non-production provider after following the secret-injection and three-confirmation procedure in the [deployment guide](../../Deployment/Deployment-Guide.md). The runner passed on 2026-09-15 against a temporary local HTTPS contract fixture, including all three process terminations and project cleanup. That run validates the harness and production code paths only; no selected external provider credential is configured, so provider-specific release evidence remains pending.
