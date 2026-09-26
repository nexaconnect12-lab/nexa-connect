# Disposable cash-close portal infrastructure

Use only through `scripts/test-cash-close-portal.ps1 -ConfirmDisposableInfrastructure` from the repository root. This project reuses the joined Payment Review identity template and generated realm/database naming convention; it is a separate Compose project and creates no Order fixtures.

PostgreSQL 17 supplies five independently owned service databases plus a separate Keycloak database; Keycloak 26.7.0 imports the existing realm template, and RabbitMQ 4 carries real cash-close snapshots. Published ports bind only to IPv4 loopback. The launcher supplies every secret, database/realm run ID and BFF redirect port. The initialization script separates migration ownership from runtime roles and revokes public database connection/schema creation.

The runner verifies local Docker, starts services and browsers, then destroys its exact generated project and volumes. All data and identities are synthetic. See the [joined acceptance runbook](../../docs/Deployment/Cash-Close-Portal-Acceptance.md) for cleanup and evidence boundaries.
