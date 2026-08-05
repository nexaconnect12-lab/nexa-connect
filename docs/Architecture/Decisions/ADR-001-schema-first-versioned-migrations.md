# ADR-001: Schema-First Versioned PostgreSQL Migrations

- **Status:** Accepted
- **Date:** 2026-08-04
- **Scope:** All NexaConnect service-owned PostgreSQL databases

## Context

NexaConnect requires deliberate database constraints for restaurant orders, kitchen execution, payments, offline synchronization, and reporting. Schema changes must support controlled deployment, application rollback, database upgrade, and database downgrade without allowing service replicas to mutate production schemas independently.

The initial `NexaConnect.DataMigration` runner supports only forward execution of flat SQL files. That implementation is insufficient for the agreed release and recovery model.

## Decision

NexaConnect will use schema-first development.

Versioned PostgreSQL migration scripts are the source of truth for each service database. Application persistence models are mapped or generated after the schema is applied and validated.

Each service owns an independent, linear migration sequence. Every released migration directory contains:

- `migration.json` with version, name, transaction behavior, downgrade safety, and compatibility metadata.
- `up.sql` to move from the preceding version to this version.
- `down.sql` to move from this version to the preceding supported version.

The migration runner accepts an explicit target version. It upgrades in ascending order and downgrades in descending order. It records immutable checksums and execution history inside the owning database.

Every released version requires tested clean-install, upgrade, and downgrade paths. Downgrades are classified as safe, transformative, destructive, or unsupported. A production release cannot contain an unsupported downgrade without a separately accepted recovery decision.

Application releases will declare their required schema versions per service. Expand-and-contract migrations are preferred so the preceding application version remains temporarily compatible with the new schema and can be rolled back without immediately performing a physical database downgrade.

## Safety constraints

- Only one migration process may change a service database at a time; the runner acquires a PostgreSQL advisory lock.
- Applied migration files are immutable and checksum-verified.
- Mutation requires an execution plan and explicit confirmation.
- Destructive downgrade requires additional authorization and a verified backup.
- Transactions are the default. Non-transactional scripts require explicit metadata and a documented recovery procedure.
- Production services do not automatically run schema migrations during replica startup.
- Connection strings and credentials never appear in migration logs or command arguments.
- External effects are not assumed to be reversible through SQL downgrade scripts.

## Consequences

### Positive

- PostgreSQL constraints and indexes are designed explicitly.
- Database state can be moved to a known service-specific target version.
- Upgrade and downgrade behavior becomes reviewable and testable.
- Application releases can declare exact schema compatibility.
- Migration execution is independent from service replica startup.

### Costs and risks

- Every migration requires metadata and two reviewed scripts.
- Downgrade testing increases delivery effort.
- Some data transformations cannot be reversed without loss.
- Events, payment-provider actions, object changes, and other external effects require separate compensation.
- Generated persistence models must be isolated so regeneration does not overwrite application logic.

## Implementation status

Implemented by the migration runner. The version-1 catalog still requires live PostgreSQL clean-install, downgrade, and re-upgrade validation before production release.
