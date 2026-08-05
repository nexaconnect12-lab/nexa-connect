namespace NexaConnect.UnitTests;

public sealed class DataGenerationRunnerTests
{
    [Fact]
    public void Parse_requires_an_explicit_command()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DataGenerationOptions.Parse(["--service", "Catalog"]));
        Assert.Contains("--plan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_accepts_plan_alias_and_environment_file()
    {
        string environmentFile = Path.GetFullPath(".env");
        DataGenerationOptions options = Assert.IsType<DataGenerationOptions>(
            DataGenerationOptions.Parse(
            [
                "--service", "Catalog", "--dry-run",
                "--environment-file", ".env"
            ]));

        Assert.Equal(DataGenerationCommand.Plan, options.Command);
        Assert.Equal(environmentFile, options.EnvironmentFile);
    }

    [Fact]
    public void Parse_accepts_all_services()
    {
        DataGenerationOptions options = Assert.IsType<DataGenerationOptions>(
            DataGenerationOptions.Parse(["--all", "--confirm"]));

        Assert.True(options.AllServices);
        Assert.Null(options.Service);
        Assert.Equal(DataGenerationCommand.Confirm, options.Command);
    }

    [Fact]
    public void Parse_rejects_service_combined_with_all()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DataGenerationOptions.Parse(
                ["--service", "Catalog", "--all", "--plan"]));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_loads_ordered_repository_seeds()
    {
        string seedsRoot = Path.Combine(AppContext.BaseDirectory, "Seeds");
        SeedCatalog catalog = await SeedCatalog.LoadAsync(
            seedsRoot, "catalog", CancellationToken.None);

        Assert.Equal("Catalog", catalog.Service);
        Assert.Equal(3, catalog.Seeds.Count);
        Assert.Collection(
            catalog.Seeds,
            seed =>
            {
                Assert.Equal(1, seed.Sequence);
                Assert.Equal(1, seed.RequiredSchemaVersion);
            },
            seed => Assert.Equal(2, seed.Sequence),
            seed => Assert.Equal(3, seed.Sequence));
        Assert.All(catalog.Seeds, seed => Assert.Equal(64, seed.Checksum.Length));
    }

    [Fact]
    public async Task Repository_seeds_insert_into_every_owned_table()
    {
        string seedsRoot = Path.Combine(AppContext.BaseDirectory, "Seeds");

        foreach (string service in DataGenerationApplication.ServiceOrder)
        {
            string schemaPath = Path.Combine(
                AppContext.BaseDirectory,
                "Scripts",
                service,
                "0001_initial_schema",
                "up.sql");
            SeedCatalog catalog = await SeedCatalog.LoadAsync(
                seedsRoot, service, CancellationToken.None);
            string schema = await File.ReadAllTextAsync(schemaPath);
            string[] tables = System.Text.RegularExpressions.Regex.Matches(
                    schema,
                    @"(?im)^CREATE TABLE\s+(?<table>[a-z_]+)")
                .Select(match => match.Groups["table"].Value)
                .ToArray();
            string allSeeds = string.Join(
                Environment.NewLine,
                await Task.WhenAll(catalog.Seeds.Select(
                    seed => File.ReadAllTextAsync(seed.Path))));

            Assert.NotEmpty(tables);
            Assert.All(
                tables,
                table => Assert.Matches(
                    $@"(?im)^INSERT INTO\s+{System.Text.RegularExpressions.Regex.Escape(table)}\b",
                    allSeeds));
        }
    }

    [Fact]
    public async Task Catalog_rejects_gapped_sequences()
    {
        using var fixture = new SeedFixture();
        fixture.AddSeed(2, "unexpected");

        DataGenerationException exception = await Assert.ThrowsAsync<DataGenerationException>(
            () => SeedCatalog.LoadAsync(fixture.Root, "Catalog", CancellationToken.None));
        Assert.Contains("not linear", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_requires_a_schema_version_header()
    {
        using var fixture = new SeedFixture();
        fixture.AddSeed(1, "missing_header", "SELECT 1;");

        DataGenerationException exception = await Assert.ThrowsAsync<DataGenerationException>(
            () => SeedCatalog.LoadAsync(fixture.Root, "Catalog", CancellationToken.None));
        Assert.Contains("requires-schema-version", exception.Message, StringComparison.Ordinal);
    }

    private sealed class SeedFixture : IDisposable
    {
        public SeedFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"nexaconnect-seeds-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "Catalog"));
        }

        public string Root { get; }

        public void AddSeed(
            int sequence,
            string name,
            string sql = "-- requires-schema-version: 1\nSELECT 1;")
        {
            File.WriteAllText(
                Path.Combine(Root, "Catalog", $"{sequence:D4}_{name}.sql"),
                sql);
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
