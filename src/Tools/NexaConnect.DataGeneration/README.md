# NexaConnect Data Generation

This .NET console tool loads repeatable sample data into one service-owned PostgreSQL database at a time.

## Seed ownership

Store seed scripts under `Seeds/<Service>/`:

```text
Seeds/
└── Catalog/
    ├── 0001_reference_data.sql
    └── 0002_sample_products.sql
```

Seed scripts must be safe to run repeatedly. Prefer stable identifiers and PostgreSQL `INSERT ... ON CONFLICT` statements. Never put production secrets or real customer information in seed files.

## Usage

```powershell
$env:NEXACONNECT_CATALOG_DB = 'Host=localhost;Database=NexaConnect_Catalog;Username=...;Password=...'
dotnet run --project src/Tools/NexaConnect.DataGeneration -- --service Catalog --dry-run
dotnet run --project src/Tools/NexaConnect.DataGeneration -- --service Catalog --confirm
```

The explicit `--confirm` flag protects against accidental database changes. Use `--seeds-root <path>` to load seeds from another root directory.
