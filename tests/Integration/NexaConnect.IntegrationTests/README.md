# Integration Tests

This project hosts in-process API integration tests using `WebApplicationFactory`.

`RestaurantWorkflowCrossServiceTests` verifies the public Catalog -> Order -> Inventory -> Kitchen -> Payment workflow over independent HTTP service boundaries. Catalog and Inventory are seeded through their APIs, Order uses its production HTTP adapters, Payment is recorded through its service API, and the deployed Kitchen API is hosted through its real `WebApplicationFactory` boundary with a controlled store. The test asserts the paid order, inventory decrement, kitchen ticket, and payment intent.

`InboxPersistenceTests` verifies durable consumer claims against PostgreSQL when `NEXACONNECT_INBOX_INTEGRATION_DB` is configured: duplicate deliveries are suppressed, failed claims are retried, and completed messages remain suppressed.

- `GatewayAuthenticationTests` verifies JWT validation, fallback authorization, role checks, and safe BFF return URLs.
- `CatalogBranchAuthorizationTests` verifies a customer-portal Catalog read allows a branch only when the selected branch is owned by the selected organization; the test exercises the real Catalog HTTP boundary with controlled authorization dependencies.
- `OrderTenantAuthorizationTests` verifies the Order workflow rejects a customer-portal request when Order-side organization/branch authorization denies the tenant, before workflow execution.
- `RealTenantAuthorizationE2ETests` verifies the real Docker-hosted Platform Directory and Restaurant APIs when the E2E environment variables are supplied. It uses a customer access token for organization access and a workload client-credentials token for the Restaurant branch scope.
- `PosShiftApiTests` verifies POS shift authentication, sign-in → open → close lifecycle, terminal enrollment, cash-session open → movement → close lifecycle, request validation, open/close orchestration, and dependency-failure mapping. POS persistence and external Restaurant/Authorization clients are replaced with controlled test doubles; no production database is required.
- `SupportElevationPersistenceTests` verifies transactional request/approval/revocation persistence, effective-expiry filtering, and append-only audit history when `NEXACONNECT_PLATFORMDIRECTORY_INTEGRATION_DB` targets a Development/Test PostgreSQL database.

Run the suite with:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj
```

Run only the cross-service workflow test with:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~RestaurantWorkflowCrossServiceTests
```

Run the Catalog branch/resource authorization regression test with:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~CatalogBranchAuthorizationTests
```

Run the Order tenant-authorization regression test with:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~OrderTenantAuthorizationTests
```

Run the real-service tenant authorization test by setting `NEXACONNECT_E2E_PLATFORM_DIRECTORY_URL`, `NEXACONNECT_E2E_RESTAURANT_URL`, `NEXACONNECT_E2E_TOKEN_ENDPOINT`, `NEXACONNECT_E2E_USER_ACCESS_TOKEN`, `NEXACONNECT_E2E_WORKLOAD_CLIENT_ID`, `NEXACONNECT_E2E_WORKLOAD_CLIENT_SECRET`, `NEXACONNECT_E2E_ORGANIZATION_ID`, and `NEXACONNECT_E2E_BRANCH_ID`, then run:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~RealTenantAuthorizationE2ETests
```

When the variables are absent, the test returns without contacting external services; it never embeds credentials or assumes a production environment.

On Windows, stop the local service processes before rebuilding to avoid executable and assembly locks:

```powershell
.\scripts\build-development.ps1
```

Use `-Test` to build and run the solution tests. Start services again afterward with `scripts/run-pos-development.ps1`.

The PostgreSQL persistence tests are enabled explicitly with a dedicated POS database connection string:

```powershell
$env:NEXACONNECT_POS_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=NexaConnect_POS;Username=nexaconnect_migration;Password=<migration-password>'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj
```

They run only in `Development`, `Test`, or `Testing` environments and create/remove a uniquely named schema, so they do not modify the service's normal tables. Without the connection string or a safe environment name, the API integration tests still run and the database-specific tests return without opening a connection.

The authorization provisioning regression test uses the same isolated-schema pattern:

```powershell
$env:NEXACONNECT_AUTHORIZATION_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=NexaConnect_Authorization;Username=nexaconnect_migration;Password=<migration-password>'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~AuthorizationAssignmentPersistenceTests
```

It verifies that assigning the `cashier` role creates the scoped `pos.shift.open` override and that a fresh authorization decision service still grants the permission. Never run these database tests against a production database.

The durable Order outbox replay test uses an isolated schema in the Order database:

```powershell
$env:NEXACONNECT_ORDER_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=NexaConnect_Order;Username=nexaconnect_migration;Password=<migration-password>'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~OrderOutboxReplayPersistenceTests
```

It verifies that a failed event is eligible for a later claim and is removed from the replay queue only after publication is marked successful.
