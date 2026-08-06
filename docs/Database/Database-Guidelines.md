# Database Guidelines

See [Database Design](Database-Design.md) for the PostgreSQL topology, service-owned logical models, media metadata, and operational workflows.

- Each service owns its database or schema.
- Do not query or update another service's tables directly.
- Share cross-product organization data through an owning Platform Directory API and events, never through shared physical tables.
- Reference Keycloak identities by stable subject identifiers; never query Keycloak tables.
- Standard technical tables may share templates, but every service owns its own physical migration, outbox, inbox, idempotency, and audit records.
- Keep migrations with the owning service.
- Treat versioned PostgreSQL migration scripts as the schema source of truth.
- Route runtime database access through the owning service's Infrastructure layer and a narrow persistence abstraction; controllers, API endpoints, Domain code, and Application use cases must not issue database commands directly.
- Keep raw SQL inside Infrastructure or migration tooling. Parameterize every runtime data value and never concatenate untrusted input. When tooling must compose an identifier, accept it only from validated, allow-listed metadata and quote it with the database provider. Use least-privilege runtime credentials and make transaction boundaries explicit.
- Test security-sensitive persistence behavior against PostgreSQL, including tenant and resource-scope isolation, authorization filters, concurrency behavior, and rollback on failure.
- Keep business authorization and tenant rules explicit in Domain or Application behavior. SQL constraints and filters provide defense in depth but must not be the only undocumented expression of a business decision.
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
