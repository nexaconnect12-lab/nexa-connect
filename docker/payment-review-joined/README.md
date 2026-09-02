# Payment Review joined infrastructure

This test-only project creates the disposable infrastructure and fixture boundary for the ten-scenario Payment Review browser acceptance. It owns a fresh PostgreSQL cluster, Keycloak and its database, RabbitMQ, and Toxiproxy on dynamic loopback ports. The launcher migrates four service-owned databases, creates two realm users and eight fixtures, writes identifier-only evidence, and removes the exact generated processes, Compose project, and volumes in `finally`.

Run the non-Docker safety checks first, then explicitly authorize disposable creation and deletion:

```powershell
pwsh -NoProfile -File scripts/test-payment-review-joined-safety.ps1
pwsh -NoProfile -File scripts/test-payment-review-joined-infrastructure.ps1 -ConfirmDisposableInfrastructure -RunLiveBrowser
```

Use `-DockerExecutable <absolute-path>` when Docker is not on `PATH`. The launcher requires PowerShell 7, a local Docker socket, and restored .NET dependencies. It generates secrets in process memory, injects them through environment variables, restores the prior process environment, and keeps the structured fixture/summary JSON free of secrets. A local Docker administrator can inspect container configuration while the run exists, so use only synthetic credentials and data.

Evidence is stored under `.runstate/payment-review-joined/<run-id>/`. `fixture-identifiers.json` contains only generated identifiers and loopback ports. `summary.json` records migration, identity, fixture, application-host, proxy, browser, process-cleanup, and Compose-cleanup status. With `-RunLiveBrowser`, the launcher starts Platform Directory, Authorization, Restaurant, Inventory, Kitchen, Order, and the Customer BFF; creates the Inventory proxy; and requires 10/10. A generated 256-bit bearer token protects a loopback controller that accepts only Inventory/Kitchen running-state changes and shutdown; its configuration and token remain in child-process memory and are restored from the parent environment afterward. `-NoBuild` may be used only after current artifacts have been built. Startup failures may retain filtered diagnostics; treat them as restricted local evidence.

Any partial provisioning failure retires the whole run. Do not reuse its databases, realm, users, or fixtures, and do not run this project against production infrastructure.
