# Payment Review release acceptance checklist

## Development verification

- Run `scripts/test-payment-review-acceptance-safety.ps1` and frontend `npm run test:payment-review:guards`.
- Run `scripts/test-payment-review-isolated.ps1 -ConfirmDisposableInfrastructure` on a local Docker host. Retain its linked TRX with all 13 cases passing, no skips, and `cleanupPassed=true` summary. This owns a new PostgreSQL/RabbitMQ/alert environment; it does not touch the existing root Compose application databases.
- Run frontend checks/build, synthetic Payment Review browser tests and the relevant .NET suite.

## Joined operator verification — still required

- Independently verify the disposable application stack, backing databases, dedicated realm/workload identities, tenant memberships, five synthetic fixtures, and the run-scoped Inventory fault proxy specified in the [live browser guide](../../src/Frontend/e2e/payment-review-live/README.md). Record that the proxy upstream is the disposable Inventory instance, Order uses its listener, the control API is loopback-only, and the proxy starts enabled; its generated name alone is not ownership proof.
- Inject credentials through a secret mechanism, not command arguments, source files, console output or chat. Run `npm run test:e2e:payment-review:live` and retain a seven-pass verified summary. No skipped or partial suite counts as sign-off.
- Record target identity, source revision, migration versions, run ID and restricted evidence location. Test fixture markers and URL labels do not establish infrastructure ownership.
- Retain the joined browser evidence that the configured Order-to-Inventory proxy outage fails closed and restoration requires an explicit fresh decision. Separately rehearse Inventory process/container loss, Kitchen failure, and combined dependency failures; one transport-proxy exercise is not complete production outage coverage.
- Verify provider/accounting evidence before confirm-void. The UI does not query provider state, reverse captured funds or bypass Payment ownership.
- Rehearse production receiver authentication, paging, acknowledgement/escalation, threshold calibration and forward-recovery policy. Local synthetic alert delivery alone is insufficient.

## Cleanup and rollout

Deploy compatible Order endpoints before the updated BFF/SPA; maintain Authorization 5 and Reporting 13 compatibility. Use a branch-scoped accountant for the read-only acceptance identity and a restaurant-scoped store manager or organization-scoped tenant administrator for the resolver. Retire only the explicitly owned test stack after retaining approved evidence. Never delete immutable history or downgrade real financial state to make acceptance rerunnable. Failed cleanup or missing live operator evidence keeps the corresponding release gate open.

See [current evidence](../Architecture/Evidence/Phase-11-Payment-Review-Live-Verification.md) and the [operations runbook](Payment-Capture-Recovery-Runbook.md).
