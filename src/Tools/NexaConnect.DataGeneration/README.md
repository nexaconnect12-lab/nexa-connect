# NexaConnect Data Generation

This .NET console tool imports deterministic, repeatable, manifest-driven UTF-8 CSV packages into service-owned PostgreSQL databases. Repository SQL sample inserts are no longer supported; this keeps sample-data loading on the restricted import path.

## Usage

```powershell
dotnet run --project src/Tools/NexaConnect.DataGeneration -- --all --import-package src/Tools/NexaConnect.DataGeneration/ImportPackages --plan
dotnet run --project src/Tools/NexaConnect.DataGeneration -- --all --import-package src/Tools/NexaConnect.DataGeneration/ImportPackages --confirm --environment-file .env
```

`--plan` (or `--dry-run`) validates and displays the ordered CSV packages without connecting to PostgreSQL. `--confirm` processes databases in dependency order, acquires a service-specific advisory lock, verifies migration history and the required schema version, and imports each package in one transaction. Use `--service <name>` instead of `--all` to target one database.

CSV import connection strings come from `NEXACONNECT_<SERVICE>_IMPORT_DB` and must use the owning service's restricted runtime role. Existing process variables take precedence over `--environment-file` values.

The runner executes only when `NEXACONNECT_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, or `ASPNETCORE_ENVIRONMENT` is explicitly `Development`, `Test`, or `Testing`. Missing, unknown, and production environment names are rejected before any database connection is opened.

The repository helper plans by default and mutates only with `-Confirm`:

```powershell
./scripts/generate-sample-data.ps1
./scripts/generate-sample-data.ps1 -Confirm
```

In Visual Studio, the default `All Data Generate` launch profile runs the confirmed all-database workflow using the repository `.env` file.

## CSV import packages

Use CSV packages for generated samples or data exported from another system. A package is one directory containing `manifest.json` and one destination-shaped CSV file per table. Manifest table order is import order, so place referenced tables before dependent tables.

```powershell
dotnet run --project src/Tools/NexaConnect.DataGeneration -- `
  --service Catalog `
  --import-package src/Tools/NexaConnect.DataGeneration/ImportPackages/CatalogSample `
  --plan

dotnet run --project src/Tools/NexaConnect.DataGeneration -- `
  --service Catalog `
  --import-package src/Tools/NexaConnect.DataGeneration/ImportPackages/CatalogSample `
  --confirm `
  --environment-file .env

dotnet run --project src/Tools/NexaConnect.DataGeneration -- `
  --all `
  --import-package src/Tools/NexaConnect.DataGeneration/ImportPackages `
  --plan
```

`--plan` validates the complete package without connecting to PostgreSQL. `--confirm` checks the destination schema version, creates typed temporary tables, uses PostgreSQL CSV loading, and upserts all declared tables in one transaction. Tables prefixed `nexaconnect_` are reserved operational tables and cannot be imported. Any invalid value, missing destination column, constraint failure, or import error rolls back the whole package.

The manifest contract is:

```json
{
  "formatVersion": 1,
  "service": "Catalog",
  "requiredSchemaVersion": 1,
  "minimumTotalRows": 50,
  "tables": [
    {
      "table": "products",
      "file": "products.csv",
      "keyColumns": ["id"],
      "minimumRows": 50
    }
  ]
}
```

CSV requirements:

- Encode files as UTF-8, with or without a byte-order mark.
- Use the destination table's lowercase `snake_case` column names as the header.
- Follow RFC 4180 quoting. Both LF and CRLF line endings are accepted.
- Use unquoted `\N` for SQL `NULL`; an empty field imports as an empty string.
- Use ISO 8601 UTC timestamps, `YYYY-MM-DD` dates, invariant decimals, and textual UUIDs.
- Include every conflict key in the header. Keys must be unique and non-null within each file.
- Select `keyColumns` backed by a primary-key or unique constraint in the destination schema.
- Include required destination columns without defaults. PostgreSQL validates types and constraints.
- Keep files fictional and free from production personal data when packages are committed here.

The repository includes one complete package for each of the 11 service databases. Together they cover all 83 baseline tables with 5,000 rows; every table has at least 50 deterministic records. The `CatalogSample` package remains as a smaller four-table example. Use the `--all` form with the `ImportPackages` root to validate or import every complete package in service dependency order.
