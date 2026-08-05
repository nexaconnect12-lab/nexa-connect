# Migration scripts

Create one directory per owning service and one subdirectory per sequential migration version. Each migration contains `migration.json`, `up.sql`, and `down.sql`.

## Initial schema catalog

| Service | Version | Owned tables |
| --- | ---: | ---: |
| PlatformDirectory | 1 | 5 |
| Restaurant | 1 | 7 |
| Catalog | 1 | 20 |
| Inventory | 1 | 7 |
| Order | 1 | 9 |
| Kitchen | 1 | 6 |
| Customer | 1 | 5 |
| Payment | 1 | 5 |
| POS | 1 | 8 |
| Media | 1 | 4 |
| Reporting | 1 | 7 |

The initial catalog contains 83 tables. Counts include service-owned technical tables such as outboxes, processed-message stores, idempotency records, and projection checkpoints.

Versions must be linear, sortable, immutable, and independently owned by one service. Scripts should normally be safe to execute inside a PostgreSQL transaction. A non-transactional migration must declare that requirement in its metadata and document its recovery procedure.

Every release migration requires tested clean-install, upgrade, and downgrade paths. Downgrades that transform or discard data must be classified and protected by explicit operational approval and backup requirements.

Version 1 is a reviewed baseline, not a production release. The executable runner supports this directory format, but the scripts still require live PostgreSQL clean-install, downgrade, and re-upgrade verification before release.

Never add a cross-database foreign key. Columns that identify another service's entity must be documented as external identifiers and populated through APIs or versioned events.

Do not store passwords, production data, or connection strings in this directory.
