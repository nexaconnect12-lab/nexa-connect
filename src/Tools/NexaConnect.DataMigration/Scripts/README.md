# Migration scripts

Create one directory per owning service and one subdirectory per sequential migration version. Each migration contains `migration.json`, `up.sql`, and `down.sql`.

Versions must be linear, sortable, immutable, and independently owned by one service. Scripts should normally be safe to execute inside a PostgreSQL transaction. A non-transactional migration must declare that requirement in its metadata and document its recovery procedure.

Every release migration requires tested clean-install, upgrade, and downgrade paths. Downgrades that transform or discard data must be classified and protected by explicit operational approval and backup requirements.

Do not store passwords, production data, or connection strings in this directory.
