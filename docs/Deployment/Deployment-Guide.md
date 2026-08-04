# Deployment Guide

## Local infrastructure

1. Copy `.env.example` to `.env` and replace every placeholder password.
2. Start infrastructure with `docker compose up -d`.
3. Wait for the `postgres` and `keycloak-db` health checks.
4. Apply each service's versioned schema migration with the dedicated migration connection string after the migration runner supports the accepted versioned-directory contract.

The application PostgreSQL container listens on port `5432`. Keycloak uses a separate PostgreSQL container and database because Keycloak owns its schema and lifecycle.

## PostgreSQL provisioning

The local PostgreSQL initializer creates these databases on the first start of an empty `postgres-data` volume:

```text
PlatformDirectory
NexaConnect_Restaurant
NexaConnect_Catalog
NexaConnect_Inventory
NexaConnect_Order
NexaConnect_Kitchen
NexaConnect_Customer
NexaConnect_Payment
NexaConnect_POS
NexaConnect_Media
NexaConnect_Reporting
```

`nexaconnect_migration` owns every local development database and performs DDL. Each database has a separate runtime login with DML permissions but no database-creation, role-management, or schema-creation privileges. Other projects must use the owning service's API or integration events and must never receive these database credentials.

The initializer creates databases and roles only; the versioned service migrations create tables, constraints, indexes, and migration history.

## Initialization lifecycle

PostgreSQL image initialization scripts execute only when the data directory is empty. Editing `.env` or an initialization script does not alter an existing cluster.

To change passwords in an existing environment, use an approved credential-rotation procedure. Recreating the `postgres-data` volume destroys all local application database data and should be done only when a clean environment is explicitly intended.

## Production requirements

- Provision databases and roles through the selected infrastructure-as-code and secret-management system.
- Do not expose the PostgreSQL port publicly.
- Use TLS, encrypted backups, tested restoration, and environment-specific recovery objectives.
- Give the migration process DDL access only during controlled deployments.
- Give each runtime only its own database credentials.
- Generate and approve a migration plan before mutation.
- Back up before destructive or transformative downgrade operations.
- Record schema versions and checksums in the release manifest.

The local Docker initializer is a development convenience, not the production provisioning mechanism.
