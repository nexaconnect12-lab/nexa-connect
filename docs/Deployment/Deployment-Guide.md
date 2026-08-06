# Deployment Guide

## Local infrastructure

1. Copy `.env.example` to `.env` and replace every placeholder password.
2. Start infrastructure with `docker compose up -d`.
3. Wait for the `postgres`, `keycloak-db`, and `keycloak` health checks.
4. Preview every service migration with `.\scripts\migrate-databases.ps1`.
5. After reviewing the plans, apply them with `.\scripts\migrate-databases.ps1 -Confirm`.

The application PostgreSQL container listens on `127.0.0.1:5432`. Redis (`127.0.0.1:6379`) and RabbitMQ including its management UI (`127.0.0.1:5672` and `127.0.0.1:15672`) are local-only Compose endpoints. Keycloak uses a separate PostgreSQL container and database because Keycloak owns its schema and lifecycle.

## Local identity platform

Docker Compose runs the explicitly pinned Keycloak version in development mode, imports the `nexa-dev` realm on first creation, and binds application and management ports to localhost. Verify discovery after startup:

```powershell
Invoke-RestMethod http://localhost:8080/realms/nexa-dev/.well-known/openid-configuration
Invoke-RestMethod http://localhost:9000/health/ready
```

Startup import skips an existing realm. Apply subsequent realm changes through an explicit reviewed configuration migration; do not delete the identity database volume unless losing all local identity data is intended.

The checked-in realm contains no users. Create local users in the Admin Console and assign only the coarse roles needed for development. See [Client Matrix](../Identity/Client-Matrix.md) and [Claims Contract](../Identity/Claims-Contract.md).

To run the WPF POS client locally, build it and register its custom callback protocol for the current Windows user:

```powershell
dotnet build src/Clients/NexaConnect.POS/NexaConnect.POS.csproj
./scripts/register-pos-protocol.ps1 -ExecutablePath ./src/Clients/NexaConnect.POS/bin/Debug/net10.0-windows/NexaConnect.POS.exe
dotnet run --project src/Clients/NexaConnect.POS/NexaConnect.POS.csproj
```

The protocol registration is per-user for development. Production installers must register the same exact `nexaconnect-pos://oauth/callback` callback for the signed executable and must not log callback URIs, authorization codes, or tokens.

## PostgreSQL provisioning

The local PostgreSQL initializer creates these databases on the first start of an empty `postgres-data` volume:

```text
PlatformDirectory
NexaConnect_Restaurant
NexaConnect_Catalog
NexaConnect_Inventory
NexaConnect_Order
NexaConnect_Kitchen
NexaConnect_Customer
NexaConnect_Payment
NexaConnect_POS
NexaConnect_Media
NexaConnect_Reporting
```

`nexaconnect_migration` owns every local development database and performs DDL. Each database has a separate runtime login with DML permissions but no database-creation, role-management, or schema-creation privileges. Other projects must use the owning service's API or integration events and must never receive these database credentials.

The initializer creates databases and roles only; the versioned service migrations create tables, constraints, indexes, and migration history.

## Initialization lifecycle

PostgreSQL image initialization scripts execute only when the data directory is empty. Editing `.env` or an initialization script does not alter an existing cluster.

To change passwords in an existing environment, use an approved credential-rotation procedure. Recreating the `postgres-data` volume destroys all local application database data and should be done only when a clean environment is explicitly intended.

## Production requirements

Follow the complete [Keycloak Production Runbook](../Identity/Production-Runbook.md) for identity deployment and go-live evidence.

- Provision databases and roles through the selected infrastructure-as-code and secret-management system.
- Do not expose the PostgreSQL port publicly.
- Use TLS, encrypted backups, tested restoration, and environment-specific recovery objectives.
- Give the migration process DDL access only during controlled deployments.
- Give each runtime only its own database credentials.
- Generate and approve a migration plan before mutation.
- Back up before destructive or transformative downgrade operations.
- Record schema versions and checksums in the release manifest.
- Run Keycloak with `start --optimized`, not `start-dev`.
- Configure TLS, explicit public and administrative hostnames, trusted proxy headers, health probes, metrics protection, and managed secrets.
- Keep Keycloak's Admin Console and Admin API off the public application hostname where the deployment topology permits it.
- Back up and restore the Keycloak PostgreSQL database using database-consistent procedures; realm export is not a complete backup.
- Test every pinned Keycloak upgrade in a restored non-production environment before production rollout.

The local Docker initializer is a development convenience, not the production provisioning mechanism.

## Runtime persistence and event delivery

The Catalog, Inventory, Order, Payment, Customer, and Notification services default to in-memory adapters for local scaffolding. The `Persistence__Provider=PostgreSQL` switch is honored by Customer and Payment repositories and by the Order aggregate, idempotency, and transactional outbox. Catalog, Inventory, and Notification persistence remain migration work. Provide only the owning service's runtime connection string, for example `ConnectionStrings__Order`, `ConnectionStrings__Customer`, or `ConnectionStrings__Payment`.

The Order service can persist integration events through the service-owned `outbox_messages` table and publish them through RabbitMQ when `Persistence__Provider=PostgreSQL` and an explicit `Outbox__ConnectionString` are configured. Outside Development, startup fails if the outbox connection is missing. The dispatcher claims rows with `SKIP LOCKED`, retries failed publications, and marks rows published only after the broker accepts the message. RabbitMQ credentials and TLS settings must come from the deployment secret store; do not use the local guest account in production.
