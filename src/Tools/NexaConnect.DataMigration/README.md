# NexaConnect Data Migration

This .NET console project contains the schema-first migration runner and the service-owned PostgreSQL migration catalog. Versioned SQL scripts are the source of truth for database structure; application persistence models are mapped or generated only after the target schema is validated.

The agreed migration contract is defined by [ADR-001](../../../docs/Architecture/Decisions/ADR-001-schema-first-versioned-migrations.md).

## Implementation status

The version-1 schema baseline has been authored for 11 independently owned databases. It currently contains 83 tables and 99 explicit indexes across:

- `PlatformDirectory`
- `Restaurant`
- `Catalog`
- `Inventory`
- `Order`
- `Kitchen`
- `Customer`
- `Payment`
- `POS`
- `Media`
- `Reporting`

Every baseline migration contains `migration.json`, `up.sql`, and `down.sql`. Metadata parsing, create/drop parity, PostgreSQL identifier lengths, output packaging, and the migration project build have been checked.

The executable runner is still the original forward-only implementation. It reads flat `.sql` files from the service directory and records only a script checksum. It does **not** discover the versioned subdirectories or implement target versions, planning, advisory locking, paired downgrade execution, downgrade authorization, or release manifests.

Consequently, the baseline is an authoritative design and implementation artifact, but it is not yet approved for production execution. Upgrade the runner and execute clean-install and downgrade tests against PostgreSQL 17 before treating version 1 as released. Do not flatten or manually reorder the scripts to work around the runner.

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

The completed runner will accept an explicit target version for one service database. It will apply `up.sql` files in ascending order when upgrading and `down.sql` files in descending order when downgrading.

Planned commands:

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

The commands above document the accepted contract; they are not supported by the current executable yet.

## Ownership and access rules

- Only the owning runtime and its migration process receive credentials for a service database.
- Other services use versioned APIs, integration events, or owned projections.
- Cross-database foreign keys and direct cross-service SQL are prohibited.
- Platform Directory shares organizations and memberships through its API and events; product services store only stable identifiers.
- Keycloak owns credentials and authentication state. Application migrations never query or copy Keycloak tables.
- Media binaries remain in S3-compatible object storage; the Media database stores metadata only.
- Reporting tables are rebuildable projections and never write back to operational databases.

## Required safety behavior

The completed runner must:

- Reject missing, duplicate, or branched version sequences.
- Verify checksums for metadata, upgrade scripts, and downgrade scripts.
- Refuse to modify a previously applied migration.
- Acquire a PostgreSQL advisory lock before changing a schema.
- Produce an execution plan before mutation.
- Apply one migration version at a time.
- Use a transaction by default and require explicit metadata for non-transactional scripts.
- Update migration history atomically with transactional schema changes.
- Require `--confirm` for mutation and stronger authorization for destructive downgrades.
- Stop on the first failure and preserve diagnostic execution information.
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
