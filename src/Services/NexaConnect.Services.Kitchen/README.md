# NexaConnect Kitchen Service

The Kitchen service owns kitchen tickets, preparation snapshots, ticket status, cancellation, and the Kitchen database boundary. It does not recalculate commercial prices or connect directly to the Order database.

## Local run

```powershell
dotnet run --project src/Services/NexaConnect.Services.Kitchen --urls http://localhost:7103
```

The service defaults to in-memory persistence. For PostgreSQL, set:

```text
Persistence__Provider=PostgreSQL
ConnectionStrings__Kitchen=Host=127.0.0.1;Port=5432;Database=NexaConnect_Kitchen;Username=nexaconnect_kitchen_app;Password=<secret>
Kitchen__RestaurantId=<restaurant-guid>
```

Apply the service-owned Kitchen migration before enabling PostgreSQL persistence. Order calls the authenticated endpoints using its configured `Services__Kitchen` URL and workload bearer token.

## API

- `POST /api/kitchen/v1/tickets` creates a queued ticket from an Order snapshot.
- `GET /api/kitchen/v1/tickets/{ticketId}` reads a ticket.
- `POST /api/kitchen/v1/tickets/{orderId}/cancel` compensates a ticket after payment failure.

All routes require the NexaConnect API bearer token. Production deployments must use HTTPS and a durable PostgreSQL database.
