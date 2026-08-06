# NexaConnect Restaurant POS Architecture

## 1. Document status

This document records the business scope and architectural direction for NexaConnect before implementation. It intentionally separates confirmed requirements from decisions that still require validation.

NexaConnect is a restaurant operating platform with:

- Staff-operated point-of-sale terminals.
- Customer-operated touch-screen selling kiosks.
- A kitchen ordering and Kitchen Display System (KDS).
- Customer QR ordering from mobile browsers.
- Offline branch operation during internet or cloud outages.
- Synchronization after connectivity returns.
- Operational and management reporting.
- Authentication and authorization shared with other products through a separate identity platform.

The project will proceed through small, end-to-end vertical slices. Service boundaries, offline behavior, and data ownership must be agreed before implementing broad functionality.

## 2. Architectural priorities

1. **Restaurant operations continue during WAN outages** — order entry, kitchen routing, cash payment, and receipt printing must not depend on continuous internet access.
2. **One order model across channels** — POS, waiter devices, touch-screen kiosks, and QR ordering submit into the same Ordering capability.
3. **Local-first branch coordination** — POS terminals and kitchen displays coordinate over the restaurant LAN when cloud connectivity is unavailable.
4. **Reliable synchronization** — local operations use immutable identifiers, durable outboxes, acknowledgements, checkpoints, and idempotent cloud processing.
5. **Explicit ownership** — each capability owns its business rules and PostgreSQL data.
6. **Auditable financial operations** — payments, voids, discounts, refunds, cash movements, and offline privileged actions retain an immutable audit history.
7. **Read-optimized reporting** — reports use owned projections instead of cross-service operational queries.
8. **Shared identity without shared application coupling** — products share an OpenID Connect identity platform, not user tables or business authorization databases.

## 3. Ordering channels

```mermaid
flowchart LR
    POS[POS Terminal]
    WAITER[Waiter Device]
    KIOSK[Self-Service Kiosk]
    QR[Customer QR Order]

    ORDER[Ordering]
    KITCHEN[Kitchen Execution]
    PAYMENT[Payment]
    REPORTING[Reporting Projections]

    POS --> ORDER
    WAITER --> ORDER
    KIOSK --> ORDER
    QR --> ORDER
    ORDER --> KITCHEN
    ORDER --> PAYMENT
    ORDER -. Integration events .-> REPORTING
    KITCHEN -. Integration events .-> REPORTING
    PAYMENT -. Integration events .-> REPORTING
```

Channel-specific request models may differ, but all accepted orders must enter one authoritative order lifecycle. Existing orders keep commercial snapshots of item names, modifiers, prices, discounts, service charges, and taxes. Kiosk is an ordering client and device type, not a separate owner of order business rules.

## 4. Business capability boundaries and bounded contexts

The capabilities below are the current NexaConnect bounded-context map. Each owns its ubiquitous language, domain model, persistence, and versioned integration contracts. They do not share domain entities or persistence models. Tactical Domain-Driven Design is applied according to the complexity of each capability, following [ADR-005](Decisions/ADR-005-domain-driven-design.md).

### 4.1 Restaurant Management

Owns restaurant tenants, branches, dining areas, tables, business hours, tax and service-charge configuration, preparation stations, and device registration policies.

### 4.2 Menu

Owns menus, categories, items, sizes, variants, modifier groups, modifier choices, prices, availability, sold-out state, preparation-station assignment, and product-image associations.

The existing `Catalog` project is the provisional implementation home for this capability. A rename to `Menu` should be decided before business code makes the original name expensive to change.

### 4.3 Ordering

Owns dine-in, takeaway, kiosk, and QR orders; order lines; modifiers; guest counts; commercial snapshots; discounts; service charges; taxes; lifecycle transitions; cancellations; and void requests.

### 4.4 Kitchen Execution

Owns kitchen tickets, preparation routing, station queues, item-level preparation state, ticket state, reprints, preparation timing, and kitchen audit history.

Ordering owns what was purchased. Kitchen Execution owns how accepted items are prepared. `OrderSubmitted` or an equivalent event creates kitchen work; a notification service must not substitute for the Kitchen capability.

### 4.5 POS Operations

Owns POS terminals and kiosks, shifts, cash sessions, cash movements, receipt numbering, device enrollment and health, local synchronization state, device commands, and operation acknowledgements.

### 4.6 Payment

Owns payment intents, cash payments, card-provider references, split payments, tips, refunds, reconciliation, and payment idempotency.

Cash can normally be accepted offline. Card payments may be accepted offline only when the payment terminal and provider explicitly support an approved store-and-forward workflow.

### 4.7 Inventory

Owns ingredients or stock items, storage locations, balances, movements, reservations, recipes or depletion rules where required, adjustments, and replenishment.

The exact boundary between recipe management in Menu and stock depletion in Inventory requires a dedicated domain decision.

### 4.8 Customer

Owns optional customer profiles, contact preferences, loyalty identity, and customer-specific restaurant information. Anonymous QR ordering must not require creation of a permanent customer profile.

### 4.9 Media

Owns upload lifecycle, image metadata, processing state, generated variants, and object-storage keys. Image files remain in MinIO or S3-compatible object storage.

### 4.10 Reporting

Owns read-optimized projections for sales, payments, tax, shifts, cash, items, categories, order channels, cancellations, voids, and kitchen performance.

Reporting consumes integration events and does not become the owner of operational business facts.

## 5. Branch-resilient deployment model

The recommended topology includes an always-on branch edge service inside each restaurant. This remains a proposed decision until branch hardware and support expectations are confirmed.

```mermaid
flowchart TB
    subgraph Cloud[Cloud platform]
        CLOUDAPI[Cloud Services]
        CLOUDDB[(Service-owned PostgreSQL Databases)]
        REPORTING[Reporting Projections]
        IDP[Shared Identity Platform]
    end

    subgraph Branch[Restaurant local network]
        EDGE[Branch Edge Service]
        EDGEDB[(Durable Local Store)]
        POS1[POS Terminal]
        POS2[POS Terminal]
        KIOSK[Self-Service Kiosk]
        KDS[Kitchen Display]
        DEVICES[Printers and Local Devices]

        POS1 --> EDGE
        POS2 --> EDGE
        KIOSK --> EDGE
        KDS --> EDGE
        EDGE --> DEVICES
        EDGE --> EDGEDB
    end

    EDGE <-->|Outbox, synchronization and acknowledgements| CLOUDAPI
    CLOUDAPI --> CLOUDDB
    CLOUDAPI -. Events .-> REPORTING
    CLOUDAPI -. OIDC and OAuth 2.0 .-> IDP
```

The edge service should coordinate branch ordering and kitchen work over the LAN. Each POS terminal and kiosk should also retain a local SQLite outbox so brief device-to-edge failures do not lose an accepted operation.

## 6. Offline failure model

Offline behavior must be specified separately for each failure mode.

| Failure mode | Required behavior |
| --- | --- |
| WAN or cloud unavailable; branch LAN operational | POS, KDS, cash payments, and printing continue through the branch edge service. Operations queue for cloud synchronization. |
| POS temporarily disconnected from branch LAN | The terminal records allowed operations in local SQLite and forwards them when the edge connection returns. |
| Kiosk temporarily disconnected from branch LAN | The kiosk stops or limits new checkout according to policy, preserves accepted operations locally, and forwards them when the edge connection returns. |
| Branch edge service unavailable | The allowed terminal fallback scope must be explicitly defined; multi-terminal and kitchen coordination will be degraded. |
| Shared identity platform unavailable | Previously enrolled users may use controlled offline sessions within a configured grace period. New enrollment and sensitive operations may require connectivity. |
| Payment provider unavailable | Cash continues; card behavior follows provider-certified offline policy. |
| Cloud reporting unavailable | Branch operations continue; report projections catch up from retained events after recovery. |

## 7. Synchronization contract

Every locally accepted command must include:

- A globally unique operation identifier generated by the originating device.
- Restaurant tenant and branch identifiers.
- Terminal, user, and shift identifiers where applicable.
- A business timestamp and a device-recorded timestamp.
- The command type and contract version.
- An idempotency key appropriate to the operation.

Synchronization rules:

- Local operations are stored durably before success is shown to the operator.
- Successfully committed local operations enter an outbox in the same transaction.
- Cloud consumers process each operation identifier at most once from the business perspective.
- The cloud returns explicit acknowledgements and rejection reasons.
- Checkpoints advance only after acknowledged processing.
- Retried operations must return the original outcome rather than creating duplicate orders or payments.
- Financial conflicts are never resolved using generic last-write-wins behavior.
- Rejected or conflicting operations remain visible for operational resolution and audit.
- Local records and outbox entries are retained until acknowledgement and the configured retention period are satisfied.

## 8. Primary restaurant workflow

The first executable workflow is:

```text
Open shift
→ Create dine-in order
→ Select table and guest count
→ Add menu items and modifiers
→ Submit order
→ Route items to kitchen stations
→ Mark items queued, preparing, and ready
→ Accept cash payment
→ Print receipt
→ Synchronize after a simulated WAN outage
→ Produce a daily sales and kitchen-time report
```

The Catalog/Menu → Order → Inventory → Kitchen → Payment portion is implemented by the Order application workflow. It uses bounded-context ports and publishes `OrderSubmittedV1`, `InventoryReservedV1` or `InventoryReservationRejectedV1`, `KitchenTicketCreatedV1`, and `PaymentCompletedV1` or `PaymentFailedV1`. Unit tests supply deterministic adapters to exercise the contract boundary; PostgreSQL repositories, RabbitMQ transport, transactional outbox delivery, production HTTP adapters, idempotency, and compensation for an inventory or kitchen operation when payment fails remain follow-up work. Event publication is sequential and non-transactional until the outbox is implemented.

## 9. Kitchen execution rules

- Kitchen tickets are derived from accepted order changes, not directly from mutable menu data.
- Items are routed by preparation station, such as kitchen, bar, dessert, or expediter.
- An order may produce multiple station tickets while remaining one commercial order.
- Quantity changes, voids, and additions after submission create explicit kitchen adjustments.
- Ticket and item status transitions are append-only or fully audited.
- Duplicate delivery of an order event must not create duplicate kitchen work.
- KDS state must continue over the restaurant LAN during a WAN outage.

## 10. QR ordering

The QR token identifies a restaurant branch and normally a table or ordering context. It must be opaque, revocable, and protected against guessing or cross-branch use.

A customer QR flow normally includes:

1. Scan table QR code.
2. Resolve restaurant, branch, and table context.
3. Load the active menu and current availability.
4. Build a cart with modifiers.
5. Submit an order or request staff approval according to restaurant policy.
6. Pay online, pay at the counter, or add to the table account according to policy.
7. Receive order status without gaining access to other table orders.

Cloud QR ordering becomes unavailable when the restaurant loses internet access unless customers join restaurant Wi-Fi and a secure local ordering endpoint is provided. Local offline QR support therefore requires an explicit networking, DNS, TLS, guest-Wi-Fi, and threat-model decision.

## 11. Touch-screen kiosk selling

The self-service kiosk is a branch-enrolled ordering client that uses the shared Menu, Ordering, Kitchen, Payment, POS Operations, and Reporting capabilities.

A normal kiosk flow is:

1. Start a new anonymous customer session.
2. Choose language, order type, and dine-in or takeaway context.
3. Browse the branch menu, availability, modifiers, and prices.
4. Build and review the order.
5. Submit payment or select an allowed pay-at-counter flow.
6. Send the accepted order into the same Ordering and Kitchen lifecycle as other channels.
7. Print or display the receipt and collection number.
8. Clear all customer-session data before returning to the welcome screen.

Kiosk requirements:

- Large, accessible touch targets and supported languages.
- Locked-down operating-system kiosk mode.
- Automatic customer-session timeout and privacy clearing.
- Device authentication separate from customer identity.
- Staff maintenance and manager override authentication.
- Payment-terminal and optional receipt-printer adapters behind interfaces.
- Local menu and availability cache with version and freshness indicators.
- Globally unique operation identifiers and a durable local outbox.
- Explicit behavior when the edge service, kitchen, printer, or payment provider is unavailable.

The implementation platform remains undecided. A Windows-native client is appropriate when deep payment-terminal, printer, scanner, or device-control integration is required. A browser or PWA remains an option for simpler hardware profiles.

## 12. Reporting architecture

Operational services publish versioned facts such as:

- `OrderOpened`
- `OrderSubmitted`
- `OrderLineAdded`
- `KitchenItemStarted`
- `KitchenItemReady`
- `PaymentCompleted`
- `OrderVoided`
- `ShiftOpened`
- `ShiftClosed`
- `CashMovementRecorded`

Cloud reporting consumes these events into a dedicated read store. Reports are eventually consistent and expose their data freshness. Branch-local reports required during an outage use the edge store and clearly identify unsynchronized data.

Initial reporting scope:

- Daily sales by branch, terminal, employee, channel, and payment method.
- Tax, service charge, discount, refund, cancellation, and void summaries.
- Item, category, modifier, and time-period performance.
- Shift and cash reconciliation.
- Kitchen queue time, preparation time, and completion time.
- Comparison of POS, waiter, kiosk, and QR ordering channels.

## 13. Shared identity and authorization

Keycloak is the proposed shared identity platform for NexaConnect and other products. Sharing occurs through OpenID Connect and OAuth 2.0 contracts, not through direct access to Keycloak tables or another application's authorization database.

A separately owned Platform Directory provides shared organization identity, common identity-to-organization membership, registered applications, and organization-level application access when those records are required across products. NexaConnect and other products consume that information through versioned APIs and events; they do not share the Platform Directory's tables.

```mermaid
flowchart LR
    IDP[Keycloak]
    DIRECTORY[Platform Directory]
    PLATFORMDB[(Platform PostgreSQL)]
    NEXA[NexaConnect]
    OTHER[Other Product]

    IDP --> DIRECTORY
    DIRECTORY --> PLATFORMDB
    DIRECTORY -->|API and events| NEXA
    DIRECTORY -->|API and events| OTHER
```

The identity platform owns authentication, common user identity, credentials, and shared organization membership. NexaConnect owns restaurant-specific authorization such as:

- Branch access
- Terminal enrollment
- Applying discounts
- Voiding orders
- Opening and closing shifts
- Issuing refunds
- Viewing financial reports
- Performing manager overrides

Each product receives separate identity clients and resource scopes. Shared claims require a versioned contract and stable identifiers.

Offline authorization requires prior online enrollment, cached validated permissions, a configurable grace period, local PIN or device-assisted unlock where appropriate, and an audit record for every privileged offline action. High-risk actions may require connectivity or a manager override according to policy.

The branch edge stores only a minimal local projection: identity subject, organization, restaurant and allowed branches, employee profile, effective permissions, enrollment time, expiry, and last synchronization time. Revocation and suspension events must be applied idempotently when connectivity is available.

The accepted cross-product ownership decision is recorded in [ADR-002](Decisions/ADR-002-shared-platform-data-ownership.md).

## 14. Administration dashboards

Administration is separated into a cross-product control plane and independently deployed product dashboards.

### Platform Admin Dashboard

The shared platform owns a Platform Admin Dashboard for organizations, common memberships, registered products and websites, organization-level product access, shared reference data, platform support, and approved ecosystem-level reporting. It accesses Platform Control Plane APIs and does not connect directly to product databases.

The Platform Admin Dashboard does not manage restaurant menus, orders, kitchen tickets, payments, shifts, or detailed restaurant reports.

### NexaConnect Admin Dashboard

NexaConnect owns `NexaConnect.Admin`. It manages restaurant-specific configuration and operations, including restaurants, branches, employees, menus, modifiers, tables, QR codes, POS terminals, kiosks, kitchen displays, preparation stations, shifts, cash, inventory, payments, and restaurant reporting.

Future products own separate administration dashboards and product APIs. A property-listing dashboard, for example, owns property listings, agents, media, approvals, inquiries, and property reporting without depending on NexaConnect.

Each dashboard has a separate OIDC client and BFF session boundary. Initial clients include `platform-admin-bff` and `nexaconnect-admin-bff`. Platform roles do not automatically grant restaurant operational permissions; audited, time-limited support elevation is required when cross-boundary access is necessary.

Platform reporting contains approved ecosystem summaries. Detailed restaurant reporting remains inside NexaConnect. The accepted dashboard separation is recorded in [ADR-003](Decisions/ADR-003-platform-and-product-dashboard-separation.md).

## 15. Data and media direction

- PostgreSQL is the transactional database technology.
- Each cloud service owns its PostgreSQL database or isolated schema and credentials.
- Schema-first PostgreSQL migrations are the source of truth, with paired and tested upgrade and downgrade paths for every released version.
- Application releases declare required schema versions per service and prefer expand-and-contract compatibility for rollback.
- PostgreSQL `jsonb` is used only for flexible attributes with clear ownership.
- POS terminals use SQLite for local state and durable outboxes.
- The branch edge service requires a durable local store; PostgreSQL versus SQLite remains a deployment decision.
- MinIO is used for local image development and S3-compatible object storage for production.
- RabbitMQ carries asynchronous cloud integration events and image-processing work.
- MongoDB is added only if a bounded context develops a justified complex document workload.

Detailed logical database guidance is in [Database Design](../Database/Database-Design.md).
The accepted migration decision is recorded in [ADR-001](Decisions/ADR-001-schema-first-versioned-migrations.md).

## 16. Testing implications

The system requires tests beyond ordinary API coverage:

- WAN interruption during order submission and payment.
- Duplicate, delayed, and out-of-order messages.
- Terminal restart with unsynchronized operations.
- Edge restart with pending outbox entries.
- Cloud rejection and conflict handling.
- Duplicate kitchen-event delivery.
- Menu or price changes while a branch is offline.
- Shift close with unsynchronized transactions.
- Identity-provider outage during an active shift.
- Reporting replay and projection rebuild.
- QR token tampering and cross-table access attempts.
- Kiosk session timeout and customer-data clearing.
- Kiosk restart with an accepted but unsynchronized order.
- Duplicate kiosk checkout and payment callbacks.
- Kiosk behavior during edge, printer, kitchen, and payment-provider failures.
- Touch accessibility, supported resolution, and locked-down device behavior.
- Platform administrators cannot access restaurant operations without explicit product authorization.
- Product administrators cannot access Platform Control Plane functions without platform authorization.
- Dashboard cookies, audiences, scopes, logout, and support-elevation expiry remain isolated.

## 17. Decisions required before implementation

1. Whether every branch will have a supported always-on edge computer.
2. Required behavior when the edge computer itself is unavailable.
3. Whether QR ordering must operate during a WAN outage.
4. Supported payment types and provider-certified offline card behavior.
5. Tenant, restaurant, branch, employee, and role model shared with the other product.
6. Whether KDS runs in a browser, installed application, or dedicated appliance.
7. Branch hardware targets, operating system, database, backup, and update mechanism.
8. Source-of-truth and resolution policy for each synchronizable entity.
9. Reporting freshness, retention, export, and regulatory requirements.
10. Fiscal receipt, tax, privacy, and audit requirements for target countries.
11. Kiosk operating system and native application versus browser/PWA delivery.
12. Kiosk payment methods, payment-terminal model, printer, scanner, and cash hardware.
13. Kiosk dine-in, takeaway, table-selection, collection-number, accessibility, and language requirements.
14. Platform Dashboard hosting domain, navigation, support elevation, and cross-product summary contracts.
15. Whether restaurant owners and internal NexaConnect product operators use role-specific views in one product dashboard or separately deployed portals.

No implementation decision should silently resolve these items. Each material decision should be recorded as an Architecture Decision Record.
