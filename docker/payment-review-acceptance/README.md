# Isolated Payment Review acceptance infrastructure

Run from the repository root after restoring .NET dependencies:

```powershell
.\scripts\test-payment-review-isolated.ps1 -ConfirmDisposableInfrastructure
```

Requires PowerShell 7 (`pwsh`), a local Docker socket/context and Compose with `up --wait` support. The new launcher rejects Windows PowerShell 5 before side effects, avoiding its incompatible native stderr handling. Use `-DockerExecutable <absolute-path>` when Docker is not on PATH. `-NoBuild` is only for an already verified current build. The confirmation authorizes generated test-database migrations/rollback, synthetic alert delivery, and removal of this run's resources.

The launcher generates a unique `nexa-review-it-<32-hex-id>` Compose project and credentials in memory. It explicitly selects this Compose file and its empty `.env.example`, without reading the root `.env`. PostgreSQL 17 creates only `review_order` and `review_reporting` in a new cluster; RabbitMQ 4 and the two alert-rehearsal services are also run-specific. Published ports are assigned dynamically on `127.0.0.1`; state uses container tmpfs, not existing application volumes. Integration tests use the disposable administrator because clean-install tests need CREATEDB/CREATE SCHEMA. This is not a production runtime-credential pattern.

Only generated connection settings are injected into the existing 13-case matrix. The previous process environment is restored afterward. The existing matrix's HTTP cases use controlled authentication/dependencies; infrastructure isolation does not turn them into live OIDC tests.

The `finally` path removes this exact project, including its disposable volumes/network, and verifies no containers remain. Sanitized project identity, matrix outcome, cleanup outcome, and linked TRX location are retained under `.runstate/payment-review-isolated/<id>/`. `liveBrowserVerified` is always false: this environment does not launch Keycloak or application services. No password is written to evidence. Docker administrators can inspect container environment secrets while the containers exist; use this only on a trusted local host.

If the shell or Docker daemon terminates before cleanup, retain the generated project identity. Inspect containers by the `com.docker.compose.project` label and remove only IDs verified to belong to that run. Do not use the root project's `down -v`, broad prune commands, or delete real application databases. Failed cleanup must not be recorded as a successful release run.

The separate [joined browser suite](../../src/Frontend/e2e/payment-review-live/README.md) requires an operator-provisioned disposable application/identity stack and fresh fixtures; this launcher neither creates nor changes those accounts or application data.

Safety checks: `.\scripts\test-payment-review-acceptance-safety.ps1` runs without Docker and rejects non-generated projects, non-loopback ports, real application database names, and missing confirmation. It also simulates startup failure to verify project-scoped cleanup and restoration of the prior process environment; this simulation creates only a sanitized failed-run summary, not containers or databases.
