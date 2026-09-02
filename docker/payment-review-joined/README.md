# Payment Review joined infrastructure

This test-only Compose project creates the disposable infrastructure and fixture boundary for the seven-scenario Payment Review browser acceptance. It owns a fresh PostgreSQL cluster, Keycloak and its database, and Toxiproxy on dynamic loopback ports. The launcher migrates the four service-owned databases, creates two realm users, invokes the service-owned fixture provisioner, writes identifier-only evidence, and removes the exact generated Compose project and volumes in `finally`.

Run the non-Docker safety checks first, then explicitly authorize disposable creation and deletion:

```powershell
pwsh -NoProfile -File scripts/test-payment-review-joined-safety.ps1
pwsh -NoProfile -File scripts/test-payment-review-joined-infrastructure.ps1 -ConfirmDisposableInfrastructure -RunLiveBrowser
```

Use `-DockerExecutable <absolute-path>` when Docker is not on `PATH`. The launcher requires PowerShell 7, a local Docker socket, and restored .NET dependencies. It generates secrets in process memory, injects them through environment variables, restores the prior process environment, and keeps the structured fixture/summary JSON free of secrets. A local Docker administrator can inspect container configuration while the run exists, so use only synthetic credentials and data.

Evidence is stored under `.runstate/payment-review-joined/<run-id>/`. `fixture-identifiers.json` contains only generated identifiers and loopback ports. `summary.json` records migration, identity, fixture, application-host, proxy, browser, process-cleanup, and Compose-cleanup status. With `-RunLiveBrowser`, the launcher builds and starts Platform Directory, Authorization, Restaurant, Inventory, Kitchen, Order, and the Customer BFF on dynamic loopback ports; creates the run-scoped Inventory proxy; injects in-memory Playwright settings; and requires a verified 7/7 summary. `-NoBuild` may be used only after the current artifacts have already been built. Here `cleanupPassed=true` means Compose teardown succeeded with no project containers remaining, while `processCleanupPassed=true` covers the exact child hosts. Startup failures may retain filtered Docker and application diagnostics; treat them as restricted local evidence and review before sharing.

Any partial provisioning failure retires the whole run. Do not reuse its databases, realm, users, or fixtures, and do not run this project against production infrastructure.
