using Npgsql;

return await DataGenerationApplication.RunAsync(args);

internal static class DataGenerationApplication
{
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

        string serviceDirectory = Path.Combine(options.SeedsRoot, options.Service);
        string[] seeds = Directory.Exists(serviceDirectory)
            ? Directory.GetFiles(serviceDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray()
            : [];

        if (seeds.Length == 0)
        {
            Console.WriteLine($"No seed scripts found for {options.Service} in {serviceDirectory}.");
            return 0;
        }

        Console.WriteLine($"Found {seeds.Length} seed script(s) for {options.Service}.");
        foreach (string seed in seeds)
        {
            Console.WriteLine($"  {Path.GetFileName(seed)}");
        }

        if (options.DryRun)
        {
            return 0;
        }

        if (!options.Confirmed)
        {
            Console.Error.WriteLine("Data generation changes the database. Pass --confirm after reviewing --dry-run output.");
            return 2;
        }

        string environmentVariable = ConnectionStringEnvironmentVariable(options.Service);
        string? connectionString = Environment.GetEnvironmentVariable(environmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set {environmentVariable} to the owning service's PostgreSQL connection string.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellation.Token);

        foreach (string seedPath in seeds)
        {
            await ExecuteSeedAsync(connection, seedPath, cancellation.Token);
        }

        return 0;
    }

    private static async Task ExecuteSeedAsync(
        NpgsqlConnection connection,
        string seedPath,
        CancellationToken cancellationToken)
    {
        string sql = await File.ReadAllTextAsync(seedPath, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine($"Executed {Path.GetFileName(seedPath)}.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string ConnectionStringEnvironmentVariable(string service) =>
        $"NEXACONNECT_{new string(service.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()}_DB";

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --service <name> [--seeds-root <path>] [--dry-run | --confirm]");
        Console.WriteLine();
        Console.WriteLine("Connection strings are read from NEXACONNECT_<SERVICE>_DB.");
    }
}

internal sealed record DataGenerationOptions(
    string Service,
    string SeedsRoot,
    bool DryRun,
    bool Confirmed)
{
    public static DataGenerationOptions? Parse(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string? service = null;
        string seedsRoot = Path.Combine(AppContext.BaseDirectory, "Seeds");
        bool dryRun = false;
        bool confirmed = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--service" when index + 1 < args.Length:
                    service = args[++index];
                    break;
                case "--seeds-root" when index + 1 < args.Length:
                    seedsRoot = Path.GetFullPath(args[++index]);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--confirm":
                    confirmed = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(service))
        {
            throw new ArgumentException("The --service argument is required.");
        }

        if (dryRun && confirmed)
        {
            throw new ArgumentException("Use either --dry-run or --confirm, not both.");
        }

        return new DataGenerationOptions(service, seedsRoot, dryRun, confirmed);
    }
}
