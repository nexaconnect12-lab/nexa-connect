# Disposable cash-close recovery infrastructure

Use `./scripts/test-cash-close-recovery.ps1 -ConfirmDisposableInfrastructure` from the repository root. The runner supplies generated credentials, a unique `cashclose-<guid>` Compose project, PostgreSQL 17 and RabbitMQ 4 with dynamic loopback-only ports and project-owned persistent volumes. Broker restart preserves that run's messages. It removes the project's containers/volumes afterward and restores environment variables.

The switch authorizes destructive disposal and exact acceptance-child termination. Do not point this fixture at deployed data or reuse its credentials. `-NoBuild` requires already verified tool/test binaries. Exactly two database/process cases must pass with zero skips before evidence is emitted. No live pass has been recorded in the current environment. See [coverage, output and cleanup boundaries](../../docs/Deployment/Cash-Close-Recovery.md).
