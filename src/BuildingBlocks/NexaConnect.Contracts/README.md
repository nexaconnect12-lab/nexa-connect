# Shared Contracts

Window-aware sealed reconciliation is implemented in Order 15 / Payment 14 / POS 12 (compatibility 0.28.0). Owning sources atomically retain safe before/after effective timestamps with journal revisions. Proven unrelated next-day trading no longer invalidates older seals; historical, ownership-uncertain and unattributed changes remain blocking. Managers compare current sales, tenders, refunds and cash variance with the preserved sealed baseline. Approval/finalization and exact historical-day attribution remain planned. See [attribution and comparison policy](../../../docs/Architecture/Decisions/ADR-025-window-aware-sealed-change-reconciliation.md).

`Reporting/DaySeals.cs` defines source seal commands/references, bounded journal observations and sealed reconciliation contracts. Source protocols remain HTTP v1 with existing protocol-two manifests; revisions, seals and journal entries are distinct concepts. Actor/Authorization history remains private. See [seal contract](../../../docs/API/Day-Close-Seals.md).

`Reporting/DayCutoffs.cs` adds `SourceFinancialRevision` (database epoch UUID and nonnegative Int64 revision), nullable manifest revision provenance and evidence protocol version, and reconciliation revision echoes plus `deliveryComplete`. Defaults retain protocol-one deserialization; protocol-two readiness requires explicit valid provenance and delivery proof. These are transport contracts, not financial policy or database models. See [ADR-023](../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md) and [wire compatibility and rollout](../../../docs/Deployment/Day-Close-Cutoffs.md). Revision evidence and financial payloads are not logged.

`Reporting/DayCutoffs.cs` defines source-manifest, original-event and reconciliation integration DTOs for [retained cutoff evidence](../../../docs/API/Day-Close-Cutoffs.md). No shared aggregate or database model is introduced; each source selects and validates its own evidence, and Reporting translates versioned events into local facts.

Day source summaries now include optional nullable `evidenceVersion`: an owner/window-scoped SHA-256 observation fingerprint, not a monotonic revision or completeness watermark. Null/older adapters remain compatible with the draft but block POS preparation. See [selection, limits and readiness semantics](../../../docs/API/Day-Close-Preparation.md).

`Reporting/EndOfDaySources.cs` contains version-one owning-service read DTOs for an explicit UTC owner/window, tender totals and Order/Payment/POS summaries. It contains no business policy, domain entities or persistence models. Financial amounts and summary bodies are restricted data and never logged. See [end-of-day contract](../../../docs/API/End-Of-Day-Draft.md).

This project contains stable cross-context contracts only. `IntegrationEvents/RestaurantWorkflowEvents.cs` defines versioned events for the first restaurant workflow:

- `OrderSubmittedV1`
- `InventoryReservedV1` / `InventoryReservationRejectedV1`
- `KitchenTicketCreatedV1`, `KitchenTicketQueuedV1`, and `KitchenTicketStatusChangedV1`
- `PaymentCompletedV1` / `PaymentFailedV1`

These records are integration contracts, not domain entities. Each bounded context keeps its own aggregate and persistence model.

`IntegrationEvents/OrderSaleCompletedV1.cs` defines `order.sale-completed.v1`: receipt-backed Order completion evidence with tenant/branch scope, original pricing and timestamps, and a provider-intent or manual-settlement payment identity. Order publishes through its transaction/outbox boundary; Reporting translates it into separately owned sales/payment facts. It excludes customer details and bank references, but contains restricted financial data and must never be logged. See [projection, replay and rollout](../../../docs/API/Sale-Financial-Reporting.md).

`IntegrationEvents/PosCashCloseSnapshotV1.cs` defines `pos.cash-close.snapshot.v1`: a full versioned POS cash-session snapshot with tenant/store scope, amounts, currency, current review status and capture time. Unlike safe audit events, this is restricted financial integration data and must never be logged. It excludes review reasons and actor subjects; Reporting translates it into its own model. See [contract and replay semantics](../../../docs/API/Cash-Close-Reporting.md).

`IntegrationEvents/CustomerProfileEvents.cs` defines `CustomerProfileCreatedV1`. It carries tenant/profile identifiers, status, concurrency version, and correlation only; Customer PII remains inside the Customer bounded context.

`Platform/PlatformControlPlaneContracts.cs` defines the stable organization, membership, product registration, organization-product-access, and support-elevation contracts used by the Product Owner Portal and Customer Portal boundaries. `Platform/TenantContextContracts.cs` defines the server-derived tenant context passed from a BFF to a product application use case. These are transport contracts only; authorization decisions and aggregates remain owned by Platform Directory or the product bounded context.

`IntegrationEvents/PlatformAuditEvents.cs` defines the versioned platform audit event contract. Audit events contain identifiers and outcomes, not access tokens, credentials, or sensitive payloads.
