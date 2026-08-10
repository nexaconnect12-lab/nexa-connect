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

Deploy the Product Owner Portal, NexaConnect product administration portal, and Customer Portal as separate web/BFF units. Use separate OIDC clients, session cookies, secrets, scopes, audiences, health checks, and rollback controls. The Customer Portal is tenant-scoped; the Product Owner Portal is restricted to platform control-plane APIs and must not receive broad customer data access through direct database connectivity.

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

### Production Data Protection and TLS

Every production API must have its own writable, durable Data Protection key directory and a certificate containing a private key to encrypt that key ring. Configure `DataProtection__KeyDirectory`, `DataProtection__CertificatePath`, `DataProtection__CertificatePassword`, and (optionally) `DataProtection__ApplicationName` through the deployment secret/configuration store. Provision the directory before startup, restrict it to the service identity, include it in encrypted backups, and do not share it between unrelated applications.

Every production API must also expose an HTTPS listener and load a PFX certificate containing a private key. Set `ASPNETCORE_URLS` (or `Kestrel:Endpoints`) to an `https://` address and provide `Tls__CertificatePath` and `Tls__CertificatePassword`. Startup rejects missing, unreadable, passwordless, or private-key-less certificates, and rejects cleartext-only listeners outside Development and Test. Terminate TLS in the service process unless an explicitly configured trusted proxy boundary is deployed.

Use [`production.env.example`](production.env.example) as the configuration template; keep these settings out of the local development `.env` file.

## Runtime persistence and event delivery

The Catalog, Inventory, Order, Kitchen, Payment, Customer, and Notification services default to in-memory adapters for local scaffolding. The `Persistence__Provider=PostgreSQL` switch is honored by Customer and Payment repositories, the Order aggregate/idempotency/outbox, the Kitchen ticket store, and the Catalog, Inventory, and Notification adapters. Provide only the owning service's runtime connection string. When Order uses HTTP workflow adapters, configure `Authentication__OutboundToken` with a short-lived workload token; adapters attach it as a bearer token.

The Kitchen API is exposed at `/api/kitchen/v1/tickets` for authenticated Order workloads. Configure `ConnectionStrings__Kitchen`, `Persistence__Provider=PostgreSQL`, and `Kitchen__RestaurantId` when deploying it with the service-owned Kitchen database. Its HTTPS endpoint must be the value of Order's `Services__Kitchen` setting.

The Order service can persist integration events through the service-owned `outbox_messages` table and publish them through RabbitMQ when `Persistence__Provider=PostgreSQL` and an explicit `Outbox__ConnectionString` are configured. Outside Development, startup fails if the outbox connection is missing. The dispatcher claims rows with `SKIP LOCKED`, retries failed publications, and marks rows published only after the broker accepts the message. RabbitMQ credentials and TLS settings must come from the deployment secret store; do not use the local guest account in production.

In Development, each API service stores ASP.NET Data Protection keys under its own writable `.runstate/data-protection-keys/<service>` directory beneath the service content root. This prevents stale user-profile DPAPI keys from locking startup. Production deployments must configure a durable, service-owned key store with appropriate access control; the development directory is not a production secret store.

Local service launch scripts do not contain database passwords or workload secrets. `scripts/run-pos-development.ps1` loads these values from the developer-only root `.env` file (existing process environment variables take precedence): `ConnectionStrings__Authorization`, `ConnectionStrings__Restaurant`, `ConnectionStrings__POS`, and `WorkloadIdentity__ClientSecret`. PostgreSQL outbox mode also requires an explicit `Outbox:ConnectionString`; no RabbitMQ guest credential is used as a fallback.

Operational provider settings: Order outbound calls use `Authentication__TokenEndpoint`, `Authentication__ClientId`, and `Authentication__ClientSecret` to obtain short-lived Keycloak client-credentials tokens (a static `Authentication__OutboundToken` is for development only). Payment and Notification HTTP providers use bounded retries for transient 5xx/timeout responses. Notification provider delivery is enabled by setting `NotificationProvider__BaseUrl`; otherwise the configured PostgreSQL or in-memory queue remains authoritative.

Inventory and Kitchen expose the shared durable inbox store when PostgreSQL persistence is enabled. A message is claimed with a lease, marked completed only after the handler succeeds, and returned to the retryable queue after handler failure or lease expiry. The Reporting database migration includes the same inbox schema for its future projection host, but no Reporting service is currently deployed. Apply the latest service migrations before enabling durable consumers; do not treat RabbitMQ acknowledgement alone as idempotency.
