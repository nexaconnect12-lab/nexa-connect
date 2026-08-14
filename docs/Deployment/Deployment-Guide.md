# Deployment Guide

## Local infrastructure

1. Copy `.env.example` to `.env` and replace every placeholder password.
   Trust the local ASP.NET Core HTTPS certificate with `dotnet dev-certs https --trust`. Service-to-service Development addresses use HTTPS directly; an absent or untrusted certificate causes dependency calls to fail and certificate validation must not be disabled.
2. Start infrastructure with `docker compose up -d`.
   This includes the local OpenTelemetry Collector, Loki, and Grafana logging stack. Set `GRAFANA_ADMIN_PASSWORD` before startup.
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

The checked-in realm contains no users. Create local users in the Admin Console and assign only the coarse roles needed for development. Product Owner Portal users receive one of `platform-owner`, `platform-admin`, `platform-support`, or `platform-auditor`; customer and product roles remain separate. See [Client Matrix](../Identity/Client-Matrix.md) and [Claims Contract](../Identity/Claims-Contract.md).

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
NexaConnect_Authorization
NexaConnect_Restaurant
NexaConnect_Catalog
NexaConnect_Inventory
NexaConnect_Order
NexaConnect_Kitchen
NexaConnect_Customer
NexaConnect_Payment
NexaConnect_Notification
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

The Media write lifecycle is a hardened preview pending the full production acceptance suite. Production requires a rotated bucket/prefix-scoped identity over TLS, object-access audit logs, an HTTPS browser endpoint, explicit portal-origin CORS, and a private ClamAV endpoint with current signatures. Configure `MediaSafety__MalwareScanEnabled=true`, `MediaSafety__ClamAvHost`, and `MediaSafety__ClamAvPort`; non-development startup rejects disabled scanning. Scanner errors fail closed and must alert operators. Local Compose exposes ClamAV only at `127.0.0.1:3310`; it is acceptance infrastructure, not a production topology. Media also requires `Services__Catalog` and dedicated `nexaconnect-media-service` client-credentials settings for product-owner validation.

For Media, apply migrations 1, 2, then 3 and configure `MediaStorage__ServiceUrl/Bucket/AccessKey/SecretKey` from secret management. Keep the bucket private and configure CORS for the portal origin to PUT `Content-Type` and `x-amz-checksum-sha256`; credentials never reach the browser. Local Compose exposes MinIO at 9100/9101 and creates `nexaconnect-media`. Migration 3 is a hard Media startup prerequisite. Before enabling the UI, verify start/PUT/complete/download/delete, a quarantined invalid-signature upload, and the opt-in real MinIO/ClamAV acceptance tests. Roll out ClamAV and confirm healthy signature updates before Media. Before downgrade, disable writes, drain `media_object_deletions`, stop Media (including its deletion worker), then downgrade migrations 3 and 2. Alert on scanner failure, due deletion jobs, and any row with `attempts >= 10`.

Deploy the Product Owner Portal, NexaConnect product administration portal, and Customer Portal as separate web/BFF units. The Customer Portal BFF is `src/Gateway/NexaConnect.CustomerBff`; configure `Services__PlatformDirectory`, `Services__Restaurant`, `Services__Reporting`, `Services__Media`, `Services__Catalog`, `Services__Inventory`, `Services__Order`, `Bff__Authority`, `Bff__ClientId`, and `Bff__ClientSecret`. Catalog, Order, Inventory, and Payment customer-path authorization require their relevant `Services__PlatformDirectory`, `Services__Restaurant`, and (for Payment) `Services__Order` addresses plus `WorkloadIdentity__Authority`, a dedicated workload client ID, and a separately managed secret. The registered IDs are `nexaconnect-catalog-service`, `nexaconnect-order-service`, `nexaconnect-inventory-service`, and `nexaconnect-payment-service`. Restaurant branch and product-configuration management requires direct HTTPS `Services__PlatformDirectory` and `Services__Authorization` addresses plus Restaurant migration 3 before configuration writes. Reporting and Media require their existing migrations, service-owned connection strings, and direct Platform Directory and Authorization addresses. Both BFFs require `ConnectionStrings__BffSessionCache` outside Development/Test and use Redis for server-side authentication tickets; `BffSessionCache__InstanceName` isolates their keys. They renew expiring access tokens with server-held refresh tokens and clear the session when renewal fails. Use direct HTTPS dependency addresses; do not rely on an HTTP redirect because authorization headers can be lost. Use separate OIDC clients, session cookies, secrets, scopes, audiences, health checks, and rollback controls. The Customer Portal is tenant-scoped; the Product Owner Portal is restricted to platform control-plane APIs and must not receive broad customer data access through direct database connectivity.
Publishing `NexaConnect.PlatformAdminBff` builds the Product Owner Portal and places its static output in the BFF publish artifact's `wwwroot`, providing executable same-origin hosting without a second reverse proxy. The BFF applies no-cache HTML, immutable fingerprinted-asset caching, CSP, frame denial, referrer, MIME-sniffing, and same-origin mutation protections. `PRODUCT_OWNER_BFF_URL` is used only by the local Vite proxy. Set `VITE_PRODUCT_ADMIN_LINKS` in the publish environment to an explicit comma-separated `application-code|label|https-url` allow-list; cross-origin HTTP, script, and malformed entries are discarded. Changing this list is a reviewed deployment change. A release pipeline may set `SkipProductOwnerPortalBuild=true` only when it injects the same verified assets before packaging. Validate `/`, one fingerprinted asset, `/health`, login redirection, a rejected cross-origin mutation, and rollback to the previous complete BFF artifact before promotion.

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

Inventory and Kitchen use the shared durable inbox. Reporting migration 3 adds activity storage and reuses that inbox. Enable `Outbox__Enabled` plus secret-managed `Outbox__ConnectionString` on Platform Directory, Restaurant, and Media, and `ActivityConsumer__Enabled` plus `ActivityConsumer__ConnectionString` on Reporting. Reporting declares durable queue `nexaconnect.reporting.activity.v1` on `nexaconnect.events` and binds `*.audit.v1`. Roll out migrations, Reporting consumer, then publishers. Roll back publishers first, drain/stop the consumer, disable reads, then downgrade migration 3. Optional HTTP ingestion client/source mappings remain default-deny. Media completion/delete publication is conditional on its outbox settings.
The `src/Gateway/NexaConnect.PlatformAdminBff` project is the NexaConnect-side foundation for Product Owner control-plane operations. Deploy it separately from the Customer and product-admin BFFs with a dedicated `platform-admin-bff` OIDC client, `Services__PlatformDirectory`, `Services__Restaurant`, `Services__Authorization`, secret-managed `Bff__ClientSecret`, and `ConnectionStrings__BffSessionCache`. Assign the least-privileged platform role for each operator. The BFF enforces endpoint-specific platform roles and proxies Restaurant hierarchy and Authorization role provisioning without accessing their PostgreSQL databases. Apply the owning service migrations before enabling these routes.

Platform Directory user administration also requires `KeycloakAdmin__BaseUrl`, `KeycloakAdmin__Realm`, `KeycloakAdmin__ClientId`, and secret-managed `KeycloakAdmin__ClientSecret`. Provision the confidential client as a service account with only Keycloak `realm-management` roles `view-users`, `manage-users`, and `view-realm`. Verify token acquisition, paged user listing, role changes, and PostgreSQL audit insertion before rollout. A Keycloak network or authentication failure returns `502`; invalid requests return `400`. Keycloak mutation, role mapping, and PostgreSQL audit insertion are not a distributed transaction: on an audit failure, operators must reconcile Keycloak state and the audit gap before retrying.


Activity delivery requires RabbitMQ TLS and separately scoped publisher/consumer credentials. The outbox publisher uses mandatory publisher-confirm channels and marks rows published only after confirmation; unroutable or failed publications remain retryable. Reporting declares .dead DLQ routing for permanent payload/contract failures. Alert on unpublished outbox count, activity queue depth, DLQ depth, and oldest inbox/outbox age. Operators must inspect and correct DLQ records before controlled replay. Use durable HA/quorum queues and retention sized for the documented recovery window.
