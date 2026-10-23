# Database Guidelines

QR1 uses Restaurant-owned parameterized Infrastructure persistence, branch-row serialized admission/version transitions, hashed capabilities and same-transaction immutable administration audit/outbox. Restaurant migration 4 brings the current catalog to 204 tables (Restaurant 11); no financial migration changes. Runtime roles need only the documented owning privileges; restored entry hashes require session cleanup and rotation before reopening guest traffic. See [QR persistence and rollback](QR-Entry.md).

Slice 6 adds no schema or migration. Its [administrative local restore drill](../Deployment/First-Draft-Release.md) stops all owned writers, restores ten service databases and Keycloak into isolated clones, and checks successful original-archive DDL restoration and separately compares exact data/sequence dumps and owner/permission statements without bypassing financial history guards in original stores. Role backups and configuration copies are private; service ownership is unchanged. Broker/Redis and restored-graph recovery remain separate.

Slice 4 adds no migration. POS terminal-admission queries are service-owned parameterized Infrastructure reads; Order never reads POS tables. Admission observation is outside the Order transaction and is not an atomic cross-service lock. Windows install preflight opens the existing schema-2 local database read-only, verifies protected scope/pending checkout and rejects legacy/schema conflicts; it does not migrate, purge or copy recovery. See [terminal deployment](../Deployment/First-Draft-Windows-POS.md).

Draft reference provisioning adds no schema. POS and Authorization own narrowly named draft-only Infrastructure adapters for reference store/terminal and financial-limit rows; every data value is parameterized and existing rows are preserved. Other reference operations reuse owning repositories. See [setup and transaction/replay boundaries](../Deployment/First-Draft-Provisioning.md).

The [local first-draft stack](../Deployment/First-Draft-Test-Stack.md) derives service schema targets from migration metadata and previews/applies only to generated owning databases. It reuses the restricted cashier initialization: each runtime role has only its owning database access and no schema creation; history rights are reduced after migration to version-column SELECT. The lifecycle verifies these grants independently of HTTP liveness and preserves immutable-history downgrade rules. It does not migrate existing application databases or replace production credential/backup controls.

Settlement original-event replay is now provided by [ADR-035](../Architecture/Decisions/ADR-035-audited-settlement-replay.md) and [the administrative runbook](../Deployment/Settlement-Replay.md). POS 23 adds two immutable audit tables; Reporting stays 22 and compatibility is 0.38.0 (201-table pre-QR1 baseline, POS 52 / Reporting 19). Customer routes/permissions remain read-only; unknown original publication fields fail closed.

Reporting 22 adds two owned immutable settlement projection/hash tables; pre-QR1 catalog 201 tables (Reporting 19/POS 52). Settlement reporting reuses reporting.sales.read and pos.day-close.read with customer membership/scope revalidation; it adds no grant. Current cutoff acceptance uses Reporting 22/POS 23 and compatibility 0.38.0 with both settlement event bindings. New POS publication is V2; original V1 history stays unchanged. See [settlement reporting](../API/Settlement-Reporting.md) and [operations](../Deployment/Settlement-Reporting.md).

Correction-aware POS manifests retain original events, source summary and revision in one repeatable-read snapshot; matching protocol-three provenance is required for new readiness. Retained epochs/history cannot be reset or silently upgraded. Source and Reporting comparisons remain separate snapshots; existing temporary/permanent financial guards protect later transactions. See [contract](../API/Correction-Day-Evidence.md) and [operations](../Deployment/Correction-Day-Evidence.md).

Correction Reporting projections may be rebuilt only after stopping consumption/reads, retaining original POS event evidence and replaying after re-upgrade. POS correction ledger and retained replay audit require forward recovery; shared-exchange confirmation does not certify Reporting completeness. See [contract](../API/Cash-Correction-Reporting.md) and [operations](../Deployment/Cash-Correction-Reporting.md).

Cash correction ledger/audit is immutable and must be retained with original custody/review/barrier/drawer provenance and source journal. POS 20 downgrade refuses financial history; runtime roles receive only owning SELECT/INSERT. Source amount and approval limit verification belongs to Application/Domain; parameterized transactions and owning SQL constraints backstop current review, unique posting and financial window. See [correction contract](../API/Late-Cash-Corrections.md) and [operations](../Deployment/Late-Cash-Corrections.md).

Late-work review uses source-owned append-only history with SELECT/INSERT runtime access, scoped committed custody membership, serialized version advancement and exact operation replay. Retain opaque custody identities together with their links/barriers/reviews; source downgrade refuses any custody or review row. Reviews do not mutate money, source revisions, message disposition or settlement receipts. See [review storage and rollback](../Deployment/Late-Work-Reviews.md).

Permanent financial-day barriers and settlement history require forward recovery after any retained intent. Runtime roles own only their service tables and narrow INSERT/UPDATE privileges; no trigger disabling, table ownership, cross-database grants or history deletion is allowed. Late financial payload custody stays private to its owner. See [settlement storage and recovery](../Deployment/Day-Close-Settlements.md).

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
