using Npgsql;

return await DataGenerationApplication.RunAsync(args);

internal static class DataGenerationApplication
{
    internal static readonly string[] ServiceOrder =
    [
        "PlatformDirectory",
        "Restaurant",
        "Catalog",
        "Customer",
        "Order",
        "Inventory",
        "Kitchen",
        "Payment",
        "POS",
        "Media",
        "Reporting"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        DataGenerationOptions? options;

        try
        {
            options = DataGenerationOptions.Parse(args);
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
                EnvironmentFile.Load(options.EnvironmentFile);
            }

            ValidateEnvironment();
            if (options.ImportPackage is not null)
            {
                return await RunCsvImportAsync(options, cancellation.Token);
            }

            string[] services = options.AllServices
                ? ServiceOrder
                : [options.Service!];
            var catalogs = new List<SeedCatalog>(services.Length);
            foreach (string service in services)
            {
                catalogs.Add(await SeedCatalog.LoadAsync(
                    options.SeedsRoot,
                    service,
                    cancellation.Token));
            }

            foreach (SeedCatalog catalog in catalogs)
            {
                PrintPlan(catalog);
            }
            if (options.Command == DataGenerationCommand.Plan)
            {
                return 0;
            }

            var connectionStrings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SeedCatalog catalog in catalogs)
            {
                string variable = ConnectionStringEnvironmentVariable(catalog.Service);
                string? connectionString = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new DataGenerationException(
                        $"Set {variable} to the owning service's PostgreSQL connection string.");
                }

                connectionStrings.Add(catalog.Service, connectionString);
            }

            foreach (SeedCatalog catalog in catalogs)
            {
                await GenerateServiceAsync(
                    catalog,
                    connectionStrings[catalog.Service],
                    cancellation.Token);
            }

            Console.WriteLine(
                options.AllServices
                    ? $"Generated sample data for all {catalogs.Count} databases successfully."
                    : $"Generated {catalogs[0].Service} sample data successfully.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Data generation cancelled.");
            return 130;
        }
        catch (Exception exception) when (
            exception is DataGenerationException or NpgsqlException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<int> RunCsvImportAsync(
        DataGenerationOptions options,
        CancellationToken cancellationToken)
    {
        string[] expectedServices = options.AllServices
            ? ServiceOrder
            : [options.Service!];
        var packages = new List<CsvImportPackage>(expectedServices.Length);
        foreach (string expectedService in expectedServices)
        {
            if (!ServiceOrder.Contains(expectedService, StringComparer.OrdinalIgnoreCase))
            {
                throw new DataGenerationException(
                    $"Unsupported import service '{expectedService}'.");
            }

            string packagePath = options.AllServices
                ? Path.Combine(options.ImportPackage!, expectedService)
                : options.ImportPackage!;
            CsvImportPackage package = await CsvImportPackage.LoadAsync(
                packagePath,
                cancellationToken);
            if (!string.Equals(package.Service, expectedService, StringComparison.OrdinalIgnoreCase))
            {
                throw new DataGenerationException(
                    $"Import package service '{package.Service}' does not match " +
                    $"the requested service '{expectedService}'.");
            }

            packages.Add(package);
        }

        foreach (CsvImportPackage package in packages)
        {
            PrintImportPlan(package);
        }
        if (options.Command == DataGenerationCommand.Plan)
        {
            return 0;
        }

        var connectionStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CsvImportPackage package in packages)
        {
            string variable = ConnectionStringEnvironmentVariable(package.Service);
            string? connectionString = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new DataGenerationException(
                    $"Set {variable} to the owning service's PostgreSQL connection string.");
            }

            connectionStrings.Add(package.Service, connectionString);
        }

        foreach (CsvImportPackage package in packages)
        {
            await ImportCsvPackageAsync(
                package,
                connectionStrings[package.Service],
                cancellationToken);
        }

        Console.WriteLine(
            options.AllServices
                ? $"Imported CSV packages for all {packages.Count} databases successfully."
                : $"Imported CSV package for {packages[0].Service} successfully.");
        return 0;
    }

    private static async Task ImportCsvPackageAsync(
        CsvImportPackage package,
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlConnection connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        var seedDatabase = new SeedDatabase(connection);
        await seedDatabase.AcquireLockAsync(package.Service, cancellationToken);
        try
        {
            int schemaVersion = await seedDatabase.ReadSchemaVersionAsync(cancellationToken);
            if (package.RequiredSchemaVersion > schemaVersion)
            {
                throw new DataGenerationException(
                    $"Import package requires schema version {package.RequiredSchemaVersion}, " +
                    $"but the database is at version {schemaVersion}.");
            }

            var database = new CsvImportDatabase(connection);
            IReadOnlyList<CsvImportResult> results = await database.ImportAsync(
                package,
                cancellationToken);
            foreach (CsvImportResult result in results)
            {
                Console.WriteLine(
                    $"Imported {result.SourceRows} rows into {result.Table} " +
                    $"({result.AffectedRows} inserted or updated).");
            }
        }
        finally
        {
            await seedDatabase.ReleaseLockAsync(package.Service, CancellationToken.None);
        }

    }

    private static async Task GenerateServiceAsync(
        SeedCatalog catalog,
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlConnection connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        var database = new SeedDatabase(connection);

        await database.AcquireLockAsync(catalog.Service, cancellationToken);
        try
        {
            int schemaVersion = await database.ReadSchemaVersionAsync(cancellationToken);
            foreach (SeedDefinition seed in catalog.Seeds)
            {
                if (seed.RequiredSchemaVersion > schemaVersion)
                {
                    throw new DataGenerationException(
                        $"Seed {seed.FileName} requires schema version " +
                        $"{seed.RequiredSchemaVersion}, but the database is at version {schemaVersion}.");
                }

                await database.ExecuteAsync(seed, cancellationToken);
                Console.WriteLine($"Applied {catalog.Service}/{seed.FileName}.");
            }
        }
        finally
        {
            await database.ReleaseLockAsync(catalog.Service, CancellationToken.None);
        }
    }

    internal static void ValidateEnvironment()
    {
        string? environment =
            Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new DataGenerationException(
                "Data generation is disabled when the environment is Production.");
        }
    }

    private static void PrintPlan(SeedCatalog catalog)
    {
        Console.WriteLine($"Service: {catalog.Service}");
        Console.WriteLine($"Seed scripts: {catalog.Seeds.Count}");
        foreach (SeedDefinition seed in catalog.Seeds)
        {
            Console.WriteLine(
                $"  {seed.FileName} schema>={seed.RequiredSchemaVersion} " +
                $"sha256={seed.Checksum[..12]}");
        }
    }

    private static void PrintImportPlan(CsvImportPackage package)
    {
        Console.WriteLine($"CSV import service: {package.Service}");
        Console.WriteLine($"Required schema version: {package.RequiredSchemaVersion}");
        Console.WriteLine($"Tables: {package.Tables.Count}");
        Console.WriteLine($"Rows: {package.Tables.Sum(table => table.RowCount)}");
        foreach (CsvImportTable table in package.Tables)
        {
            Console.WriteLine(
                $"  {table.Table} <= {table.File}: {table.RowCount} rows, " +
                $"key=({string.Join(",", table.KeyColumns)})");
        }
    }

    private static string ConnectionStringEnvironmentVariable(string service) =>
        $"NEXACONNECT_{new string(service.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()}_DB";

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --all --plan [--seeds-root <path>]");
        Console.WriteLine("  dotnet run -- --all --confirm [--environment-file <path>]");
        Console.WriteLine("  dotnet run -- --service <name> --plan [--seeds-root <path>]");
        Console.WriteLine("  dotnet run -- --service <name> --confirm [--environment-file <path>]");
        Console.WriteLine("  dotnet run -- --service <name> --import-package <path> --plan");
        Console.WriteLine(
            "  dotnet run -- --service <name> --import-package <path> --confirm " +
            "[--environment-file <path>]");
        Console.WriteLine("  dotnet run -- --all --import-package <root> --plan");
        Console.WriteLine(
            "  dotnet run -- --all --import-package <root> --confirm " +
            "[--environment-file <path>]");
        Console.WriteLine();
        Console.WriteLine("Connection strings are read from NEXACONNECT_<SERVICE>_DB.");
        Console.WriteLine("The runner refuses to execute when the environment is Production.");
    }
}
