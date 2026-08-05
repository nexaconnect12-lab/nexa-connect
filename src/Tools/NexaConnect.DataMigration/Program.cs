using Npgsql;

return await MigrationApplication.RunAsync(args);

internal static class MigrationApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        MigrationOptions? options;

        try
        {
            options = MigrationOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }

        if (options is null)
        {
            PrintUsage();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (options.EnvironmentFile is not null)
            {
                LoadEnvironmentFile(options.EnvironmentFile);
            }

            MigrationCatalog catalog = await MigrationCatalog.LoadAsync(
                options.ScriptsRoot,
                options.Service,
                cancellation.Token);

            string environmentVariable = ConnectionStringEnvironmentVariable(catalog.Service);
            string? connectionString = Environment.GetEnvironmentVariable(environmentVariable);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new MigrationException(
                    $"Set {environmentVariable} to the owning service's PostgreSQL connection string.");
            }

            await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellation.Token);
            var history = new MigrationHistoryStore(connection);

            IReadOnlyList<AppliedMigration> applied = await history.ReadAppliedAsync(cancellation.Token);
            catalog.ValidateAppliedMigrations(applied);

            if (options.Command == MigrationCommand.Status)
            {
                PrintStatus(catalog, applied);
                return 0;
            }

            int targetVersion = options.TargetVersion!.Value;
            IReadOnlyList<MigrationStep> plan = catalog.CreatePlan(applied, targetVersion);
            ValidateApplicationCompatibility(plan, options.ApplicationVersion);
            if (options.Command == MigrationCommand.Confirm)
            {
                ValidateDowngradeAuthorization(plan, options);
            }

            PrintPlan(catalog.Service, applied.Count, targetVersion, plan);

            if (options.Command == MigrationCommand.Plan || plan.Count == 0)
            {
                return 0;
            }

            await history.AcquireLockAsync(catalog.Service, cancellation.Token);
            try
            {
                // Re-read after taking the lock so the plan cannot be based on stale state.
                applied = await history.ReadAppliedAsync(cancellation.Token);
                catalog.ValidateAppliedMigrations(applied);
                IReadOnlyList<MigrationStep> lockedPlan = catalog.CreatePlan(applied, targetVersion);

                if (!plan.SequenceEqual(lockedPlan))
                {
                    throw new MigrationException(
                        "Database migration state changed after the plan was reviewed. " +
                        "Run --plan again before confirming.");
                }

                ValidateApplicationCompatibility(lockedPlan, options.ApplicationVersion);
                ValidateDowngradeAuthorization(lockedPlan, options);

                await history.EnsureCurrentSchemaAsync(
                    RuntimeRole(catalog.Service),
                    cancellation.Token);

                var executor = new MigrationExecutor(history, connection);
                Guid executionId = Guid.NewGuid();

                foreach (MigrationStep step in lockedPlan)
                {
                    await executor.ExecuteAsync(
                        step,
                        options.ApplicationVersion,
                        executionId,
                        cancellation.Token);
                }
            }
            finally
            {
                await history.ReleaseLockAsync(catalog.Service, CancellationToken.None);
            }

            Console.WriteLine($"{catalog.Service} is now at schema version {targetVersion}.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Migration operation cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is MigrationException or NpgsqlException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void PrintStatus(MigrationCatalog catalog, IReadOnlyList<AppliedMigration> applied)
    {
        Console.WriteLine($"Service: {catalog.Service}");
        Console.WriteLine($"Current database version: {applied.Count}");
        Console.WriteLine($"Latest available version: {catalog.LatestVersion}");

        if (applied.Count == 0)
        {
            Console.WriteLine("No migrations have been applied.");
            return;
        }

        foreach (AppliedMigration migration in applied)
        {
            Console.WriteLine(
                $"  {migration.Version:D4} {migration.Name} " +
                $"(applied {migration.AppliedAtUtc:O}, application {migration.ApplicationVersion})");
        }
    }

    private static void PrintPlan(
        string service,
        int currentVersion,
        int targetVersion,
        IReadOnlyList<MigrationStep> plan)
    {
        Console.WriteLine($"Service: {service}");
        Console.WriteLine($"Current version: {currentVersion}");
        Console.WriteLine($"Target version: {targetVersion}");

        if (plan.Count == 0)
        {
            Console.WriteLine("No migration steps are required.");
            return;
        }

        Console.WriteLine("Execution plan:");
        foreach (MigrationStep step in plan)
        {
            Console.WriteLine(
                $"  {step.Direction.ToString().ToUpperInvariant(),-4} " +
                $"{step.Migration.Version:D4}_{step.Migration.Metadata.Name} " +
                $"transactional={step.Migration.Metadata.Transactional.ToString().ToLowerInvariant()} " +
                $"downgradeSafety={step.Migration.Metadata.DowngradeSafety.ToString().ToLowerInvariant()}");
        }
    }

    private static void ValidateApplicationCompatibility(
        IReadOnlyList<MigrationStep> plan,
        string applicationVersion)
    {
        Version current = ParseVersion(applicationVersion, "application version");

        foreach (MigrationStep step in plan.Where(step => step.Direction == MigrationDirection.Up))
        {
            Version minimum = ParseVersion(
                step.Migration.Metadata.MinimumApplicationVersion,
                $"minimum application version for migration {step.Migration.Version}");

            if (current < minimum)
            {
                throw new MigrationException(
                    $"Application version {applicationVersion} cannot apply migration " +
                    $"{step.Migration.Version}; minimum version is {minimum}.");
            }
        }
    }

    private static Version ParseVersion(string value, string description)
    {
        string numericPart = value.Split('-', '+')[0];
        return Version.TryParse(numericPart, out Version? version)
            ? version
            : throw new MigrationException($"Invalid {description}: {value}");
    }

    private static void ValidateDowngradeAuthorization(
        IReadOnlyList<MigrationStep> plan,
        MigrationOptions options)
    {
        foreach (MigrationStep step in plan.Where(step => step.Direction == MigrationDirection.Down))
        {
            switch (step.Migration.Metadata.DowngradeSafety)
            {
                case DowngradeSafety.Unsupported:
                    throw new MigrationException(
                        $"Migration {step.Migration.Version} has an unsupported downgrade.");
                case DowngradeSafety.Transformative when !options.AllowTransformative:
                    throw new MigrationException(
                        $"Migration {step.Migration.Version} has a transformative downgrade. " +
                        "Pass --allow-transformative after validating the transformation.");
                case DowngradeSafety.Destructive when !options.AllowDestructive || !options.BackupVerified:
                    throw new MigrationException(
                        $"Migration {step.Migration.Version} has a destructive downgrade. " +
                        "Pass both --allow-destructive and --backup-verified after operational approval.");
            }
        }
    }

    private static string ConnectionStringEnvironmentVariable(string service) =>
        $"NEXACONNECT_{new string(service.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()}_DB";

    private static void LoadEnvironmentFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new MigrationException($"Environment file does not exist: {path}");
        }

        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                throw new MigrationException($"Invalid environment-file entry: {line}");
            }

            string name = trimmed[..separator].Trim();
            string value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static string RuntimeRole(string service)
    {
        string normalizedService = new string(service.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalizedService == "platformdirectory"
            ? "platform_directory_app"
            : $"nexaconnect_{normalizedService}_app";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --service <name> --status [--environment-file <path>]");
        Console.WriteLine("  dotnet run -- --service <name> --target <version> --plan [options]");
        Console.WriteLine("  dotnet run -- --service <name> --target <version> --confirm [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --scripts-root <path>          Root containing service migration directories.");
        Console.WriteLine("  --environment-file <path>      Load connection strings from an .env file.");
        Console.WriteLine("  --application-version <version>  Version recorded in migration history.");
        Console.WriteLine("  --allow-transformative            Authorize a transformative downgrade.");
        Console.WriteLine("  --allow-destructive               Authorize a destructive downgrade.");
        Console.WriteLine("  --backup-verified                 Confirm a verified backup exists.");
        Console.WriteLine("  --dry-run                         Alias for --plan.");
        Console.WriteLine();
        Console.WriteLine("Connection strings are read from NEXACONNECT_<SERVICE>_DB.");
    }
}

internal sealed class MigrationException(string message) : Exception(message);
