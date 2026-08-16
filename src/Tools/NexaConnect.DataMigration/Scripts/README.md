# Migration scripts

Create one directory per owning service and one subdirectory per sequential migration version. Each migration contains `migration.json`, `up.sql`, and `down.sql`.

## Current schema catalog

| Service | Version | Owned tables |
| --- | ---: | ---: |
| PlatformDirectory | 3 | 8 |
| Authorization | 3 | 7 |
| Restaurant | 3 | 8 |
| Catalog | 4 | 22 |
| Inventory | 5 | 11 |
| Order | 1 | 9 |
| Kitchen | 3 | 8 |
| Customer | 1 | 5 |
| Payment | 2 | 6 |
| Notification | 2 | 4 |
| POS | 3 | 8 |
| Media | 4 | 6 |
| Reporting | 5 | 9 |

The current 13-service catalog contains 111 tables and 127 explicit indexes. Counts include service-owned technical tables such as outboxes, inboxes, idempotency records, audit history, and projection checkpoints.

Versions must be linear, sortable, immutable, independently owned by one service, and transactional. Non-transactional migrations are not supported because schema mutation and migration-history recording must remain atomic.

Every release migration requires tested clean-install, upgrade, and downgrade paths. Downgrades that transform or discard data must be classified and protected by explicit operational approval and backup requirements.

Version 1 is a reviewed baseline, not a production release. The executable runner supports this directory format, but the scripts still require live PostgreSQL clean-install, downgrade, and re-upgrade verification before release.

Never add a cross-database foreign key. Columns that identify another service's entity must be documented as external identifiers and populated through APIs or versioned events.

Do not store passwords, production data, or connection strings in this directory.
