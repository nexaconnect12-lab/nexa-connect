# Shared Contracts

This project contains stable cross-context contracts only. `IntegrationEvents/RestaurantWorkflowEvents.cs` defines versioned events for the first restaurant workflow:

- `OrderSubmittedV1`
- `InventoryReservedV1` / `InventoryReservationRejectedV1`
- `KitchenTicketCreatedV1`, `KitchenTicketQueuedV1`, and `KitchenTicketStatusChangedV1`
- `PaymentCompletedV1` / `PaymentFailedV1`

These records are integration contracts, not domain entities. Each bounded context keeps its own aggregate and persistence model.

`Platform/PlatformControlPlaneContracts.cs` defines the stable organization, membership, product registration, organization-product-access, and support-elevation contracts used by the Product Owner Portal and Customer Portal boundaries. `Platform/TenantContextContracts.cs` defines the server-derived tenant context passed from a BFF to a product application use case. These are transport contracts only; authorization decisions and aggregates remain owned by Platform Directory or the product bounded context.

`IntegrationEvents/PlatformAuditEvents.cs` defines the versioned platform audit event contract. Audit events contain identifiers and outcomes, not access tokens, credentials, or sensitive payloads.
