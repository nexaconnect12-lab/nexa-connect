# NexaConnect Data Migration

This .NET console project contains the schema-first migration runner and the service-owned PostgreSQL migration catalog. Versioned SQL scripts are the source of truth for database structure; application persistence models are mapped or generated only after the target schema is validated.

The agreed migration contract is defined by [ADR-001](../../../docs/Architecture/Decisions/ADR-001-schema-first-versioned-migrations.md).

## Implementation status

Schema-first catalogs have been authored for 13 independently owned databases. Platform Directory version 2 adds support-elevation state and database-enforced append-only audit history. Inventory version 5 adds durable simplified-table reservation identity and its append-only product-audit table/trigger while preserving the outbox owned by version 1. Its full seven-test acceptance passed against local PostgreSQL 17 and RabbitMQ. The migration case invokes the actual runner for 0→5→4→5 and validates history checksums, schema ownership, downgrade preservation, and repository writes before and after re-upgrade. Other services retain their independently versioned catalogs.

Payment version 2 adds organization ownership and append-only product audit while preserving the outbox owned by version 1. Its five-test acceptance passed locally against PostgreSQL 17 and RabbitMQ; the full-database case invokes the actual runner for 0→2→1→2 and verifies history, ownership, downgrade preservation, and repository writes.

Reporting version 4 expands the activity projection's database-enforced action/resource vocabulary for approved Catalog, Media, Notification, and Payment audit contracts. Live PostgreSQL coverage confirms Payment projection and destructive rollback removal of incompatible projection rows.

Kitchen version 3 adds tenant ownership, station snapshot fingerprints, multi-station uniqueness, and append-only audit/history protection while preserving migration-1 outbox and migration-2 inbox ownership. Reporting version 5 adds Kitchen audit vocabulary and replay-safe destructive rollback. Their coordinated live lifecycle passed locally.

Authorization version 3 backfills `kitchen.ticket.read` and `kitchen.ticket.transition` for existing `tenant-admin` and `store-manager` role assignments. Its destructive downgrade removes only those Kitchen permission rows.

Customer version 2 adds append-only audit that excludes profile fields while preserving the outbox owned by version 1. Reporting version 6 accepts `customer.audit.v1` profile-created vocabulary and removes incompatible projections/inbox markers on destructive downgrade so retained source events can replay after re-upgrade. Six coordinated Customer acceptances passed locally against PostgreSQL 17 and RabbitMQ, including concurrent replay, atomic rollback, confirmed publication, Reporting replay, and the actual 0→2→1→2 runner.

- `PlatformDirectory`
- `Authorization`
- `Restaurant`
- `Catalog`
- `Inventory`
- `Order`
- `Kitchen`
- `Customer`
- `Payment`
- `Notification`
- `POS`
- `Media`
- `Reporting`

Every baseline migration contains `migration.json`, `up.sql`, and `down.sql`. Metadata parsing, create/drop parity, PostgreSQL identifier lengths, output packaging, and the migration project build have been checked.

The executable runner implements the versioned-directory contract. It discovers and validates linear service catalogs, retains the checksum-validated SQL content for execution, reports status, plans explicit target versions, executes paired upgrades and downgrades, serializes mutation with a PostgreSQL advisory lock, and protects transformative and destructive downgrades with explicit authorization flags.

The baseline is still not approved for production execution until every service passes live clean-install, downgrade, and re-upgrade tests against PostgreSQL 17. Catalog has opt-in 0→4→3→4 runner acceptance; Inventory has successful local 0→5→4→5 evidence; Payment and Customer have successful local 0→2→1→2 evidence. They validate history checksums and representative repository writes and require a disposable-database administrator. Catalog's configured administrator password remains stale. The normal migration owner correctly lacks `CREATEDB`; Inventory, Payment, and Customer used and removed temporary local acceptance administrators. The remaining service catalogs still require recorded release evidence. Do not flatten or manually reorder the scripts.

## Script ownership and layout

Each service owns its scripts and database even though one operational tool executes them. Store every immutable migration in a versioned directory with metadata and paired scripts:

```text
Scripts/
└── Order/
    ├── 0001_initial_schema/
    │   ├── migration.json
    │   ├── up.sql
    │   └── down.sql
    └── 0002_add_order_channel/
        ├── migration.json
        ├── up.sql
        └── down.sql
```

Do not place cross-service schema changes in one migration.

The folder name is the service identifier used by migration tooling and connection-string configuration. Names are case-sensitive for repository conventions even when the host filesystem is not.

Example `migration.json`:

```json
{
  "version": 2,
  "name": "add_order_channel",
  "transactional": true,
  "downgradeSafety": "safe",
  "minimumApplicationVersion": "1.1.0"
}
```

Supported downgrade-safety values are:

- `safe` — restores the preceding schema without expected data loss.
- `transformative` — converts data back and requires explicit validation.
- `destructive` — may lose data and requires additional authorization and a verified backup.
- `unsupported` — blocks production release until a supported path is designed.

## Target-version behavior

The runner accepts an explicit target version for one service database. It applies `up.sql` files in ascending order when upgrading and `down.sql` files in descending order when downgrading.

Supported commands:

```powershell
# Inspect the current service schema version.
dotnet run --project src/Tools/NexaConnect.DataMigration -- `
  --service Order --status

# Preview the required upgrade or downgrade steps.
dotnet run --project src/Tools/NexaConnect.DataMigration -- `
  --service Order --target 3 --plan

# Move the database to an explicit target version.
dotnet run --project src/Tools/NexaConnect.DataMigration -- `
  --service Order --target 3 --confirm
```

Connection strings are read from `NEXACONNECT_<SERVICE>_DB` so secrets do not appear in command arguments.

Visual Studio launch profiles use `--environment-file .env` with the repository root as their working directory. This loads local connection strings without placing passwords in `launchSettings.json`. Existing process environment variables take precedence over values in the file.

`--plan` is read-only. `--confirm` is required for mutation. A transformative downgrade also requires `--allow-transformative`; a destructive downgrade requires both `--allow-destructive` and `--backup-verified`.

To plan every service to the latest catalog version, loading connection strings from the repository `.env` file:

```powershell
.\scripts\migrate-databases.ps1
```

After reviewing all plans, execute them with explicit confirmation:

```powershell
.\scripts\migrate-databases.ps1 -Confirm
```

The wrapper stops on the first failed service. Each service retains its own history, transaction boundary, connection string, and advisory lock; the wrapper does not create a cross-database transaction.

## Database provisioning

Database and role creation is deliberately separate from service schema migration. For local development, Docker Compose mounts [`docker/postgres/init/001_create_nexaconnect_databases.sh`](../../../docker/postgres/init/001_create_nexaconnect_databases.sh) into PostgreSQL's first-start initialization directory.

On an empty PostgreSQL volume, the initializer creates all 13 catalog databases, the `nexaconnect_migration` DDL owner, and one restricted runtime login per database. It also configures default table and sequence privileges for objects later created by the migration owner. The migration history table is explicitly removed from runtime-role access by the runner.

Initialization scripts run only when the PostgreSQL data directory is empty. They do not apply service migrations, rerun on ordinary container restarts, or rotate passwords in an existing cluster.

Provisioning credentials come from `.env`; migration-tool connection strings use `NEXACONNECT_<SERVICE>_DB`. Runtime services must use their own application roles rather than `nexaconnect_migration`.

## Ownership and access rules

- Only the owning runtime and its migration process receive credentials for a service database.
- Other services use versioned APIs, integration events, or owned projections.
- Cross-database foreign keys and direct cross-service SQL are prohibited.
- Platform Directory shares organizations and memberships through its API and events; product services store only stable identifiers.
- Keycloak owns credentials and authentication state. Application migrations never query or copy Keycloak tables.
- Media binaries remain in S3-compatible object storage; the Media database stores metadata only.
- Reporting tables are rebuildable projections and never write back to operational databases.

## Required safety behavior

The runner:

- Reject missing, duplicate, or branched version sequences.
- Verify checksums for metadata, upgrade scripts, and downgrade scripts.
- Refuse to modify a previously applied migration.
- Acquire a PostgreSQL advisory lock before changing a schema.
- Produce an execution plan before mutation.
- Apply one migration version at a time.
- Require every migration to be transactional; non-transactional scripts are rejected because schema mutation and migration history must remain atomic.
- Update migration history atomically with transactional schema changes.
- Require `--confirm` for mutation and stronger authorization for destructive downgrades.
- Bound database commands and advisory-lock acquisition to 60 seconds, stop on the first failure, and preserve diagnostic execution information.
- Never log connection strings or credentials.

## Upgrade and downgrade guarantee

Every released schema version must have a tested path to the preceding supported version. This does not imply that every downgrade is lossless. Destructive or externally irreversible changes require an explicit classification, backup, recovery plan, and operational approval.

Application rollback should normally be enabled through expand-and-contract database changes so application version N-1 remains temporarily compatible with schema version N. Physical schema downgrade is a controlled fallback, not the default application rollback mechanism.

## Validation required before release

For every service migration sequence:

1. Validate its metadata and immutable checksums.
2. Apply `up.sql` to an empty PostgreSQL 17 database.
3. Verify constraints, indexes, comments, and runtime permissions.
4. Exercise representative application reads and writes.
5. Apply `down.sql` to the preceding supported version.
6. Reapply `up.sql` and verify deterministic results.
7. Record the tested schema version in the application release manifest.

The initial version-1 downgrades are classified as `destructive` because returning to version 0 drops all service-owned tables and data.
