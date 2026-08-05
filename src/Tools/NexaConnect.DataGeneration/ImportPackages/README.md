# CSV import packages

Each service-named child directory is a self-contained UTF-8 CSV package for one database. The 11 complete packages cover all 83 baseline tables with at least 50 rows per table. See the parent [README](../README.md#csv-import-packages) for the manifest and CSV contracts.

`CatalogSample` is deterministic fictional data and contains 50 product records plus their reference and product-category relationship rows. Validate it without a database:

```powershell
dotnet run --project src/Tools/NexaConnect.DataGeneration -- `
  --service Catalog `
  --import-package src/Tools/NexaConnect.DataGeneration/ImportPackages/CatalogSample `
  --plan
```

Do not place raw production exports, credentials, personal information, or unreviewed source-system files in this directory.
