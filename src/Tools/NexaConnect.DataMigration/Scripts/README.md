# Migration scripts

Window-aware sealed reconciliation is implemented in Order 15 / Payment 14 / POS 12 (compatibility 0.28.0). Owning sources atomically retain safe before/after effective timestamps with journal revisions. Proven unrelated next-day trading no longer invalidates older seals; historical, ownership-uncertain and unattributed changes remain blocking. Managers compare current sales, tenders, refunds and cash variance with the preserved sealed baseline. Approval/finalization and exact historical-day attribution remain planned. See [attribution and comparison policy](../../../../docs/Architecture/Decisions/ADR-025-window-aware-sealed-change-reconciliation.md).

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
| Order | 15 | 21 |
| Kitchen | 3 | 8 |
| Customer | 2 | 6 |
| Payment | 14 | 14 |
| Notification | 3 | 5 |
| POS | 12 | 28 |
| Media | 4 | 6 |
| Reporting | 20 | 15 |

The current 13-service catalog contains 159 tables. Counts include service-owned technical tables such as outboxes, inboxes, idempotency records, audit history, and projection checkpoints. Index counts should be generated from the packaged migration catalog when needed rather than maintained manually here.

Versions must be linear, sortable, immutable, independently owned by one service, and transactional. Non-transactional migrations are not supported because schema mutation and migration-history recording must remain atomic.

Every release migration requires tested clean-install, upgrade, and downgrade paths. Downgrades that transform or discard data must be classified and protected by explicit operational approval and backup requirements.

Version 1 is a reviewed baseline, not a production release. The executable runner supports this directory format, but the scripts still require live PostgreSQL clean-install, downgrade, and re-upgrade verification before release.

Never add a cross-database foreign key. Columns that identify another service's entity must be documented as external identifiers and populated through APIs or versioned events.

Do not store passwords, production data, or connection strings in this directory.
