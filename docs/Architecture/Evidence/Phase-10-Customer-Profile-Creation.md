# Phase 10 Customer profile-creation evidence

Recorded on 2026-08-16 against the local disposable Development/Test stack. Source revision is the commit containing this evidence document, based on `318d96746d49969d18886201647f728b88128de0`.

- PostgreSQL image `postgres:17`, image ID `sha256:a426e44bac0b759c95894d68e1a0ac03ecc20b619f498a91aae373bf06d8508d`, in the healthy `nexa-connect-postgres-1` container.
- RabbitMQ image `rabbitmq:4-management`, image ID `sha256:cfba143b43318e968df9ddb1fc60c5cc5bab43eb056816ca5ac72b1b0d95172a`, observed running in the `nexa-connect-rabbitmq-1` container. Compose defines no RabbitMQ healthcheck; successful publisher-confirm and consume behavior is the acceptance signal.
- Customer and Reporting service migration connection strings loaded from the ignored local environment without printing credentials.
- A generated temporary PostgreSQL role with `CREATEDB`, removed immediately after the run.

Required process environment:

- `NEXACONNECT_ENVIRONMENT=Testing`
- `NEXACONNECT_CUSTOMER_INTEGRATION_DB`
- `NEXACONNECT_REPORTING_INTEGRATION_DB`
- `NEXACONNECT_RABBITMQ_ACCEPTANCE=1`
- `NEXACONNECT_RABBITMQ_INTEGRATION_URI`
- `NEXACONNECT_CUSTOMER_CLEAN_INSTALL_ACCEPTANCE=1`
- `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB` pointing to a disposable administrator permitted to create databases

Exact test command after setting those values:

```powershell
dotnet test "tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj" --no-restore --filter "FullyQualifiedName~CustomerPostgresIntegrationTests|FullyQualifiedName~CustomerMigrationRunnerAcceptanceTests|FullyQualifiedName~Migration_6_accepts_customer"
```

Result: 6 passed, 0 failed, 0 skipped in 22 seconds. The command output was observed directly in the implementation session; no credentials or environment file contents were captured.

The real Customer `WebApplicationFactory` boundary was separately verified with:

```powershell
dotnet test "tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj" --no-restore --filter "FullyQualifiedName~CustomerHttpBoundaryTests"
```

Result: 1 passed, 0 failed, 0 skipped. It covers fallback-policy `401`, create denial `403`, disclosure-safe read denial `404`, and successful `POST`/`GET {id}` routing.

The evidence covers:

- first-create profile/audit/two-event atomicity and profile-field exclusion;
- tenant-leading reads, matching replay, explicit conflicts, and append-only audit;
- concurrent matching and conflicting retries with one profile/audit/publication set;
- forced outbox failure rollback;
- unreachable-broker separation followed by persistent confirmed publication over a new RabbitMQ connection;
- Reporting migration-6 projection removal and controlled replay;
- the actual Customer migration runner's 0→2→1→2 lifecycle with migration-1 profile/outbox preservation.

The runner managed only `nexaconnect_customer_clean_it_<guid>`. Its generated database and temporary administrator were removed after the successful run. This evidence does not stop a running broker or prove automatic recovery of an established dispatcher connection.
