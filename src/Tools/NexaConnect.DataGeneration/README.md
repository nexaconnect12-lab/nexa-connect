# NexaConnect Data Generation

This .NET console tool loads deterministic, repeatable sample data into every NexaConnect service-owned PostgreSQL database. Every table in the baseline migrations has fictional sample rows.

## Seed ownership

Seed scripts are stored under `Seeds/<Service>/` for PlatformDirectory, Restaurant, Catalog, Customer, Order, Inventory, Kitchen, Payment, POS, Media, and Reporting.

```text
Seeds/
`-- Catalog/
    |-- 0001_reference_data.sql
    |-- 0002_sample_menu.sql
    `-- 0003_operational_catalog.sql
```

Names must follow `<four-digit-sequence>_<name>.sql`, starting at `0001` without gaps. Every script declares the minimum database schema version in its first comments:

```sql
-- requires-schema-version: 1
```

Scripts must be safe to run repeatedly. Use stable identifiers and PostgreSQL `INSERT ... ON CONFLICT` statements. Never put production secrets or real customer information in seed files.

## Usage

```powershell
dotnet run --project src/Tools/NexaConnect.DataGeneration -- --all --plan
dotnet run --project src/Tools/NexaConnect.DataGeneration -- --all --confirm --environment-file .env
```

`--plan` (or `--dry-run`) validates and displays the ordered scripts without connecting to PostgreSQL. `--confirm` processes databases in dependency order, acquires a service-specific advisory lock, verifies migration history and the required schema version, and executes each script in its own transaction. Use `--service <name>` instead of `--all` to target one database.

Connection strings come from `NEXACONNECT_<SERVICE>_DB`. Existing process variables take precedence over `--environment-file` values. Use `--seeds-root <path>` to load another root.

The runner refuses to execute when `NEXACONNECT_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, or `ASPNETCORE_ENVIRONMENT` is `Production`.

The repository helper plans by default and mutates only with `-Confirm`:

```powershell
./scripts/generate-sample-data.ps1
./scripts/generate-sample-data.ps1 -Confirm
```

In Visual Studio, the default `All Data Generate` launch profile runs the confirmed all-database workflow using the repository `.env` file.
