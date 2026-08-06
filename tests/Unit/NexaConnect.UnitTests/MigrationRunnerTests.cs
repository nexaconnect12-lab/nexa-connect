namespace NexaConnect.UnitTests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task Repository_catalog_loads_every_owned_service()
    {
        string scriptsRoot = Path.Combine(AppContext.BaseDirectory, "Scripts");
        var services = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["PlatformDirectory"] = 1,
            ["Authorization"] = 1,
            ["Restaurant"] = 1,
            ["Catalog"] = 2,
            ["Inventory"] = 2,
            ["Order"] = 1,
            ["Kitchen"] = 1,
            ["Customer"] = 1,
            ["Payment"] = 1,
            ["Notification"] = 1,
            ["POS"] = 3,
            ["Media"] = 1,
            ["Reporting"] = 1
        };

        foreach ((string service, int expectedVersion) in services)
        {
            MigrationCatalog catalog = await MigrationCatalog.LoadAsync(
                scriptsRoot,
                service,
                CancellationToken.None);

            Assert.Equal(service, catalog.Service);
            Assert.Equal(expectedVersion, catalog.LatestVersion);
        }
    }

    [Fact]
    public void Parse_requires_explicit_command_and_target()
    {
        ArgumentException missingCommand = Assert.Throws<ArgumentException>(
            () => MigrationOptions.Parse(["--service", "Order"]));
        ArgumentException missingTarget = Assert.Throws<ArgumentException>(
            () => MigrationOptions.Parse(["--service", "Order", "--plan"]));

        Assert.Contains("--status", missingCommand.Message, StringComparison.Ordinal);
        Assert.Contains("--target", missingTarget.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_accepts_confirmed_target()
    {
        MigrationOptions options = Assert.IsType<MigrationOptions>(MigrationOptions.Parse(
        [
            "--service", "Order",
            "--target", "1",
            "--confirm",
            "--application-version", "1.2.3"
        ]));

        Assert.Equal(MigrationCommand.Confirm, options.Command);
        Assert.Equal(1, options.TargetVersion);
        Assert.Equal("1.2.3", options.ApplicationVersion);
    }

    [Fact]
    public void Parse_accepts_an_environment_file()
    {
        string path = Path.GetFullPath(".env");
        MigrationOptions options = Assert.IsType<MigrationOptions>(MigrationOptions.Parse(
        [
            "--service", "Order",
            "--status",
            "--environment-file", ".env"
        ]));

        Assert.Equal(path, options.EnvironmentFile);
    }

    [Fact]
    public async Task Catalog_discovers_version_directories_and_builds_plans()
    {
        using var fixture = new MigrationFixture();
        fixture.AddMigration(1, "initial_schema", "safe");
        fixture.AddMigration(2, "add_channel", "transformative");

        MigrationCatalog catalog = await MigrationCatalog.LoadAsync(
            fixture.Root,
            "order",
            CancellationToken.None);

        Assert.Equal("Order", catalog.Service);
        Assert.Equal(2, catalog.LatestVersion);

        IReadOnlyList<MigrationStep> upgrade = catalog.CreatePlan([], 2);
        Assert.Collection(
            upgrade,
            step => Assert.Equal(1, step.Migration.Version),
            step => Assert.Equal(2, step.Migration.Version));

        IReadOnlyList<AppliedMigration> applied = catalog.Migrations
            .Select(ToAppliedMigration)
            .ToArray();
        IReadOnlyList<MigrationStep> downgrade = catalog.CreatePlan(applied, 0);

        Assert.Collection(
            downgrade,
            step => Assert.Equal(2, step.Migration.Version),
            step => Assert.Equal(1, step.Migration.Version));
        Assert.All(downgrade, step => Assert.Equal(MigrationDirection.Down, step.Direction));
    }

    [Fact]
    public async Task Catalog_rejects_gapped_sequences()
    {
        using var fixture = new MigrationFixture();
        fixture.AddMigration(2, "unexpected", "safe");

        MigrationException exception = await Assert.ThrowsAsync<MigrationException>(
            () => MigrationCatalog.LoadAsync(
                fixture.Root,
                "Order",
                CancellationToken.None));

        Assert.Contains("not linear", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_rejects_non_transactional_migrations()
    {
        using var fixture = new MigrationFixture();
        fixture.AddMigration(1, "concurrent_operation", "safe", transactional: false);

        MigrationException exception = await Assert.ThrowsAsync<MigrationException>(
            () => MigrationCatalog.LoadAsync(fixture.Root, "Order", CancellationToken.None));

        Assert.Contains("non-transactional", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_detects_modified_applied_files()
    {
        using var fixture = new MigrationFixture();
        fixture.AddMigration(1, "initial_schema", "safe");
        MigrationCatalog catalog = await MigrationCatalog.LoadAsync(
            fixture.Root,
            "Order",
            CancellationToken.None);
        AppliedMigration recorded = ToAppliedMigration(catalog.Migrations[0]) with
        {
            UpChecksum = new string('0', 64)
        };

        MigrationException exception = Assert.Throws<MigrationException>(
            () => catalog.ValidateAppliedMigrations([recorded]));

        Assert.Contains("differs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_retains_the_validated_script_content()
    {
        using var fixture = new MigrationFixture();
        fixture.AddMigration(1, "initial_schema", "safe");
        MigrationCatalog catalog = await MigrationCatalog.LoadAsync(
            fixture.Root,
            "Order",
            CancellationToken.None);

        File.WriteAllText(
            Path.Combine(fixture.Root, "Order", "0001_initial_schema", "up.sql"),
            "SELECT 999;");

        Assert.Equal("SELECT 1;", catalog.Migrations[0].UpSql);
    }

    private static AppliedMigration ToAppliedMigration(MigrationDefinition migration) =>
        new(
            migration.Version,
            migration.Metadata.Name,
            migration.MetadataChecksum,
            migration.UpChecksum,
            migration.DownChecksum,
            migration.Metadata.DowngradeSafety,
            DateTimeOffset.UtcNow,
            "1.0.0",
            Guid.NewGuid());

    private sealed class MigrationFixture : IDisposable
    {
        public MigrationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"nexaconnect-migrations-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "Order"));
        }

        public string Root { get; }

        public void AddMigration(
            int version,
            string name,
            string downgradeSafety,
            bool transactional = true)
        {
            string directory = Path.Combine(Root, "Order", $"{version:D4}_{name}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "migration.json"),
                $$"""{"version":{{version}},"name":"{{name}}","transactional":{{transactional.ToString().ToLowerInvariant()}},"downgradeSafety":"{{downgradeSafety}}","minimumApplicationVersion":"0.1.0"}""");
            File.WriteAllText(Path.Combine(directory, "up.sql"), $"SELECT {version};");
            File.WriteAllText(Path.Combine(directory, "down.sql"), $"SELECT {-version};");
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
