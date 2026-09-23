# Shared Contracts

This project contains stable cross-context contracts only. `IntegrationEvents/RestaurantWorkflowEvents.cs` defines versioned events for the first restaurant workflow:

- `OrderSubmittedV1`
- `InventoryReservedV1` / `InventoryReservationRejectedV1`
- `KitchenTicketCreatedV1`, `KitchenTicketQueuedV1`, and `KitchenTicketStatusChangedV1`
- `PaymentCompletedV1` / `PaymentFailedV1`

These records are integration contracts, not domain entities. Each bounded context keeps its own aggregate and persistence model.

`IntegrationEvents/PosCashCloseSnapshotV1.cs` defines `pos.cash-close.snapshot.v1`: a full versioned POS cash-session snapshot with tenant/store scope, amounts, currency, current review status and capture time. Unlike safe audit events, this is restricted financial integration data and must never be logged. It excludes review reasons and actor subjects; Reporting translates it into its own model. See [contract and replay semantics](../../../docs/API/Cash-Close-Reporting.md).

`IntegrationEvents/CustomerProfileEvents.cs` defines `CustomerProfileCreatedV1`. It carries tenant/profile identifiers, status, concurrency version, and correlation only; Customer PII remains inside the Customer bounded context.

`Platform/PlatformControlPlaneContracts.cs` defines the stable organization, membership, product registration, organization-product-access, and support-elevation contracts used by the Product Owner Portal and Customer Portal boundaries. `Platform/TenantContextContracts.cs` defines the server-derived tenant context passed from a BFF to a product application use case. These are transport contracts only; authorization decisions and aggregates remain owned by Platform Directory or the product bounded context.

`IntegrationEvents/PlatformAuditEvents.cs` defines the versioned platform audit event contract. Audit events contain identifiers and outcomes, not access tokens, credentials, or sensitive payloads.
