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
            return await RunCsvImportAsync(options, cancellation.Token);
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
        var migrationConnectionStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CsvImportPackage package in packages)
        {
            string variable = ImportConnectionStringEnvironmentVariable(package.Service);
            string? connectionString = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new DataGenerationException(
                    $"Set {variable} to the owning service's restricted import connection string.");
            }

            connectionStrings.Add(package.Service, connectionString);
            string migrationVariable = ConnectionStringEnvironmentVariable(package.Service);
            string? migrationConnectionString = Environment.GetEnvironmentVariable(migrationVariable);
            if (string.IsNullOrWhiteSpace(migrationConnectionString))
            {
                throw new DataGenerationException(
                    $"Set {migrationVariable} to the owning service's migration connection string.");
            }

            migrationConnectionStrings.Add(package.Service, migrationConnectionString);
        }

        foreach (CsvImportPackage package in packages)
        {
            await ImportCsvPackageAsync(
                package,
                connectionStrings[package.Service],
                migrationConnectionStrings[package.Service],
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
        string migrationConnectionString,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataSource migrationDataSource = NpgsqlDataSource.Create(migrationConnectionString);
        await using NpgsqlConnection migrationConnection =
            await migrationDataSource.OpenConnectionAsync(cancellationToken);
        var migrationDatabase = new ImportDatabaseSession(migrationConnection);
        int schemaVersion = await migrationDatabase.ReadSchemaVersionAsync(cancellationToken);
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlConnection connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        var importDatabase = new ImportDatabaseSession(connection);
        await importDatabase.AcquireLockAsync(package.Service, cancellationToken);
        try
        {
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
            await importDatabase.ReleaseLockAsync(package.Service, CancellationToken.None);
        }

    }

    internal static void ValidateEnvironment()
    {
        string? environment =
            Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            throw new DataGenerationException(
                "Data generation requires NEXACONNECT_ENVIRONMENT, DOTNET_ENVIRONMENT, or " +
                "ASPNETCORE_ENVIRONMENT to be Development, Test, or Testing.");
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

    private static string ImportConnectionStringEnvironmentVariable(string service) =>
        $"NEXACONNECT_{new string(service.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()}_IMPORT_DB";

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --service <name> --import-package <path> --plan");
        Console.WriteLine(
            "  dotnet run -- --service <name> --import-package <path> --confirm " +
            "[--environment-file <path>]");
        Console.WriteLine("  dotnet run -- --all --import-package <root> --plan");
        Console.WriteLine(
            "  dotnet run -- --all --import-package <root> --confirm " +
            "[--environment-file <path>]");
        Console.WriteLine();
        Console.WriteLine("CSV imports use NEXACONNECT_<SERVICE>_IMPORT_DB and the owning service's migration connection.");
        Console.WriteLine("The runner executes only in Development, Test, or Testing environments.");
    }
}
