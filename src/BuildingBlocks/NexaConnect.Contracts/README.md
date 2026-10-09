# Shared Contracts

LateCashCorrections.cs adds immutable POS preview/receipt transport with exact decimal-string adjustments. PosLateCashCorrectionPostedV1 publishes a separate pos.late-cash-correction-posted.v1 event with original references and posting date/interval/variance adjustment; no Reporting consumer is included. PosDaySummary adds default-zero adjustment/count fields and includes the correction in cashVariance. Domain policy remains POS-owned. See [correction contract](../../../docs/API/Late-Cash-Corrections.md) and [operations](../../../docs/Deployment/Late-Cash-Corrections.md).

`Reporting/LateWorkReviews.cs` defines source-scoped queue/detail transport and version-bound review commands/results. Public metadata includes opaque custody UUIDs, allow-listed affected record UUIDs, bounded history and settlement references; actor, authorization decision, fingerprint, provider identity and payload stay private. Review policy and aggregates remain source-owned, and no review integration event is published. See [review contract](../../../docs/API/Late-Work-Reviews.md).

Settlement integration adds SourceBarrierCommand/Request/Proof and pos.branch-day-settled.v1 (BranchDaySettledV1). These are stable integration contracts; source Domain models and immutable receipt ownership stay inside their bounded contexts. No Reporting settlement projection is introduced. See [settlement contract](../../../docs/API/Day-Close-Settlements.md) and [operations/telemetry](../../../docs/Deployment/Day-Close-Settlements.md).

Version-one source fence transport records bind operation, scoped UTC window, approval/source seal and expiry. They expose active/cancelled status and observed owning revision; they are integration DTOs, not shared business entities or finalized settlement proof.

Exact historical-day attribution is implemented in Order 16 / Payment 15 / POS 13 (compatibility 0.29.0). Source-owned before/after selection distinguishes creation-time sales, Paid-time tenders, refund completion and drawer closure/review. Unrelated historical changes are excluded; older unresolved work, missing history, legacy descriptors and ownership uncertainty block readiness. Managers see bounded record identities, reasons, statuses, versions and effective times beside the preserved baseline/current comparison. Snapshot approval is implemented separately; durable online settlement is implemented separately by ADR-029; verified POS late-cash correction posting is implemented separately by ADR-031; other correction types and offline finalization remain planned. See [attribution and comparison policy](../../../docs/Architecture/Decisions/ADR-026-exact-historical-day-attribution.md).

`Reporting/DaySeals.cs` defines source seal commands/references, bounded journal observations and sealed reconciliation contracts. `SourceSealImpact` conveys owning classification and safe record/parent identity, before/after status, financial version and effective time; relevant/unknown `SourceSealChange` entries expose those fields. These stable transport fields contain no domain entities, monetary payloads, receipt bodies or provider data. Source responses require attribution protocol two independently of manifest provenance. Source protocols remain HTTP v1 with existing protocol-two manifests; revisions, seals and journal entries are distinct concepts. Actor/Authorization history remains private. See [seal contract](../../../docs/API/Day-Close-Seals.md).

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
