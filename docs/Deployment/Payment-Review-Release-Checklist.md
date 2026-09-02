# Payment Review release acceptance checklist

## Development verification

- Run `scripts/test-payment-review-acceptance-safety.ps1` and frontend `npm run test:payment-review:guards`.
- Run `scripts/test-payment-review-isolated.ps1 -ConfirmDisposableInfrastructure` on a local Docker host. Retain its linked TRX with all 13 cases passing, no skips, and `cleanupPassed=true` summary. This owns a new PostgreSQL/RabbitMQ/alert environment; it does not touch the existing root Compose application databases.
- Run frontend checks/build, synthetic Payment Review browser tests and the relevant .NET suite.

## Joined operator verification

- Run `scripts/test-payment-review-joined-safety.ps1`, then `scripts/test-payment-review-joined-infrastructure.ps1 -ConfirmDisposableInfrastructure -RunLiveBrowser`. Retain sanitized summaries and require every lifecycle flag. The launcher injects generated credentials in memory, starts the loopback stack, validates the proxy and allow-listed process controller, and requires 10/10 without skips or retries.
- Record target identity, source revision, migration versions, run ID and restricted evidence location. Test fixture markers and URL labels do not establish infrastructure ownership.
- Retain joined evidence that proxy outage and Inventory, Kitchen, and combined process loss fail closed and restoration requires a fresh decision. Separately rehearse the same cases with production persistence and the target container orchestrator; local in-memory process restart is not production recovery evidence.
- Verify provider/accounting evidence before confirm-void. The UI does not query provider state, reverse captured funds or bypass Payment ownership.
- Rehearse production receiver authentication, paging, acknowledgement/escalation, threshold calibration and forward-recovery policy. Local synthetic alert delivery alone is insufficient.

## Cleanup and rollout

Deploy compatible Order endpoints before the updated BFF/SPA; maintain Authorization 5 and Reporting 13 compatibility. Use a branch-scoped accountant for the read-only acceptance identity and a restaurant-scoped store manager or organization-scoped tenant administrator for the resolver. Retire only the explicitly owned test stack after retaining approved evidence. Never delete immutable history or downgrade real financial state to make acceptance rerunnable. Failed cleanup or missing live operator evidence keeps the corresponding release gate open.

See [current evidence](../Architecture/Evidence/Phase-11-Payment-Review-Live-Verification.md) and the [operations runbook](Payment-Capture-Recovery-Runbook.md).
