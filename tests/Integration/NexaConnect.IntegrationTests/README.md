# Integration Tests

This project hosts in-process API integration tests using `WebApplicationFactory`.

The joined authenticated browser acceptance suite is intentionally hosted in the frontend workspace rather than this in-process project. See `src/Frontend/e2e/phase8/README.md` for the opt-in Playwright workflow across Keycloak, Customer Portal/BFF, Media, MinIO, ClamAV, PostgreSQL, and the Media worker.

Media provider acceptance is opt-in so normal test runs do not require containers; disabled cases are reported as skipped. Start Compose services `minio`, `minio-init`, and `clamav`, set `NEXA_MINIO_ACCEPTANCE=1` and `NEXA_CLAMAV_ACCEPTANCE=1`, then filter on `MediaObjectStorageAcceptanceTests`. `MediaAuthenticatedHttpAcceptanceTests` proves an authenticated customer role crosses the HTTP authorization boundary and uses the route organization. Set `NEXACONNECT_MEDIA_INTEGRATION_DB` to a disposable Development/Test PostgreSQL database for `MediaPostgresAcceptanceTests`, which applies migrations 1-4 in an isolated schema and verifies tenant quota serialization, expiry cleanup/deletion, completion processing jobs, and generated variant metadata. Never target production infrastructure.

`RestaurantWorkflowCrossServiceTests` verifies the public Catalog -> Order -> Inventory -> Kitchen -> Payment workflow over independent HTTP service boundaries. Catalog and Inventory are seeded through their APIs, Order uses its production HTTP adapters, Payment is recorded through its service API, and the deployed Kitchen API is hosted through its real `WebApplicationFactory` boundary with a controlled store. The test asserts the paid order, inventory decrement, kitchen ticket, and payment intent.

`InboxPersistenceTests` verifies durable consumer claims against PostgreSQL when `NEXACONNECT_INBOX_INTEGRATION_DB` is configured: duplicate deliveries are suppressed, failed claims are retried, completed messages remain suppressed, and ten concurrent claimers yield exactly one processing lease. Missing configuration is reported as skipped.

`CatalogPostgresIntegrationTests` is the opt-in Phase 11 Catalog component suite. When `NEXACONNECT_CATALOG_INTEGRATION_DB` targets a disposable Development/Test PostgreSQL database, it creates isolated schemas and checks the menu/audit/two-event transaction, rollback on outbox-table failure, the append-only audit trigger, outbox-store failure/retry state, and migration 4 downgrade/re-upgrade using the checked-in migration 2-4 SQL scripts. With `NEXACONNECT_RABBITMQ_ACCEPTANCE=1` and `NEXACONNECT_RABBITMQ_INTEGRATION_URI`, it also verifies that a deliberately unreachable broker connection attempt fails, a subsequent Catalog mutation commits while no broker connection exists, and both outbox messages can later be published over a separately established real RabbitMQ connection. Publication uses the production transport's publisher confirms; the test verifies both routing keys are persistent, consumes them from an isolated queue, checks their correlation payloads, and verifies publication timestamps. It does not stop a running broker, prove automatic reconnection of an established dispatcher connection, or perform a migration-1-to-4 clean install.

- `GatewayAuthenticationTests` verifies JWT validation, fallback authorization, role checks, and safe BFF return URLs.
- `CatalogBranchAuthorizationTests` verifies a customer-portal Catalog read allows a branch only when the selected branch is owned by the selected organization; the test exercises the real Catalog HTTP boundary with controlled authorization dependencies.
- `OrderTenantAuthorizationTests` verifies the Order workflow rejects a customer-portal request when Order-side organization/branch authorization denies the tenant, before workflow execution.
- `RealTenantAuthorizationE2ETests` verifies the real Docker-hosted Platform Directory and Restaurant APIs when the E2E environment variables are supplied. It uses a customer access token for organization access and a workload client-credentials token for the Restaurant branch scope.
- `PosShiftApiTests` verifies POS shift authentication, sign-in → open → close lifecycle, terminal enrollment, cash-session open → movement → close lifecycle, request validation, open/close orchestration, and dependency-failure mapping. POS persistence and external Restaurant/Authorization clients are replaced with controlled test doubles; no production database is required.
- `SupportElevationPersistenceTests` verifies transactional request/approval/revocation persistence, effective-expiry filtering, and append-only audit history when `NEXACONNECT_PLATFORMDIRECTORY_INTEGRATION_DB` targets a Development/Test PostgreSQL database.
- `PlatformControlPlaneLiveTests` exercises the real local Keycloak Admin API and an isolated PostgreSQL schema. It verifies platform-user create/list/role/disable behavior, append-only audit persistence, cleanup, and the explicitly reconcilable partial state when identity creation succeeds before audit persistence fails.

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

Run the live Catalog Phase 11 component suite with:

```powershell
$env:NEXACONNECT_CATALOG_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=NexaConnect_Catalog;Username=nexaconnect_migration;Password=<migration-password>'
$env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'
$env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = 'amqp://<user>:<password>@localhost:5672/'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~CatalogPostgresIntegrationTests
```

The suite creates and removes unique schemas plus a unique RabbitMQ exchange and auto-delete queue. Never point it at production infrastructure.

Run the live Inventory PostgreSQL transactional suite with:

```powershell
$env:NEXACONNECT_INVENTORY_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=NexaConnect_Inventory;Username=nexaconnect_migration;Password=<migration-password>'
$env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'
$env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = 'amqp://<user>:<password>@localhost:5672/'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~InventoryPostgresIntegrationTests
```

It creates and removes unique schemas. All six component cases passed locally against PostgreSQL 17 and RabbitMQ, covering atomic outbox/audit writes and rollback, tenant isolation, concurrent same-order and competing-order behavior, idempotent release, append-only audit, checked-in migration-5 scripts, an unreachable connection attempt, broker-independent stock/reservation/release commits, and later confirmed persistent publication of all three routing keys over a new real connection and isolated queue. It does not stop a running broker or prove automatic reconnection of an established dispatcher. Without the connection string and safe environment, tests return before opening PostgreSQL; a green return-gated result is not live evidence. Never point it at production infrastructure.

`CatalogMigrationRunnerAcceptanceTests` is the destructive opt-in full Catalog database lifecycle check. It requires `NEXACONNECT_CATALOG_CLEAN_INSTALL_ACCEPTANCE=1`, `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB`, and a Development/Test environment. The supplied PostgreSQL identity must be allowed to create and drop databases. The test creates only a generated `nexaconnect_catalog_clean_it_<guid>` database, invokes the actual migration runner for versions 0→4→3→4, validates history checksums and migration-4 objects, exercises the real Catalog repository before and after downgrade/re-upgrade, and force-drops only the validated generated database during cleanup. Never supply production credentials.

```powershell
$env:NEXACONNECT_CATALOG_CLEAN_INSTALL_ACCEPTANCE = '1'
$env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=postgres;Username=<test-admin>;Password=<password>'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~CatalogMigrationRunnerAcceptanceTests
```

`InventoryMigrationRunnerAcceptanceTests` applies the same destructive opt-in boundary to Inventory. It invokes the actual runner for 0→5→4→5, verifies checksums and representative ownership across all five migrations, proves migration 5 removes only its audit/reservation-identity objects, confirms baseline outbox and simplified inventory rows survive, and exercises the real repository before and after re-upgrade. It manages only `nexaconnect_inventory_clean_it_<guid>` databases and requires a Development/Test administrator with create/drop permission.

The migration-runner case passed locally against PostgreSQL 17 together with the six Inventory component cases. The generated database and temporary administrator were removed after the run.

```powershell
$env:NEXACONNECT_INVENTORY_CLEAN_INSTALL_ACCEPTANCE = '1'
$env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=postgres;Username=<test-admin>;Password=<password>'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~InventoryMigrationRunnerAcceptanceTests
```

Run the Payment Phase 10 PostgreSQL/RabbitMQ component acceptance with:

```powershell
$env:NEXACONNECT_PAYMENT_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=NexaConnect_Payment;Username=nexaconnect_migration;Password=<migration-password>'
$env:NEXACONNECT_RABBITMQ_ACCEPTANCE = '1'
$env:NEXACONNECT_RABBITMQ_INTEGRATION_URI = 'amqp://<user>:<password>@localhost:5672/'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~PaymentPostgresIntegrationTests
```

The four component cases use isolated resources and verify atomic intent/audit/two-event commit and rollback, organization-leading reads, append-only audit, concurrent matching idempotency, conflicting-key rejection, and confirmed persistent publication over a new recovery connection. Missing opt-in configuration is reported as skipped rather than passed. They do not perform provider authorization or prove established-dispatcher reconnection.

Run the destructive Payment migration lifecycle only with a disposable Development/Test administrator:

```powershell
$env:NEXACONNECT_PAYMENT_CLEAN_INSTALL_ACCEPTANCE = '1'
$env:NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB = 'Host=localhost;Port=5432;Database=postgres;Username=<test-admin>;Password=<password>'
$env:DOTNET_ENVIRONMENT = 'Testing'
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~PaymentMigrationRunnerAcceptanceTests
```

It manages only `nexaconnect_payment_clean_it_<guid>`, invokes the actual runner for 0→1→2→1→2, seeds a version-1 intent, verifies the empty-organization upgrade marker and mechanical backfill/query boundary, and checks checksums, migration ownership, baseline outbox/intent preservation, repository writes, and atomic refusal of a colliding downgrade. The test does not establish authoritative production ownership; operators must reconcile against Order. All five Payment acceptances passed locally against PostgreSQL 17 and RabbitMQ; generated infrastructure was removed afterward.

`ReportingActivityVocabularyPostgresTests` applies Reporting migration 4 in an isolated schema and proves a Payment audit event persists through the real projection repository before destructive downgrade removes the incompatible projection and completed inbox marker, leaving the event eligible for controlled replay after re-upgrade. Set `NEXACONNECT_REPORTING_INTEGRATION_DB` and a safe environment to run it. This sixth coordinated acceptance also passed locally.

Kitchen Phase 10 acceptance uses `NEXACONNECT_KITCHEN_INTEGRATION_DB`, the shared RabbitMQ opt-in/URI, and `NEXACONNECT_KITCHEN_CLEAN_INSTALL_ACCEPTANCE=1` with the disposable PostgreSQL administrator. `KitchenPostgresIntegrationTests` verifies multi-station identity, snapshot replay, lifecycle atomicity, append-only state, rollback, tenant isolation, and confirmed publication. `KitchenMigrationRunnerAcceptanceTests` manages only `nexaconnect_kitchen_clean_it_<guid>` and runs 0→3→2→3. The Reporting vocabulary suite also verifies migration-5 Kitchen projection and replay. All five coordinated cases passed locally; generated roles/databases were removed.

Authorization migration-3 backfill acceptance uses `NEXACONNECT_AUTHORIZATION_CLEAN_INSTALL_ACCEPTANCE=1` and the same disposable PostgreSQL administrator in a safe environment. `AuthorizationMigrationRunnerAcceptanceTests` manages only `nexaconnect_authorization_clean_it_<guid>`, applies 0→2, seeds existing `tenant-admin` and `store-manager` roles, verifies 2→3 adds both Kitchen permissions to both roles, and verifies destructive 3→2 removes those permission associations.

Run the Order tenant-authorization regression test with:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~OrderTenantAuthorizationTests
```

Run the real-service tenant authorization test by setting `NEXACONNECT_E2E_PLATFORM_DIRECTORY_URL`, `NEXACONNECT_E2E_RESTAURANT_URL`, `NEXACONNECT_E2E_TOKEN_ENDPOINT`, `NEXACONNECT_E2E_USER_ACCESS_TOKEN`, `NEXACONNECT_E2E_WORKLOAD_CLIENT_ID`, `NEXACONNECT_E2E_WORKLOAD_CLIENT_SECRET`, `NEXACONNECT_E2E_ORGANIZATION_ID`, and `NEXACONNECT_E2E_BRANCH_ID`, then run:

```powershell
dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter FullyQualifiedName~RealTenantAuthorizationE2ETests
```

When the variables are absent, the test returns without contacting external services; it never embeds credentials or assumes a production environment.

Run the live Phase 3 control-plane tests only against disposable Development/Test infrastructure by setting `NEXACONNECT_PLATFORMDIRECTORY_INTEGRATION_DB`, `NEXACONNECT_KEYCLOAK_INTEGRATION_BASE_URL`, `NEXACONNECT_KEYCLOAK_INTEGRATION_REALM`, `NEXACONNECT_KEYCLOAK_INTEGRATION_CLIENT_ID`, `NEXACONNECT_KEYCLOAK_INTEGRATION_CLIENT_SECRET`, and `NEXACONNECT_ENVIRONMENT=Testing`, then filter on `PlatformControlPlaneLiveTests`. Before mutation, the tests register uniquely named `phase3-it-*` and `phase3-partial-*` usernames for cleanup; teardown discovers and deletes exact matches and independently drops the unique PostgreSQL schema, reporting any cleanup failure. Never point these variables at production.

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
