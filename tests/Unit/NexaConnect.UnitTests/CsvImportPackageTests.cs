using System.Text.Json;

namespace NexaConnect.UnitTests;

public sealed class CsvImportPackageTests
{
    [Fact]
    public async Task Repository_sample_package_contains_fifty_products()
    {
        string packageRoot = Path.Combine(
            AppContext.BaseDirectory,
            "ImportPackages",
            "CatalogSample");

        CsvImportPackage package = await CsvImportPackage.LoadAsync(
            packageRoot,
            CancellationToken.None);

        Assert.Equal("Catalog", package.Service);
        Assert.Equal(106, package.Tables.Sum(table => table.RowCount));
        Assert.Equal(
            50,
            Assert.Single(package.Tables, table => table.Table == "products").RowCount);
    }

    [Fact]
    public async Task Repository_packages_cover_every_table_with_at_least_fifty_rows()
    {
        string packageRoot = Path.Combine(AppContext.BaseDirectory, "ImportPackages");

        foreach (string service in DataGenerationApplication.ServiceOrder)
        {
            CsvImportPackage package = await CsvImportPackage.LoadAsync(
                Path.Combine(packageRoot, service),
                CancellationToken.None);
            string schemaPath = Path.Combine(
                AppContext.BaseDirectory,
                "Scripts",
                service,
                "0001_initial_schema",
                "up.sql");
            string schema = await File.ReadAllTextAsync(schemaPath);
            string[] schemaTables = System.Text.RegularExpressions.Regex.Matches(
                    schema,
                    @"(?im)^CREATE TABLE\s+(?<table>[a-z_]+)")
                .Select(match => match.Groups["table"].Value)
                .ToArray();

            Assert.Equal(service, package.Service);
            Assert.Equal(schemaTables.Length, package.Tables.Count);
            Assert.Empty(schemaTables.Except(
                package.Tables.Select(table => table.Table),
                StringComparer.Ordinal));
            Assert.All(package.Tables, table => Assert.True(
                table.RowCount >= 50,
                $"{service}/{table.Table} has only {table.RowCount} rows."));
        }
    }

    [Fact]
    public void Payment_sample_uses_only_supported_provider_transaction_types()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "ImportPackages",
            "Payment",
            "provider_transactions.csv");
        string[] allowed = ["authorize", "capture", "sale", "void", "refund"];
        string[] transactionTypes = File.ReadLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(',')[4])
            .ToArray();

        Assert.NotEmpty(transactionTypes);
        Assert.All(transactionTypes, value => Assert.Contains(value, allowed));
    }

    [Fact]
    public void Payment_sample_uses_unique_provider_transaction_references()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "ImportPackages",
            "Payment",
            "provider_transactions.csv");
        string[] providerReferences = File.ReadLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(',')[3])
            .ToArray();

        Assert.Equal(providerReferences.Length, providerReferences.Distinct().Count());
    }

    [Fact]
    public async Task Pos_sample_covers_current_shift_authorization_columns()
    {
        string packageRoot = Path.Combine(AppContext.BaseDirectory, "ImportPackages", "POS");
        CsvImportPackage package = await CsvImportPackage.LoadAsync(
            packageRoot,
            CancellationToken.None);
        CsvImportTable shifts = Assert.Single(package.Tables, table => table.Table == "shifts");

        Assert.Equal(3, package.RequiredSchemaVersion);
        Assert.Contains("authorization_decision_id", shifts.Columns);
        Assert.Contains("close_authorization_decision_id", shifts.Columns);
        int authorizationColumn = shifts.Columns.ToList().IndexOf("authorization_decision_id");
        IReadOnlyList<string[]> rows = CsvParser.Parse(
            await File.ReadAllTextAsync(shifts.Path),
            shifts.File);
        Assert.All(rows.Skip(1), row => Assert.False(
            string.IsNullOrWhiteSpace(row[authorizationColumn])));
    }

    [Fact]
    public async Task Package_accepts_quoted_commas_and_newlines()
    {
        using var fixture = new ImportPackageFixture();
        fixture.WriteCsv("items.csv", "id,name\r\n1,\"First, item\"\r\n2,\"Two\r\nlines\"\r\n");
        fixture.WriteManifest(2);

        CsvImportPackage package = await CsvImportPackage.LoadAsync(
            fixture.Root,
            CancellationToken.None);

        Assert.Equal(2, Assert.Single(package.Tables).RowCount);
    }

    [Fact]
    public async Task Package_rejects_duplicate_import_keys()
    {
        using var fixture = new ImportPackageFixture();
        fixture.WriteCsv("items.csv", "id,name\n1,First\n1,Duplicate\n");
        fixture.WriteManifest(2);

        DataGenerationException exception = await Assert.ThrowsAsync<DataGenerationException>(
            () => CsvImportPackage.LoadAsync(fixture.Root, CancellationToken.None));

        Assert.Contains("duplicates an import key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_rejects_operational_tables()
    {
        using var fixture = new ImportPackageFixture();
        fixture.WriteCsv("items.csv", "id,name\n1,First\n");
        fixture.WriteManifest(1, "nexaconnect_schema_migrations");

        DataGenerationException exception = await Assert.ThrowsAsync<DataGenerationException>(
            () => CsvImportPackage.LoadAsync(fixture.Root, CancellationToken.None));

        Assert.Contains("reserved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_rejects_quotes_inside_unquoted_fields()
    {
        using var fixture = new ImportPackageFixture();
        fixture.WriteCsv("items.csv", "id,name\n1,Bad\"value\n");
        fixture.WriteManifest(1);

        DataGenerationException exception = await Assert.ThrowsAsync<DataGenerationException>(
            () => CsvImportPackage.LoadAsync(fixture.Root, CancellationToken.None));

        Assert.Contains("quote inside an unquoted field", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_enforces_minimum_total_rows()
    {
        using var fixture = new ImportPackageFixture();
        fixture.WriteCsv("items.csv", "id,name\n1,Only row\n");
        fixture.WriteManifest(2);

        DataGenerationException exception = await Assert.ThrowsAsync<DataGenerationException>(
            () => CsvImportPackage.LoadAsync(fixture.Root, CancellationToken.None));

        Assert.Contains("minimumRows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_accept_all_services_for_an_import_package_root()
    {
        DataGenerationOptions options = Assert.IsType<DataGenerationOptions>(
            DataGenerationOptions.Parse(
                ["--all", "--import-package", ".", "--plan"]));

        Assert.True(options.AllServices);
        Assert.NotNull(options.ImportPackage);
    }

    private sealed class ImportPackageFixture : IDisposable
    {
        public ImportPackageFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"nexaconnect-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteCsv(string file, string content) =>
            File.WriteAllText(Path.Combine(Root, file), content);

        public void WriteManifest(int minimumRows, string table = "items")
        {
            var manifest = new
            {
                formatVersion = 1,
                service = "Catalog",
                requiredSchemaVersion = 1,
                minimumTotalRows = minimumRows,
                tables = new[]
                {
                    new
                    {
                        table,
                        file = "items.csv",
                        keyColumns = new[] { "id" },
                        minimumRows
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(Root, "manifest.json"),
                JsonSerializer.Serialize(manifest));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
