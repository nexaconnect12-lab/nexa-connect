# NexaConnect Data Migration

This .NET console tool is the planned schema-first migration runner for service-owned PostgreSQL databases. Versioned SQL scripts are the source of truth for database structure; application persistence models are mapped or generated after the schema is validated.

The agreed migration contract is defined by [ADR-001](../../../docs/Architecture/Decisions/ADR-001-schema-first-versioned-migrations.md).

## Implementation status

The current runner applies forward-only, flat `.sql` files and records their checksums. It does **not yet** implement paired upgrade and downgrade scripts, target versions, planning, advisory locking, downgrade safety classifications, or release manifests.

Do not create production migrations until the runner is upgraded to the contract below.

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

The runner will accept an explicit target version for one service database. It applies `up.sql` files in ascending order when upgrading and `down.sql` files in descending order when downgrading.

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
