# NexaConnect

NexaConnect is a restaurant operating platform design and implementation scaffold for staff POS terminals, touch-screen self-service kiosks, kitchen ordering and display, customer QR ordering, offline branch operation, synchronization, and reporting. The current implementation provides service and client scaffolding, shared JWT validation, local identity/infrastructure configuration, schema-first PostgreSQL tooling, a Platform Directory organization-access API, a web BFF session flow, a WPF POS PKCE sign-in scaffold, a POS shift open/close vertical slice, and the first executable Catalog/Menu → Order → Inventory → Kitchen → Payment orchestration with versioned integration events. Production service adapters, RabbitMQ delivery, durable workflow persistence, offline synchronization, and broader product resource authorization remain planned work.

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
See [Restaurant POS Architecture](docs/Architecture/Restaurant-POS-Architecture.md) for the business capabilities, branch-offline model, kitchen workflow, kiosk and QR ordering, reporting, and shared identity boundary.
See [Keycloak configuration](docker/keycloak/README.md), the [identity client matrix](docs/Identity/Client-Matrix.md), the [claims contract](docs/Identity/Claims-Contract.md), and the [production runbook](docs/Identity/Production-Runbook.md) for identity integration and deployment.
The cross-product Platform Admin Dashboard is owned by the shared platform; `NexaConnect.Admin` remains the independently deployed restaurant-product dashboard.

## Database baseline

NexaConnect uses schema-first PostgreSQL migrations with one independently owned database per service. The initial migration catalog covers Platform Directory, Restaurant, Catalog, Inventory, Order, Kitchen, Customer, Payment, POS, Media, and Reporting.

- [Database Design](docs/Database/Database-Design.md) describes topology, ownership, logical models, and operational rules.
- [Database Guidelines](docs/Database/Database-Guidelines.md) contains the rules every service must follow.
- [Data Migration](src/Tools/NexaConnect.DataMigration/README.md) documents migration layout, current implementation status, and release validation.

Other projects consume owned data through versioned APIs and integration events. They never connect directly to another service's PostgreSQL tables.
