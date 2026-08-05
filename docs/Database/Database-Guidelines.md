# Database Guidelines

See [Database Design](Database-Design.md) for the PostgreSQL topology, service-owned logical models, media metadata, and operational workflows.

- Each service owns its database or schema.
- Do not query or update another service's tables directly.
- Share cross-product organization data through an owning Platform Directory API and events, never through shared physical tables.
- Reference Keycloak identities by stable subject identifiers; never query Keycloak tables.
- Standard technical tables may share templates, but every service owns its own physical migration, outbox, inbox, idempotency, and audit records.
- Keep migrations with the owning service.
- Treat versioned PostgreSQL migration scripts as the schema source of truth.
- Treat documentation tables as summaries; migration SQL is authoritative for physical names, constraints, and indexes.
- Provide paired, tested upgrade and downgrade scripts for every released schema version.
- Classify downgrade paths as safe, transformative, destructive, or unsupported.
- Prefer expand-and-contract changes so an application can roll back without immediately downgrading its database.
- Never modify an applied migration; create a new version.
- Use optimistic concurrency where appropriate.
- Use the outbox pattern for reliable event publication.
- Store timestamps in UTC.
- Do not bypass the owning API by distributing another service's database credentials.
- Execute versioned directories through the migration runner; never flatten, concatenate, or manually reorder them.
- Validate clean install, downgrade, and re-upgrade against the supported PostgreSQL version before release.
