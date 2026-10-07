# NexaConnect Data Migration

Order 14 / Payment 13 / POS 11 add service-owned immutable day seals and append-only source change journals. POS also owns separate seal coordinator/operation/audit tables. Minimum/current launcher compatibility is 0.27.0. Downgrade refuses retained seal/change/coordination history; empty new-migration downgrade/reapply preserves the prior revision epoch. Runtime grants must cover journal insertion in financial transactions and source seal retention without table/trigger ownership. See [rollout](../../../docs/Deployment/Day-Close-Seals.md).

Order `0013_financial_revisions`, Payment `0012_financial_revisions` and POS `0010_financial_revisions` add independently owned epoch/revision tables and transactional source-mutation triggers. Each requires compatibility 0.26.0; the wrapper currently defaults to 0.27.0. Downgrade refuses after any protocol-two source manifest; empty-history downgrade/reapply creates a new epoch. Persistent runtime grants must cover revision SELECT/INSERT/UPDATE and epoch/parent SELECT without schema ownership or trigger-disabling privileges. Reporting 20 and Authorization 10 remain unchanged. See [ADR-023](../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md) and [coordinated rollout and verification](../../../docs/Deployment/Day-Close-Cutoffs.md).

Order 12, Payment 11 and POS 9 add immutable source day-cutoff manifests; POS additionally retains separate cutoff coordination/operation/audit tables. Minimum compatibility is 0.25.0 and `scripts/migrate-databases.ps1` defaults to 0.27.0. Retained histories reject destructive downgrade; empty POS downgrade/reapply and source guards have disposable PostgreSQL coverage. Reporting stays at 20 and Authorization at 10. See [cutoff rollout](../../../docs/Deployment/Day-Close-Cutoffs.md).

POS 8 (`0008_day_close_preparations`) and Authorization 10 (`0010_day_close_permissions`) require compatibility 0.24.0; the current migration launcher default is 0.27.0. POS owns scoped preparation state/operation/immutable audit tables and refuses `8→7` after any retained row. Authorization backfills manager/admin read+prepare and accountant read permissions. Empty downgrade/reapply and retained-history refusal are covered by the disposable PostgreSQL runner. See [rollout and least privilege](../../../docs/Deployment/Day-Close-Preparation.md).

Payment 10 / Reporting 20 require application 0.23.0; the current migration launcher default is 0.27.0. They add immutable original refund publication/replay audits and scoped financial completeness observations; both refuse downgrade after history. That release used Order 11; current cutoff deployment uses the versions above. Apply before deploying the corresponding binaries. See [rollout and migration acceptance](../../../docs/Deployment/Financial-Reporting-Recovery.md).

Order 11 / Reporting 19 require application 0.22.0 and add receipt-backed sale publication/replay audit and atomic sale/payment dedupe support. Both refuse downgrade after financial evidence. `scripts/migrate-databases.ps1` now defaults to compatibility 0.27.0. Establish the Reporting sale queue before Order publication. See [rollout](../../../docs/API/Sale-Financial-Reporting.md).

Reporting `0018_payment_refund_facts` requires application `0.21.0` and creates refund financial facts and hash receipts. Its destructive `18→17` downgrade removes both tables and only the refund-financial checkpoint. Stop consumption/affected reads first and retain Payment source events for replay after re-upgrade. `scripts/migrate-databases.ps1` defaults to application compatibility `0.27.0`; this value does not choose a schema target. See [refund reporting rollout](../../../docs/API/Refund-Financial-Reporting.md).

Order `0010_pre_payment_cancellation` adds immutable cancellation identity, compensation/recovery fencing, and explicit Order states; its downgrade refuses once history exists. Authorization `0008_order_cancellation_permission` grants `order.cancel` to existing tenant-admin, store-manager, and cashier roles. Reporting `0016_order_cancellation_vocabulary` accepts the three safe audit actions and removes incompatible facts/inbox receipts on destructive rollback. Deploy all three before exposing cancellation; use forward recovery for recorded Order history.

Order migration 0008_authoritative_pricing requires application 0.17.0. It adds pricing JSON/fingerprint/placement identity, amount constraints and an immutable-history trigger. Downgrade refuses after any priced order; use forward recovery. The dedicated test-order-pricing runner verifies SQL guards and the real migration runner. See [pricing contract and rollout](../../../docs/API/Order-Pricing.md).

POS `0006_cash_close_publication` adds a session publication checkpoint and closed-session index; Reporting `0015_cash_close_projection` adds scoped facts and hashed event receipts. Both require application version `0.15.0`. POS downgrade refuses once checkpoints or cash-close events exist. Reporting downgrade deletes facts/receipts and requires retained source-event replay after re-upgrade; the [replay CLI](../../../docs/Deployment/Cash-Close-Recovery.md) now supplies this operation with POS migration-7 append-only attribution. The opt-in repository/SQL lifecycle test is separate from the existing POS-5 full-runner acceptance. See [cash-close rollout and verification](../../../docs/API/Cash-Close-Reporting.md).

Payment migration `0008_omise_webhook_inbox` adds the optional test-account webhook identifier inbox, fenced claims/retry scheduling and due-work index. Its transactional metadata requires application version `0.12.0`; apply before enabling webhooks. Destructive `8→7` refuses pending, processing or exhausted evidence and may remove only resolved notifications after disabling ingress/worker. Controlled PostgreSQL tests use generated isolated schemas and cover deduplication, stale fences, expiration, exhaustion/downgrade refusal and financial/outbox commit before inbox acknowledgement. They do not certify external Omise delivery. See [webhook setup and failure modes](../../../docs/Deployment/Omise-Webhooks.md).

This .NET console project contains the schema-first migration runner and the service-owned PostgreSQL migration catalog. Versioned SQL scripts are the source of truth for database structure; application persistence models are mapped or generated only after the target schema is validated.

The agreed migration contract is defined by [ADR-001](../../../docs/Architecture/Decisions/ADR-001-schema-first-versioned-migrations.md).

## Implementation status

Schema-first catalogs have been authored for 13 independently owned databases. Platform Directory version 2 adds support-elevation state and database-enforced append-only audit history. Inventory version 5 adds durable simplified-table reservation identity and its append-only product-audit table/trigger while preserving the outbox owned by version 1. Its full seven-test acceptance passed against local PostgreSQL 17 and RabbitMQ. The migration case invokes the actual runner for 0→5→4→5 and validates history checksums, schema ownership, downgrade preservation, and repository writes before and after re-upgrade. Other services retain their independently versioned catalogs.

Payment version 2 adds organization ownership and append-only product audit while preserving the outbox owned by version 1. Payment version 3 adds authorization state, version 4 adds recoverable authorization leases, version 5 adds capture state and sanitized provider capture references, and version 6 adds capture recovery leases/attempts/timestamps. Order version 2 adds the durable reconciliation inbox and `payment_pending` status; Order version 6 adds exact pre-payment workflow stages, original workflow context, and fenced recovery leases; Order version 7 expands the recovery index through provider payment without moving Payment-owned intent state into Order. Reporting versions 8-11 accept the corresponding authorization, capture, and reconciliation audit vocabulary. Payment runner acceptance targets `0→6→5→6`; Order targets `0→7→6→7` and additionally proves recoverable work prevents the destructive migration-6 downgrade. Updated live recovery cases require fresh opt-in release evidence.

Reporting version 4 expands the activity projection's database-enforced action/resource vocabulary for approved Catalog, Media, Notification, and Payment audit contracts. Live PostgreSQL coverage confirms Payment projection and destructive rollback removal of incompatible projection rows.

Kitchen version 3 adds tenant ownership, station snapshot fingerprints, multi-station uniqueness, and append-only audit/history protection while preserving migration-1 outbox and migration-2 inbox ownership. Reporting version 5 adds Kitchen audit vocabulary and replay-safe destructive rollback. Their coordinated live lifecycle passed locally.

Authorization version 3 backfills `kitchen.ticket.read` and `kitchen.ticket.transition` for existing `tenant-admin` and `store-manager` role assignments. Its destructive downgrade removes only those Kitchen permission rows.

Authorization version 4 backfills Payment Review read/resolve permissions for existing `tenant-admin` and `store-manager` roles. Version 5 adds read-only Payment Review access to existing `accountant` roles so upgraded databases match runtime-created role mappings. Neither adds tables or indexes. The destructive `5→4` downgrade removes only accountant/read associations; `4→3` removes the tenant-admin/store-manager Payment Review associations. Disable affected routes and verify assignments before downgrade.

Customer version 2 adds append-only audit that excludes profile fields while preserving the outbox owned by version 1. Reporting version 6 accepts `customer.audit.v1` profile-created vocabulary and removes incompatible projections/inbox markers on destructive downgrade so retained source events can replay after re-upgrade. Six coordinated Customer acceptances passed locally against PostgreSQL 17 and RabbitMQ, including concurrent replay, atomic rollback, confirmed publication, Reporting replay, and the actual 0→2→1→2 runner.

POS versions 2 and 3 add shift open/close authorization decision ownership. Version 4 adds the immutable, Order-unique manual-tender projection. Version 5 adds current cash-review state and append-only supervisor decision history; its downgrade refuses once reviews exist. Clean-install acceptance invokes the actual runner for 0→5→4→3→5, validates checksums/history and version-owned objects, proves migration-1 cash/replay data survives downgrade, exercises real shift/cash/settlement/review repositories, and verifies the post-review downgrade guard.

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

The baseline is still not approved for production execution until every service passes live clean-install, downgrade, and re-upgrade tests against PostgreSQL 17. Catalog has opt-in 0→4→3→4 runner acceptance; Inventory has successful local 0→5→4→5 evidence; Customer has successful local 0→2→1→2 evidence; POS has successful local 0→5→4→3→5 evidence; and Payment migration 6 requires fresh 0→6→5→6 acceptance. Earlier Payment evidence does not establish capture recovery. These cases validate history checksums and representative repository writes and require a disposable-database administrator. Catalog's configured administrator password remains stale. The normal migration owner correctly lacks `CREATEDB`; successful cases use disposable administrator capability and remove their generated databases. The remaining service catalogs still require recorded release evidence. Do not flatten or manually reorder the scripts.

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
dotnet restore NexaConnect.sln
.\scripts\migrate-databases.ps1
```

After reviewing all plans, execute them with explicit confirmation:

```powershell
.\scripts\migrate-databases.ps1 -Confirm
```

The wrapper invokes each service with `dotnet run --no-restore`, so restore the solution before the first run and after dependency changes. Its `-ApplicationVersion` default is `0.27.0`, the highest minimum application version required by the current checked-in catalog. This value is the application/schema compatibility gate passed to every service migration; it does not select the schema target, which remains the latest migration discovered for each service. Override it only when deliberately validating an older application release boundary, for example `-ApplicationVersion 0.9.0`; migrations requiring a newer application version will then fail safely.

The wrapper stops on the first failed service. Each service retains its own history, transaction boundary, connection string, and advisory lock; the wrapper does not create a cross-database transaction.

## Database provisioning

Database and role creation is deliberately separate from service schema migration. For local development, Docker Compose mounts [`docker/postgres/init/001_create_nexaconnect_databases.sh`](../../../docker/postgres/init/001_create_nexaconnect_databases.sh) into PostgreSQL's first-start initialization directory.

On an empty PostgreSQL volume, the initializer creates all 13 catalog databases, the `nexaconnect_migration` DDL owner, and one restricted runtime login per database. It also configures default table and sequence privileges for objects later created by the migration owner. The runner removes runtime-role migration-history privileges. Payment alone receives SELECT on the version column for readiness; checksum reads and all history mutation remain denied. Already-current databases need an explicit reviewed column grant because a no-step invocation does not reconcile privileges. See the [hosted refund rollout correction](../../../docs/Deployment/Hosted-Refund-Acceptance.md#existing-payment-deployment-correction).

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

POS migration `0007_cash_close_replay_audit` requires application `0.16.0`, adds the outbox replay-scope index and immutable run/attempt tables, and refuses `7→6` after any replay run. It records the selection limit, manifest and database `session_user` alongside asserted operator identity. The current disposable recovery test applies SQL catalogs directly; the earlier POS-5 full migration-runner evidence does not certify this migration. See [runbook](../../../docs/Deployment/Cash-Close-Recovery.md).
Order migration 0009_paid_order_receipts requires application 0.18.0. It adds an immutable receipt snapshot bound to the paid Order's ownership, currency and total. Existing Paid rows remain without receipts; downgrade refuses after any receipt exists. The pricing runner now verifies receipt concurrency, rollback and migration guards. See [receipt contract](../../../docs/API/Paid-Order-Receipts.md).
