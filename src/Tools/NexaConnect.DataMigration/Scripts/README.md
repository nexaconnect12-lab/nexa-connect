# Migration scripts

Exact historical-day attribution is implemented in Order 16 / Payment 15 / POS 13 (compatibility 0.29.0). Source-owned before/after selection distinguishes creation-time sales, Paid-time tenders, refund completion and drawer closure/review. Unrelated historical changes are excluded; older unresolved work, missing history, legacy descriptors and ownership uncertainty block readiness. Managers see bounded record identities, reasons, statuses, versions and effective times beside the preserved baseline/current comparison. Approval/finalization remain planned. See [attribution and comparison policy](../../../../docs/Architecture/Decisions/ADR-026-exact-historical-day-attribution.md).

Current exact-attribution migrations replace capture functions with version-two descriptors and require compatibility 0.29.0; no tables are added. Version-two history rejects downgrade, while empty/legacy-only downgrade restores version-one capture without rewriting epochs or history. Parent row locks require existing owning SELECT/UPDATE privileges. Existing immutable financial history guards remain intact.

The preceding Order 14 / Payment 13 / POS 11 migrations add day seals, append-only branch change journals and (in POS) separate durable coordination. Compatibility is 0.27.0. Existing revision epochs survive empty new-migration downgrade/reapply; retained history requires forward recovery. See [ADR-024](../../../../docs/Architecture/Decisions/ADR-024-source-day-seals-and-late-change-journals.md).

Order 13 / Payment 12 / POS 10 add source financial epochs, transactionally advanced branch revisions and guarded downgrade after protocol-two manifests. Compatibility is 0.26.0. Counts below are distinct CREATE TABLE names in the current owning up-script catalog. See [ADR-023](../../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md).

Create one directory per owning service and one subdirectory per sequential migration version. Each migration contains `migration.json`, `up.sql`, and `down.sql`.

## Current schema catalog

| Service | Version | Owned tables |
| --- | ---: | ---: |
| PlatformDirectory | 3 | 8 |
| Authorization | 10 | 7 |
| Restaurant | 3 | 8 |
| Catalog | 4 | 22 |
| Inventory | 5 | 11 |
| Order | 16 | 21 |
| Kitchen | 3 | 8 |
| Customer | 2 | 6 |
| Payment | 15 | 14 |
| Notification | 3 | 5 |
| POS | 13 | 28 |
| Media | 4 | 6 |
| Reporting | 20 | 15 |

The current 13-service catalog contains 159 tables. Counts include service-owned technical tables such as outboxes, inboxes, idempotency records, audit history, and projection checkpoints. Index counts should be generated from the packaged migration catalog when needed rather than maintained manually here.

Versions must be linear, sortable, immutable, independently owned by one service, and transactional. Non-transactional migrations are not supported because schema mutation and migration-history recording must remain atomic.

Every release migration requires tested clean-install, upgrade, and downgrade paths. Downgrades that transform or discard data must be classified and protected by explicit operational approval and backup requirements.

Version 1 is a reviewed baseline, not a production release. The executable runner supports this directory format, but the scripts still require live PostgreSQL clean-install, downgrade, and re-upgrade verification before release.

Never add a cross-database foreign key. Columns that identify another service's entity must be documented as external identifiers and populated through APIs or versioned events.

Do not store passwords, production data, or connection strings in this directory.
