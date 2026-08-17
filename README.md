# NexaConnect

In the portal roadmap, Phases 1-4, 6, and 7 are complete for their documented development scope; Phase 5 BFF hardening and the Phase 8 Customer Portal and Phase 9 Media functional slices are implemented. An opt-in Playwright harness joins the authenticated Customer Portal and Media lifecycle, but environment-specific execution, recovery, load, security validation, and production operational hardening remain release gates. Phase 10 product integration is partial; its [product-by-product exit matrix](docs/Architecture/Phase-10-Product-Integration.md) records the closed Notification, Catalog, Inventory, Payment intent-creation, Kitchen ticket-lifecycle, and Customer profile-creation slices, plus the POS cash-movement replay foothold with seven successful live PostgreSQL 17 cases. Phase 11 testing is continuous, and Phase 12 has a development foundation with production hardening planned.

Phase 4 customer tenant APIs are complete for the implemented Catalog, Inventory, Order, Payment, and Customer surface: server-resolved organization/product access, product-owned permission decisions, resource ownership, tenant-filtered persistence, and cross-tenant denial are enforced at service boundaries. Portals continue to use APIs only and never access PostgreSQL directly.

NexaConnect is a restaurant operating platform design and implementation scaffold for staff POS terminals, touch-screen self-service kiosks, kitchen ordering and display, customer QR ordering, offline branch operation, synchronization, and reporting. The current implementation provides service and client scaffolding, shared JWT validation, local identity/infrastructure configuration, schema-first PostgreSQL tooling, Platform Directory control-plane and organization-access APIs, separate Customer and Platform Admin BFF session boundaries, platform/customer role separation, audited time-limited support elevation, a WPF POS PKCE sign-in scaffold, POS shift open/close and idempotent cash-movement replay slices, and an executable Catalog/Menu â†’ Order â†’ Inventory â†’ Kitchen â†’ Payment orchestration with a PostgreSQL-backed Order aggregate, idempotency, transactional outbox, and versioned integration events. Customer-facing Catalog, Order, Inventory, Kitchen, Customer, and Payment paths enforce their implemented authorization and ownership boundaries. Production provider credentials, broader offline synchronization, and authorization for the remaining product resources remain planned work.

The Kitchen service is now implemented as an authenticated bounded-context API with in-memory and PostgreSQL ticket stores. Order's production HTTP adapters call its ticket create/cancel endpoints; tenant-authorized operators use Kitchen-owned read/transition routes. Configure `Services__Kitchen`, Kitchen persistence/dependency URLs, and its dedicated workload identity for deployment.

Customer profile creation now provides tenant-only authorization, conflict-safe replay, append-only audit that excludes profile fields, and transactional `customer.profile-created.v1`/`customer.audit.v1` publication with Reporting migration-6 compatibility. Six coordinated PostgreSQL 17/RabbitMQ acceptances passed locally, including concurrent replay, Reporting replay, and the actual 0→2→1→2 runner; the Phase 10 creation slice is closed.

The centralized logging foundation provides structured JSON console logs and optional OTLP log export through an OpenTelemetry Collector to Loki and Grafana. Platform Directory and the Platform Admin BFF are the initial adopters. Traces and metrics currently reach the Collector's local debug exporter only. See the [Observability Guide](docs/Deployment/Observability.md) and [ADR-007](docs/Architecture/Decisions/ADR-007-centralized-observability-foundation.md).
The Customer BFF, Catalog, Inventory, Order, Kitchen, Payment, Customer, POS, Authorization, and Restaurant services now also adopt the foundation and propagate validated correlation identifiers through their registered HTTP dependency chains. Centralized logging is required for future service and BFF implementations.

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
Phase 5 hardening refreshes expiring BFF access tokens in the server-side ticket, clears sessions when refresh fails, forwards bodyless downstream responses correctly, and lets platform administrators provision Restaurant hierarchy and Authorization roles through service-owned APIs.
See [Keycloak configuration](docker/keycloak/README.md), the [identity client matrix](docs/Identity/Client-Matrix.md), the [claims contract](docs/Identity/Claims-Contract.md), and the [production runbook](docs/Identity/Production-Runbook.md) for identity integration and deployment.
The cross-product Platform Admin Dashboard is owned by the shared platform; `NexaConnect.Admin` remains the independently deployed restaurant-product dashboard.

Phase 6 frontend foundations are available in `src/Frontend`: eight versioned React/TypeScript packages cover the design system, layout/navigation, BFF API contracts, form validation, localization, safe error handling, presentation-only authorization helpers, and redacted telemetry. Portals share these components and contracts but keep runtime authorization, sessions, tenant context, and policies inside their independent BFF and service boundaries.

Phase 7 provides the independently buildable Product Owner Portal compatibility implementation in `src/Frontend/apps/product-owner-portal`. It completes the defined control-plane organization, membership, product enablement, Restaurant hierarchy bootstrap, hierarchical product-role assignment, platform identity, audit, support, summary, and controlled-navigation workflows while keeping detailed customer operations in product-owned portals. Publishing the Platform Admin BFF builds and hosts the SPA on the same origin.

The recorded local Product Owner/Platform Admin origin is `https://localhost:58627`; `scripts/run-phase8-development.ps1 -Build` publishes and launches it alongside the Customer Portal stack, and `scripts/stop-phase8-development.ps1` stops both launcher-owned BFF processes.

Phase 8 provides the independently buildable Customer Portal in `src/Frontend/apps/customer-portal`. Organization profile, product switching, membership and branch management, typed product configuration, Reporting dashboards/sales/activity-preview reads, and Media management use tenant-scoped contracts. Authorization supports organization-scoped tenant administrators, restaurant-scoped store managers, and branch-scoped operational roles, with hierarchical matching constrained by organization. Media validates Catalog ownership, provider-returned object size/SHA-256, file signatures, and ClamAV results before readiness; unsafe and expired objects enter durable deletion. Organization original-upload quotas and asynchronous WebP thumbnail/display variants are implemented. Authenticated HTTP, PostgreSQL lifecycle, MinIO, and ClamAV component acceptance tests are available. An opt-in joined browser-to-provider Playwright harness is implemented; its execution and evidence in each release environment plus recovery and operational load validation remain production release gates.

## Database baseline

NexaConnect uses schema-first PostgreSQL migrations with one independently owned database per service. The initial migration catalog covers Platform Directory, Restaurant, Catalog, Inventory, Order, Kitchen, Customer, Payment, POS, Media, and Reporting.

- [Database Design](docs/Database/Database-Design.md) describes topology, ownership, logical models, and operational rules.
- [Database Guidelines](docs/Database/Database-Guidelines.md) contains the rules every service must follow.
- [Data Migration](src/Tools/NexaConnect.DataMigration/README.md) documents migration layout, current implementation status, and release validation.

Other projects consume owned data through versioned APIs and integration events. They never connect directly to another service's PostgreSQL tables.
