# NexaConnect

Phase 3 platform control-plane APIs now include Keycloak-backed platform user administration, a Domain-owned platform role/permission catalog, append-only administration audit queries, and Platform Directory ecosystem summaries. Portals continue to use APIs only and never access PostgreSQL directly.

NexaConnect is a restaurant operating platform design and implementation scaffold for staff POS terminals, touch-screen self-service kiosks, kitchen ordering and display, customer QR ordering, offline branch operation, synchronization, and reporting. The current implementation provides service and client scaffolding, shared JWT validation, local identity/infrastructure configuration, schema-first PostgreSQL tooling, Platform Directory control-plane and organization-access APIs, separate Customer and Platform Admin BFF session boundaries, platform/customer role separation, audited time-limited support elevation, a WPF POS PKCE sign-in scaffold, a POS shift open/close vertical slice, and an executable Catalog/Menu → Order → Inventory → Kitchen → Payment orchestration with a PostgreSQL-backed Order aggregate, idempotency, transactional outbox, and versioned integration events. Customer-facing Catalog, Order, Inventory, and Payment paths enforce organization and branch ownership at their owning service boundaries. Production provider credentials, offline synchronization, and authorization for the remaining product resources remain planned work.

The Kitchen service is now implemented as an authenticated bounded-context API with in-memory and PostgreSQL ticket stores. Order's production HTTP adapters call its ticket create/read/cancel endpoints; configure `Services__Kitchen` and the Kitchen service's own connection string for deployment.

The centralized logging foundation provides structured JSON console logs and optional OTLP log export through an OpenTelemetry Collector to Loki and Grafana. Platform Directory and the Platform Admin BFF are the initial adopters. Traces and metrics currently reach the Collector's local debug exporter only. See the [Observability Guide](docs/Deployment/Observability.md) and [ADR-007](docs/Architecture/Decisions/ADR-007-centralized-observability-foundation.md).

## Initial components

- ASP.NET Core services
- YARP API Gateway
- Keycloak identity provider
- React + TypeScript + Ant Design web clients
- Product-specific NexaConnect administration dashboard
- .NET MAUI mobile client
- WPF Windows POS client
- Touch-screen kiosk client
- PostgreSQL, Redis, and RabbitMQ
- .NET Aspire for local orchestration
- OpenTelemetry-based observability

## First setup

1. Install the .NET SDK configured in `global.json`.
2. Install Docker Desktop.
3. Copy `.env.example` to `.env` and replace all placeholder secrets.
4. Restore and build `NexaConnect.sln`.
5. Start infrastructure with `docker compose up -d`.
6. Follow the [Deployment Guide](docs/Deployment/Deployment-Guide.md) for database provisioning and migrations.

See [Project Architecture](docs/Architecture/Project-Architecture.md).
See [Portal implementation phases](docs/Architecture/Portal-Implementation-Phases.md) for the agreed portal roadmap and current phase status.
See [Restaurant POS Architecture](docs/Architecture/Restaurant-POS-Architecture.md) for the business capabilities, branch-offline model, kitchen workflow, kiosk and QR ordering, reporting, and shared identity boundary.
Portal architecture is recorded in [ADR-006](docs/Architecture/Decisions/ADR-006-portal-separation-and-tenant-isolation.md): the ecosystem uses separately deployed Product Owner, product administration, and tenant-scoped Customer Portals, with shared libraries but separate BFF and identity boundaries.
The Customer Portal BFF foundation is implemented in `src/Gateway/NexaConnect.CustomerBff` and uses Platform Directory tenant-context validation.
The Product Owner control-plane compatibility BFF is implemented in `src/Gateway/NexaConnect.PlatformAdminBff`; both BFFs require a Redis-backed server-side ticket cache outside Development/Test.
See [Keycloak configuration](docker/keycloak/README.md), the [identity client matrix](docs/Identity/Client-Matrix.md), the [claims contract](docs/Identity/Claims-Contract.md), and the [production runbook](docs/Identity/Production-Runbook.md) for identity integration and deployment.
The cross-product Platform Admin Dashboard is owned by the shared platform; `NexaConnect.Admin` remains the independently deployed restaurant-product dashboard.

## Database baseline

NexaConnect uses schema-first PostgreSQL migrations with one independently owned database per service. The initial migration catalog covers Platform Directory, Restaurant, Catalog, Inventory, Order, Kitchen, Customer, Payment, POS, Media, and Reporting.

- [Database Design](docs/Database/Database-Design.md) describes topology, ownership, logical models, and operational rules.
- [Database Guidelines](docs/Database/Database-Guidelines.md) contains the rules every service must follow.
- [Data Migration](src/Tools/NexaConnect.DataMigration/README.md) documents migration layout, current implementation status, and release validation.

Other projects consume owned data through versioned APIs and integration events. They never connect directly to another service's PostgreSQL tables.
