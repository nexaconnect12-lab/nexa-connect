# Shared Contracts

This project contains stable cross-context contracts only. `IntegrationEvents/RestaurantWorkflowEvents.cs` defines versioned events for the first restaurant workflow:

- `OrderSubmittedV1`
- `InventoryReservedV1` / `InventoryReservationRejectedV1`
- `KitchenTicketCreatedV1`
- `PaymentCompletedV1` / `PaymentFailedV1`

These records are integration contracts, not domain entities. Each bounded context keeps its own aggregate and persistence model.
