# NexaConnect Project Architecture

Phase 4 tenant-API status: Platform Directory resolves authenticated membership and enabled product access; Catalog, Inventory, Order, Payment, and Customer enforce product-owned permission decisions and resource ownership before their customer use cases execute. Catalog and Inventory customer persistence paths use organization-leading predicates and composite tenant keys; portals remain database-free.

## 1. Purpose

NexaConnect is a restaurant operating platform that supports staff POS terminals, touch-screen self-service kiosks, kitchen ordering and display, customer QR ordering, reporting, and external integrations. Restaurant branches must continue approved operations during internet or cloud outages and synchronize safely after recovery. The architecture separates business capabilities into independently maintainable services while keeping the initial implementation practical for a small team.

Current implementation status: the repository provides solution scaffolding, JWT validation, local identity/infrastructure configuration, schema-first PostgreSQL tooling, Platform Directory organization-access and current-tenant access contracts, separate Customer and Platform Admin BFF session boundaries with required production Redis ticket storage, distinct platform/customer/product role sets, independently approved and time-limited support elevation with append-only audit history, PostgreSQL-backed Platform Directory and POS slices, PostgreSQL adapters for Catalog, Inventory, Customer, Payment, and Notification, migration-managed service projections, durable PostgreSQL/RabbitMQ outbox and inbox primitives, PostgreSQL aggregate/idempotency persistence for Order, Keycloak client-credentials outbound authentication with retries, payment-failure compensation hooks, provider retry boundaries, a public place-order workflow endpoint, executable bounded-context API slices, and cross-service HTTP coverage for the Catalog -> Order -> Inventory -> Kitchen -> Payment workflow. Catalog, Order, Inventory, and Payment now enforce the implemented customer-facing organization/branch/order authorization paths at their owning service boundaries. Production provider credentials, offline synchronization, and authorization for remaining product resources remain planned.

The centralized observability foundation supplies structured JSON console logs, validated correlation identifiers, safe request logs, and optional OTLP signals. The Phase 4 Customer BFF and tenant services propagate correlation identifiers across their HTTP dependency chain; Platform Directory and Platform Admin BFF also adopt the foundation. Locally, logs are retained in Loki and queried in Grafana; traces and metrics reach only the Collector debug exporter. New implementations must include observability and redaction verification. Production ingestion security, durable storage, retention, access hardening, and trace/metric backends remain required. See [ADR-007](../../docs/Architecture/Decisions/ADR-007-centralized-observability-foundation.md).

The detailed restaurant domains, branch-edge topology, offline failure model, kitchen flow, QR behavior, reporting architecture, and shared identity boundary are defined in [`docs/Architecture/Restaurant-POS-Architecture.md`](../../docs/Architecture/Restaurant-POS-Architecture.md). This document summarizes the supporting project architecture.

## 2. Architectural principles

1. **Business capability boundaries** — services are organized by domain responsibility rather than technical layer.
2. **Independent data ownership** — each service owns its schema or database and other services do not update its tables directly.
3. **API-first integration** — synchronous communication uses versioned HTTP APIs; asynchronous state changes use integration events.
4. **Centralized identity** — Keycloak provides OpenID Connect and OAuth 2.0 authentication.
5. **Defense in depth** — authentication occurs centrally, while resource-level authorization remains inside each business service.
6. **Observable by default** — logging, traces, metrics, and health checks are included from the beginning.
7. **Offline-aware POS** — the Windows POS client uses local storage and reliable synchronization.
8. **Incremental microservices** — avoid splitting services until a clear deployment, scaling, ownership, or reliability requirement exists.
9. **Branch resilience** — restaurant ordering, kitchen routing, cash payment, and receipt printing must not depend on continuous WAN connectivity.
10. **One order lifecycle** — POS, waiter, kiosk, and customer QR channels converge into the same Ordering capability.
11. **Reporting projections** — reporting consumes business events and never becomes a cross-service transactional query layer.
12. **Domain-driven design** — bounded contexts own their language, models, persistence, and integration contracts; tactical patterns are applied where business complexity justifies them.

## 3. High-level architecture

```mermaid
flowchart TB
    KC[Keycloak Identity Server]
    WEB[React Web Application]
    ADMIN[React Admin Application]
    MOBILE[Native Mobile Application]
    POS[Windows POS Application]
    KIOSK[Self-Service Kiosk]
    GW[YARP API Gateway / BFF]

    WEB --> GW
    ADMIN --> GW
    MOBILE --> GW
    POS --> GW
    KIOSK --> GW

    WEB -. OIDC .-> KC
    ADMIN -. OIDC .-> KC
    MOBILE -. OIDC + PKCE .-> KC
    POS -. OIDC + PKCE .-> KC
    KIOSK -. Device authentication .-> KC
    GW -. Token validation .-> KC

    GW --> CATALOG[Catalog Service]
    GW --> INVENTORY[Inventory Service]
    GW --> ORDER[Order Service]
    GW --> CUSTOMER[Customer Service]
    GW --> PAYMENT[Payment Service]
    GW --> KITCHEN[Kitchen Service]
    GW --> POSSVC[POS Service]
    GW --> DIRECTORY[Platform Directory]
    GW --> MEDIA[Media API]

    ORDER --> BUS[(RabbitMQ)]
    ORDER --> KITCHEN
    INVENTORY --> BUS
    PAYMENT --> BUS
    POSSVC --> BUS
    BUS --> NOTIFY[Notification Service]
    BUS --> IMAGEWORKER[Image Processing Worker]

    CATALOG --> CATALOGDB[(Catalog DB)]
    INVENTORY --> INVENTORYDB[(Inventory DB)]
    ORDER --> ORDERDB[(Order DB)]
    CUSTOMER --> CUSTOMERDB[(Customer DB)]
    PAYMENT --> PAYMENTDB[(Payment DB)]
    KITCHEN --> KITCHENDB[(Kitchen DB)]
    POSSVC --> POSDB[(POS DB)]
    DIRECTORY --> DIRECTORYDB[(Platform Directory DB)]
    MEDIA --> MEDIADB[(Media Metadata DB)]
    MEDIA --> OBJECTSTORE[(MinIO / S3 Object Storage)]
    IMAGEWORKER --> OBJECTSTORE
    IMAGEWORKER --> MEDIADB

    POS --> SQLITE[(Local SQLite)]
```

## 4. Solution structure

```text
NexaConnect/
├── docs/
│   ├── Architecture/
│   ├── API/
│   ├── Database/
│   └── Deployment/
├── docker/
│   ├── keycloak/
│   ├── postgres/
│   ├── redis/
│   ├── rabbitmq/
│   ├── prometheus/
│   └── grafana/
├── scripts/
├── src/
│   ├── Aspire/
│   │   ├── NexaConnect.AppHost/
│   │   └── NexaConnect.ServiceDefaults/
│   ├── BuildingBlocks/
│   │   ├── NexaConnect.BuildingBlocks/
│   │   ├── NexaConnect.Contracts/
│   │   ├── NexaConnect.Infrastructure/
│   │   └── NexaConnect.Shared/
│   ├── Gateway/
│   │   └── NexaConnect.Gateway/
│   ├── Tools/
│   │   ├── NexaConnect.DataMigration/
│   │   └── NexaConnect.DataGeneration/
│   ├── Services/
│   │   ├── NexaConnect.Services.Catalog/
│   │   ├── NexaConnect.Services.PlatformDirectory/
│   │   ├── NexaConnect.Services.Inventory/
│   │   ├── NexaConnect.Services.Order/
│   │   ├── NexaConnect.Services.Customer/
│   │   ├── NexaConnect.Services.Payment/
│   │   ├── NexaConnect.Services.Notification/
│   │   └── NexaConnect.Services.POS/
│   └── Clients/
│       ├── NexaConnect.Web/
│       ├── NexaConnect.Admin/
│       ├── NexaConnect.Mobile/
│       └── NexaConnect.POS/
└── tests/
    ├── Unit/
    ├── Integration/
    └── Architecture/
```

## 5. Component responsibilities

### 5.1 API Gateway

`NexaConnect.Gateway` is the public entry point for application clients. It uses YARP for routing and can implement client-specific Backend-for-Frontend endpoints.

Responsibilities:

- Validate access tokens.
- Route requests to internal services.
- Apply rate limits and request-size limits.
- Add correlation identifiers.
- Aggregate selected responses when justified.
- Hide internal service addresses.

The gateway must not contain core business rules.

### 5.2 Keycloak

Keycloak is deployed as a separate, shared identity platform rather than as an ASP.NET Core project. NexaConnect and other products integrate through OpenID Connect and OAuth 2.0 using separate clients and resource scopes; they do not share application authorization tables.

Shared organization and membership data is owned by a Platform Directory capability and distributed through versioned APIs and events. Products never query shared physical platform tables. This boundary is defined by [`ADR-002`](../../docs/Architecture/Decisions/ADR-002-shared-platform-data-ownership.md).

Recommended clients:

- `nexaconnect-web-bff` — confidential client.
- `nexaconnect-admin-bff` — confidential client.
- `platform-admin-bff` — separately deployed shared-platform dashboard client, owned outside NexaConnect.
- `nexaconnect-mobile` — public client using Authorization Code with PKCE.
- `nexaconnect-pos` — public client using Authorization Code with PKCE.
- One confidential service account per machine-to-machine workload.

Implemented platform roles:

- `platform-owner`
- `platform-admin`
- `platform-support`
- `platform-auditor`

Implemented customer roles:

- `customer-owner`
- `customer-admin`
- `customer-manager`
- `customer-user`
- `customer-viewer`

Product-specific realm roles remain separate:

- `tenant-admin`
- `store-manager`
- `cashier`
- `inventory-controller`
- `accountant`
- `report-viewer`

`system-admin` and `support-agent` are legacy compatibility roles; new portal authorization uses the explicit platform role set.

### 5.3 Catalog Service

Owns products, categories, barcodes, tax classifications, price definitions, and product availability metadata.

### 5.3.1 Platform Directory Service

Owns shared organizations, identity-subject memberships, and organization-level NexaConnect access. Its versioned organization-access API evaluates active membership and application enrollment; restaurant resource authorization remains product-owned.

### 5.4 Inventory Service

Owns warehouses, stock balances, stock movements, reservations, adjustments, and replenishment operations.

### 5.5 Order Service

Owns shopping carts, sales orders, order lines, returns, order status transitions, and order-level business rules.

The first order workflow is implemented in `Application/Workflow/PlaceOrderWorkflow.cs`. It snapshots menu prices, submits the Order aggregate, reserves Inventory, creates a Kitchen ticket, authorizes Payment, and publishes versioned integration events after each accepted step. The workflow depends only on Application-owned ports; its optional HTTP adapters, PostgreSQL aggregate repository, and transactional outbox are implemented in Infrastructure. `RestaurantWorkflowCrossServiceTests` exercises the public workflow through independent Catalog, Inventory, Order, Kitchen, and Payment HTTP hosts.

### 5.6 Customer Service

Owns customer profiles, addresses, contact preferences, loyalty identifiers, and customer-specific business information.

### 5.7 Payment Service

Owns payment intents, provider transactions, payment status, refunds, and reconciliation references. It must not store sensitive card data unless the deployment is designed and certified for that purpose.

### 5.8 Kitchen Service

Owns preparation tickets, station-specific preparation snapshots, ticket status transitions, and payment-failure cancellation. It receives order-line snapshots through its authenticated HTTP API and never recalculates commercial totals or reads the Order database directly.

### 5.9 POS Service

Owns terminals, stores, shifts, cash sessions, device registration, synchronization state, and server-side processing of offline POS operations.

### 5.9 Notification Service

Consumes integration events and sends email, SMS, push, or in-application notifications. Notification failures must not roll back completed sales transactions.

### 5.10 Data Migration Tool

`NexaConnect.DataMigration` is a .NET console tool that applies ordered, transactional PostgreSQL scripts for one service-owned database at a time. Migration scripts are checksum-validated, retained in memory for execution, bounded by a 60-second command/lock timeout, and treated as immutable after application.

### 5.11 Data Generation Tool

`NexaConnect.DataGeneration` is a .NET console tool that imports deterministic, repeatable CSV sample-data packages into one service-owned PostgreSQL database at a time. It executes only in explicitly named Development or test environments. Repository SQL sample inserts are not supported; CSV imports use restricted runtime credentials and cannot target reserved operational tables.

## 6. Internal service layout

Each new or materially changed business service follows Domain-Driven Design within a Clean Architecture-inspired layout, as accepted by [`ADR-005`](../../docs/Architecture/Decisions/ADR-005-domain-driven-design.md). A service normally represents one bounded context; when a deployable contains more than one module, each module keeps an explicit model and ownership boundary. Tactical DDD is applied according to business complexity rather than used to wrap simple CRUD in unnecessary abstractions. The restaurant bounded-context map is maintained in [`Restaurant-POS-Architecture.md`](../../docs/Architecture/Restaurant-POS-Architecture.md#4-business-capability-boundaries-and-bounded-contexts).

```text
NexaConnect.Services.Order/
├── Api/
├── Application/
├── Domain/
├── Infrastructure/
├── Contracts/
└── Tests/
```

For the first implementation, these can be folders inside one project. Split them into separate `.csproj` files only when compile-time boundaries provide clear value.

The required dependency direction is API to Application to Domain. Infrastructure implements interfaces owned by Application or Domain and is composed at the application boundary. Domain must not depend on ASP.NET Core, PostgreSQL providers, HTTP clients, message brokers, or other frameworks.

Bounded contexts do not share domain entities, persistence models, or internal DTOs. Aggregates enforce invariants and define transactional consistency boundaries. Repository interfaces express aggregate needs and do not expose generic table-level CRUD. Domain events remain internal; cross-context communication uses separately versioned integration events and an anti-corruption layer where external concepts differ from the local model.

### Domain

- Entities and value objects
- Domain rules
- Domain events
- Domain-specific exceptions

### Application

- Commands and queries
- Use cases
- Validation
- Interfaces for external dependencies
- Transaction boundaries

### Infrastructure

- Entity Framework Core
- Service-owned persistence implementations and parameterized raw SQL when justified
- Database migrations
- Message broker integration
- External provider clients
- File or object storage

### API

- HTTP endpoints
- Authentication and authorization policies
- Request/response mapping
- OpenAPI configuration
- Health checks

API endpoints must remain thin and must not issue SQL or contain business workflow rules. Application use cases coordinate work through narrow interfaces. Database operations belong in Infrastructure. Raw SQL must parameterize every runtime data value and must never concatenate untrusted input; dynamic identifiers are limited to validated, allow-listed metadata and use provider quoting. PostgreSQL integration tests cover security-sensitive filtering and transaction behavior. Authorization, tenant boundaries, financial limits, and other business decisions remain explicit in Domain or Application behavior, with database constraints and queries used as defense in depth.

The Restaurant authorization-scope controller and the Platform Directory and Authorization persistence paths now follow these boundaries: Application owns the ports and Infrastructure owns the PostgreSQL adapters. POS shift, cash-session, and terminal-enrollment flows likewise use Application services and Application-owned persistence ports with Infrastructure PostgreSQL adapters. Their controllers are limited to authenticated transport context and HTTP response mapping. New work must not copy remaining legacy patterns, and material changes to that code must move it toward this structure.

## 7. Data architecture

PostgreSQL is the standard transactional database technology for NexaConnect. Each service owns its data. Initial deployments may use one PostgreSQL cluster with separate databases, schemas, roles, and credentials, but ownership boundaries must remain explicit. A shared PostgreSQL cluster must not become a shared application database.

Initial databases:

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

Versioned migrations exist for all 13 service databases and currently define 100 tables and 111 explicit indexes. Platform Directory version 3 adds append-only platform-administration audit records to its organization, access, and support-elevation state. The runner supports versioned directories; the catalogs remain pre-production until every script passes clean-install, downgrade, and re-upgrade tests against PostgreSQL 17.

Rules:

- A service never writes directly to another service database.
- Cross-service queries use APIs, read models, or replicated event-driven projections.
- Distributed database transactions are avoided.
- Schema migrations are owned and deployed by the corresponding service.
- Migrations and CSV sample-data packages are grouped by owning service under the operational data tools.
- Schema-first PostgreSQL scripts are the source of truth; each released version has paired, tested upgrade and downgrade scripts.
- Application releases declare their required per-service schema versions, and expand-and-contract changes preserve a temporary rollback compatibility window.
- Cross-product organization data is referenced by stable identifiers and consumed through Platform Directory APIs, events, or controlled local projections rather than shared tables.
- Flexible business attributes use PostgreSQL `jsonb` when a relational core with extensible attributes is appropriate.
- Each service receives only the database permissions required for its owned database or schema.
- Runtime database operations pass through the owning service's Infrastructure persistence implementations. API, Application, and Domain code do not issue database commands directly.
- Raw SQL is limited to Infrastructure and schema migration tooling, parameterizes every runtime data value, never concatenates untrusted input, and runs with least-privilege credentials and explicit transaction boundaries. Dynamic identifiers come only from validated, allow-listed metadata and use provider quoting.
- Local Compose infrastructure ports bind to loopback only; production infrastructure is not exposed directly to public networks.

### 7.1 Image storage and processing

Image binaries are stored in object storage rather than in a transactional database. Use MinIO for local development and an S3-compatible managed object store for production.

Image metadata is stored in PostgreSQL and owned by the relevant business capability. Catalog may own product-image associations, while a dedicated Media capability may own upload state, object keys, checksums, dimensions, processing status, and generated variants.

Image transformation is performed asynchronously by a dedicated .NET worker:

1. A client uploads an image through the Media API or a time-limited object-storage upload URL.
2. The API validates the request, records metadata in PostgreSQL, and publishes a processing request through the transactional outbox.
3. RabbitMQ delivers the request to the image-processing worker.
4. The worker validates and transforms the source image, writes generated variants to object storage, and updates processing status and metadata in PostgreSQL.
5. Consumers use idempotency keys and checksums so retries do not create duplicate variants.

Recommended technology allocation:

| Requirement | Technology |
| --- | --- |
| Main transactional database technology | PostgreSQL |
| Flexible business attributes | PostgreSQL `jsonb` |
| Image files | MinIO locally; S3-compatible object storage in production |
| Image metadata | PostgreSQL |
| Image transformation | Dedicated .NET worker |
| Processing queue | RabbitMQ |
| MongoDB | Add only when complex document-oriented results justify another data store |
| MongoDB GridFS | Use only when object storage is unsuitable |

MongoDB is not part of the initial platform baseline. It may be introduced inside a specific bounded context for complex, independently queried document-oriented results, such as deeply nested AI detections or annotation histories. GridFS is not the default image store.

The detailed PostgreSQL topology, initial logical models, and migration and sample-data workflows are documented in [`docs/Database/Database-Design.md`](../../docs/Database/Database-Design.md).

## 8. Communication patterns

### Synchronous communication

Use HTTP/JSON initially. Use gRPC only for measured internal performance needs or strongly typed streaming scenarios.

Use synchronous calls when the caller needs an immediate response, such as retrieving product details or validating current availability.

### Asynchronous communication

Use RabbitMQ integration events for state changes that can be processed independently.

Example events:

- `ProductPriceChanged`
- `InventoryAdjusted`
- `OrderSubmitted`
- `InventoryReserved`
- `PaymentCompleted`
- `SaleCompleted`
- `ReceiptRequested`

Events are versioned contracts stored in `NexaConnect.Contracts`.

### Reliability

Services that update a database and publish an event must use the transactional outbox pattern. Event consumers must be idempotent and retain processed-message identifiers where necessary.

Durable consumers use service-owned inbox tables with leases, retry attempts, and completion markers so redeliveries are suppressed only after handler side effects succeed.

## 9. POS offline design

The recommended restaurant topology uses an always-on branch edge service so POS terminals, self-service kiosks, and kitchen displays can coordinate over the local network during WAN or cloud outages. The branch-edge decision must be confirmed before implementation. Windows POS and kiosk clients should also use local SQLite outboxes for brief device-to-edge outages.

The complete failure matrix, synchronization contract, kitchen behavior, QR limitations, and edge responsibilities are defined in [`docs/Architecture/Restaurant-POS-Architecture.md`](../../docs/Architecture/Restaurant-POS-Architecture.md).

Local data includes:

- Cached products and prices
- Store and terminal configuration
- Current shift information
- Pending sales
- Pending payment confirmations where permitted
- Synchronization outbox
- Synchronization checkpoints

Offline operations use client-generated globally unique identifiers. The server must support idempotency so resending the same operation does not create duplicate sales or payments.

Sensitive manager operations, high-value refunds, and terminal revocation checks should require an online connection according to configurable policy.

## 10. Frontend architecture

### Web and Admin

Recommended stack:

- React
- TypeScript
- Vite
- Ant Design
- React Router
- TanStack Query
- React Hook Form or Ant Design Form
- Zod

Organize by business feature:

```text
src/
├── app/
├── features/
│   ├── catalog/
│   ├── inventory/
│   ├── orders/
│   ├── customers/
│   └── reporting/
├── shared/
├── api/
└── layouts/
```

The browser should preferably authenticate through an ASP.NET Core BFF using secure HTTP-only cookies. Avoid storing long-lived refresh tokens in browser local storage.

The implemented `src/Frontend` npm workspace supplies eight independently versionable foundations: design system, layout/navigation, BFF API contracts, form validation, localization, error handling, authorization UI helpers, and telemetry. The API client uses same-origin cookies and never stores bearer tokens. Telemetry removes sensitive attribute categories and portals emit distinct service names. Authorization helpers accept only a portal-owned capability evaluator and affect presentation; they do not share roles, tenant resolution, policies, or runtime authorization decisions across portals. BFFs and owning services remain authoritative for every request.

Administration follows [`ADR-003`](../../docs/Architecture/Decisions/ADR-003-platform-and-product-dashboard-separation.md) and [`ADR-006`](../../docs/Architecture/Decisions/ADR-006-portal-separation-and-tenant-isolation.md). The shared platform owns a separately deployed Product Owner Portal for cross-product control-plane functions. NexaConnect owns `NexaConnect.Admin` for product-specific administration, and `NexaConnect.Web` is the starting point for the tenant-scoped Customer Portal. Each portal uses a separate OIDC client, BFF, cookie, scope, audience, API, and deployment boundary. None accesses PostgreSQL directly.

The complete Phase 7 compatibility implementation lives in `src/Frontend/apps/product-owner-portal`; durable ownership remains with the future shared-platform repository under ADR-006. It covers organization lifecycle, membership changes, product registration/enablement, platform-user lifecycle and roles, audit, the approved support-elevation lifecycle, directory summaries, and controlled product-admin links. Publishing `NexaConnect.PlatformAdminBff` builds and serves the SPA on the same origin with explicit browser security and caching policies.

The current Customer Portal BFF is `NexaConnect.CustomerBff`. It keeps tokens server-side and protects the selected tenant in an HTTP-only cookie. Phase 8 includes Platform Directory-owned memberships; Restaurant-owned branch and typed configuration management; Reporting-owned dashboards, sales reports, and bounded activity-projection preview reads; and Media-owned metadata reads. Exact-organization access and operation-specific Authorization decisions remain mandatory. Membership and Restaurant sources publish through transactional outboxes and Reporting consumes durably; Media publication and object workflows are staged.

The authenticated product adapters forward Customer Portal requests with the server-held bearer token and protected tenant context. Catalog, Inventory, Order, Payment, and Customer independently verify Platform Directory access and evaluate operation-specific product permissions. Branch resources additionally validate Restaurant ownership; Payment validates referenced Order ownership. Conflicting browser identifiers fail closed, and customer reads use organization-scoped resource lookup behavior.

Customer requests resolve the stable identity subject, organization membership, and enabled product access through the Platform Directory current-access API, then apply product-specific authorization before application use cases execute. The Product Owner control plane has Application-owned organization, membership, product-registration, product-access, and support-elevation use cases backed by Infrastructure PostgreSQL persistence. Support elevation requires a scoped reason, independent platform-owner/admin approval, an expiry of at most four hours, and append-only lifecycle audit records. Platform roles do not automatically grant customer product permissions, and browser-supplied tenant identifiers are never authorization proof.

### Mobile

Use .NET MAUI or React Native. Native clients use Authorization Code with PKCE and secure operating-system credential storage.

### Windows POS

Use WPF or WinUI 3 when deep Windows hardware integration is required. Hardware adapters should be isolated behind interfaces for receipt printers, barcode scanners, cash drawers, customer displays, and payment terminals.

### Self-service kiosk

The kiosk is a touch-first ordering client, not a separate business service. It uses the shared Menu, Ordering, Kitchen, Payment, POS Operations, and Reporting capabilities. It requires locked-down device mode, customer-session clearing, device authentication, local caching and outbox behavior, accessibility, and isolated hardware adapters. Windows-native and browser/PWA delivery remain options until the hardware profile is confirmed.

## 11. Shared projects

Shared code must be kept deliberately small.

### `NexaConnect.Contracts`

Contains integration event contracts and stable cross-service message definitions.

### `NexaConnect.Shared`

Contains low-level primitives that have no business ownership, such as result types, correlation helpers, and common serialization conventions.

### `NexaConnect.Infrastructure`

Contains reusable infrastructure registration helpers. It must not become a dependency that couples all services to one database or messaging implementation.

### `NexaConnect.BuildingBlocks`

Contains carefully selected architectural building blocks such as outbox abstractions, idempotency interfaces, and domain event dispatching.

Do not place service-specific entities or business rules in shared projects.

## 12. Observability

Every backend component should provide:

- Structured logs
- Distributed traces
- Metrics
- Liveness and readiness health checks
- Correlation IDs

Use `NexaConnect.Observability` for structured JSON console logging, correlation propagation, and OpenTelemetry instrumentation. Platform Directory, both BFF foundations, and the Phase 4 Catalog, Inventory, Order, Payment, Customer, Authorization, and Restaurant services can export optional OTLP signals through the local Collector. Only logs are stored in Loki and queryable in Grafana; traces and metrics use the Collector debug exporter. Operational telemetry never replaces durable business audit records.

Never log passwords, access tokens, refresh tokens, payment secrets, or sensitive personal data.

## 13. Security

- TLS is required outside local development.
- Production services use password-protected TLS certificates and service-owned encrypted ASP.NET Data Protection key rings; certificate passwords and key paths come from deployment secret/configuration management.
- Access tokens should be short-lived.
- Authorization policies must validate tenant and store boundaries.
- Public clients must not contain client secrets.
- Secrets must come from environment variables, development user secrets, or a managed secret store.
- Administrative endpoints require separate roles and stronger controls.
- Audit records should be immutable from normal application workflows.
- API input is validated at the boundary.
- Rate limiting is applied to authentication-sensitive and public endpoints.

## 14. Testing strategy

### Unit tests

Test domain rules, calculations, validation, and application use cases without external infrastructure.

### Integration tests

Test database mappings, migrations, message publication, event consumption, authentication policies, and API behavior using disposable infrastructure where practical.

### Architecture tests

Enforce boundaries such as:

- Domain must not depend on Infrastructure.
- Services must not reference another service's implementation assembly.
- API layers must not contain domain persistence logic.
- Bounded contexts must not share domain entities or persistence models.
- Domain events must remain separate from versioned integration events.

### End-to-end tests

Use Playwright for web flows and targeted end-to-end tests for critical checkout, payment, and POS synchronization scenarios.

## 15. Deployment

Initial production deployment can use Docker containers on a managed container platform or virtual machines. Kubernetes should be introduced only when its operational benefits justify its complexity.

Core deployable units:

- Keycloak
- API Gateway
- Business services
- PostgreSQL
- MinIO or S3-compatible object storage
- Redis
- RabbitMQ
- Image-processing worker
- Observability components

Each service should be independently buildable, configurable, deployable, and rollback-capable.

## 16. Recommended implementation order

1. Confirm the branch-edge hardware and offline failure model.
2. Define the shared identity, tenant, restaurant, branch, employee, and role contract.
3. Model the dine-in order, modifier, kitchen ticket, shift, cash payment, and synchronization lifecycles.
4. Define idempotency, acknowledgements, checkpoints, conflicts, and offline recovery behavior.
5. Create solution standards, PostgreSQL conventions, shared service defaults, and observability.
6. Configure the shared identity clients and application authorization boundaries.
7. Build one design-validated vertical slice from POS order entry through kitchen completion and cash payment.
8. Test the slice through WAN loss, restart, retry, duplication, and recovery scenarios.
9. Add QR ordering after its online and offline availability requirements are decided.
10. Add kiosk ordering after its device, payment, peripheral, and offline requirements are decided.
11. Add reporting projections and validate replay, reconciliation, and data freshness.
12. Expand Menu, Inventory, Payment, Customer, Media, and Notification capabilities incrementally.

## 17. Architecture decisions to document later

Create Architecture Decision Records for major choices, including:

- Keycloak as identity provider
- PostgreSQL database-per-service isolation strategy
- Object-storage provider and image lifecycle policy
- Criteria for introducing MongoDB for document-oriented workloads
- RabbitMQ versus another message broker
- WPF versus WinUI for POS
- React BFF authentication approach
- Database-per-service deployment strategy
- Multi-tenancy model
- Payment provider and PCI scope
- Branch-edge deployment and support model
- QR ordering behavior during WAN outages
- Offline payment policy by payment method and provider
- Shared identity claims and cross-product authorization boundaries
- Kitchen Display System deployment model
- Synchronization conflict policy by entity and operation
- Reporting consistency, retention, and replay strategy
- Kiosk application platform, device enrollment, and locked-down deployment
- Kiosk payment, printer, scanner, cash hardware, accessibility, and outage behavior
- Platform dashboard hosting, navigation, and summary contracts
- Restaurant-owner versus internal product-operator dashboard views
The repository now includes `NexaConnect.PlatformAdminBff` as the Product Owner control-plane BFF. It has a separate OIDC/session boundary, refreshes server-held tokens, preserves bodyless downstream responses, enforces endpoint-specific platform policies, and proxies Platform Directory plus Restaurant/Authorization provisioning APIs without direct database access. Customer and Platform Admin BFFs use in-memory session caches only in Development/Test and require Redis-backed server-side ticket storage in other environments.
