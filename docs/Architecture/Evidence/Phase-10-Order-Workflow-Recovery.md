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

The first hosted local run reached Paid but timed out publishing `payment.captured.v1`. The acceptance had no lifecycle evidence subscriber, and its RabbitMQ tmpfs erased queue/binding state during container stop/start. The harness now provisions a durable queue bound to `payment.#` before any Payment host starts, uses a generated-project RabbitMQ volume and one stable random loopback port across restart, and verifies the exact persisted terminal event arrives with its persistent flag. This evidence queue is separate from Order's production reconciliation queue; the uncertain-response scenarios still require matching Order inbox completion. Project cleanup removes the RabbitMQ volume. No production transport confirmation or routing behavior was relaxed.

A self-contained alternative is `scripts/test-order-provider-recovery-local.ps1 -ConfirmDisposableInfrastructure -ConfirmProcessTermination`. Its loopback HTTPS simulator supplies generated credentials and stable authorization/capture/status without external transactions. The simulator-only smoke gate passed on 2026-09-16, including exact-certificate HTTPS validation, invalid credential rejection, conflicting authorization replay and stable capture replay/status; process and PFX cleanup completed. The complete local hosted matrix passed on 2026-09-16 after correcting RabbitMQ restart storage and the lifecycle evidence subscription, including three provider-boundary interruptions, three Payment-host interruptions, persistent terminal-event delivery, real Order inbox completion for both reconciliations, and project/process/PFX cleanup. External-provider acceptance remains a distinct release gate.

The migration-7 provider gate is implemented in `scripts/test-order-provider-recovery-live.ps1` with its isolated Compose definition under `docker/order-provider-recovery-acceptance/` and opt-in integration matrix `OrderProviderPaymentRecoveryLiveAcceptanceTests`. It creates three independent Order/Payment pairs and exercises these boundaries:

- the Payment intent exists durably before any provider command;
- the provider returned a successful authorization before Payment committed the result; and
- the provider returned a successful capture before Payment committed the result.

The latter two cases kill the exact acceptance process after the real HTTPS response. The upgraded recovery phase starts isolated real Order and Payment hosts. It stops RabbitMQ before the expired Payment lease is reclaimed, requires Payment's hosted worker to query the provider by the stable Payment-intent identity and commit both the authoritative state and one unpublished terminal outbox event, then kills the exact Payment host process. After RabbitMQ and Payment restart, the gate requires that persisted event to be published and the matching reconciliation event to complete through Order's durable inbox. Verification also requires exactly one durable authorization-started and capture-started event per scenario, the expected provider calls made by the instrumented pre-interruption stage, one stable Order and Payment intent, one Payment-completed Order event, and final captured/Paid state. The hosted process uses the same stable idempotency identity, but the runner does not independently count its HTTP transport attempts. Success evidence contains only counts, state labels, and booleans.

The acceptance fixture is limited to loopback OIDC discovery/JWKS/token issuance and Inventory/Kitchen compensation responses. Payment's API, hosted authorization/capture recovery workers, PostgreSQL repository, transactional outbox dispatcher, lazy RabbitMQ transport, Order workload token handler, Payment workload authorization, Order reconciliation consumer, and inbox are production host code. `intent_created` proves the hosted command path and captured-event outbox restart; the two uncertain-response scenarios prove status-only reconciliation events and exact matching Order inbox records. The runner records three provider-boundary interruptions and three Payment-host interruptions.

Run it only with an API-compatible non-production provider after following the secret-injection and three-confirmation procedure in the [deployment guide](../../Deployment/Deployment-Guide.md). The earlier direct-recovery form passed on 2026-09-15 against a temporary local HTTPS contract fixture, including all three process terminations and project cleanup. The complete hosted-worker/outbox form passed locally on 2026-09-16 with the checked-in HTTPS simulator and generated PostgreSQL/RabbitMQ infrastructure. Sanitized hosted evidence is retained at `.runstate/order-provider-recovery-live/70c796d87da64f80b8691cd6f7dd325e/`, with simulator/PFX-cleanup summary at `.runstate/order-provider-recovery-local/ce2c661528a14c349a67c1b12eef461e/`. The hosted summary records matrixPassed=true, cleanupPassed=true, three provider-boundary interruptions, three Payment-host interruptions and no raw service logs. No selected external-provider evidence is retained; that remains a release gate.
