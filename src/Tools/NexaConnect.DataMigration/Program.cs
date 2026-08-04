using System.Security.Cryptography;
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

        string serviceDirectory = Path.Combine(options.ScriptsRoot, options.Service);
        string[] scripts = Directory.Exists(serviceDirectory)
            ? Directory.GetFiles(serviceDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray()
            : [];

        if (scripts.Length == 0)
        {
            Console.WriteLine($"No migration scripts found for {options.Service} in {serviceDirectory}.");
            return 0;
        }

        Console.WriteLine($"Found {scripts.Length} migration script(s) for {options.Service}.");

        if (options.DryRun)
        {
            foreach (string script in scripts)
            {
                Console.WriteLine($"  {Path.GetFileName(script)}");
            }

            return 0;
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

        await EnsureHistoryTableAsync(connection, RuntimeRole(options.Service), cancellation.Token);

        foreach (string scriptPath in scripts)
        {
            await ApplyScriptAsync(connection, scriptPath, cancellation.Token);
        }

        return 0;
    }

    private static async Task EnsureHistoryTableAsync(
        NpgsqlConnection connection,
        string runtimeRole,
        CancellationToken cancellationToken)
    {
        string sql = $$"""
            CREATE TABLE IF NOT EXISTS nexaconnect_schema_migrations
            (
                script_name text PRIMARY KEY,
                checksum_sha256 text NOT NULL,
                applied_at_utc timestamptz NOT NULL DEFAULT now()
            );

            DO $block$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{runtimeRole}}') THEN
                    EXECUTE format(
                        'REVOKE ALL PRIVILEGES ON TABLE nexaconnect_schema_migrations FROM %I',
                        '{{runtimeRole}}');
                END IF;
            END
            $block$;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyScriptAsync(
        NpgsqlConnection connection,
        string scriptPath,
        CancellationToken cancellationToken)
    {
        string scriptName = Path.GetFileName(scriptPath);
        byte[] scriptBytes = await File.ReadAllBytesAsync(scriptPath, cancellationToken);
        string checksum = Convert.ToHexString(SHA256.HashData(scriptBytes));

        const string lookupSql = """
            SELECT checksum_sha256
            FROM nexaconnect_schema_migrations
            WHERE script_name = $1;
            """;

        await using (var lookup = new NpgsqlCommand(lookupSql, connection))
        {
            lookup.Parameters.AddWithValue(scriptName);
            object? existingChecksum = await lookup.ExecuteScalarAsync(cancellationToken);

            if (existingChecksum is string appliedChecksum)
            {
                if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Applied migration {scriptName} has been modified. Create a new migration instead.");
                }

                Console.WriteLine($"Skipped {scriptName} (already applied).");
                return;
            }
        }

        string sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var migration = new NpgsqlCommand(sql, connection, transaction))
            {
                await migration.ExecuteNonQueryAsync(cancellationToken);
            }

            const string recordSql = """
                INSERT INTO nexaconnect_schema_migrations (script_name, checksum_sha256)
                VALUES ($1, $2);
                """;

            await using (var record = new NpgsqlCommand(recordSql, connection, transaction))
            {
                record.Parameters.AddWithValue(scriptName);
                record.Parameters.AddWithValue(checksum);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine($"Applied {scriptName}.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string ConnectionStringEnvironmentVariable(string service) =>
        $"NEXACONNECT_{new string(service.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()}_DB";

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
        Console.WriteLine("  dotnet run -- --service <name> [--scripts-root <path>] [--dry-run]");
        Console.WriteLine();
        Console.WriteLine("Connection strings are read from NEXACONNECT_<SERVICE>_DB.");
    }
}

internal sealed record MigrationOptions(string Service, string ScriptsRoot, bool DryRun)
{
    public static MigrationOptions? Parse(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string? service = null;
        string scriptsRoot = Path.Combine(AppContext.BaseDirectory, "Scripts");
        bool dryRun = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--service" when index + 1 < args.Length:
                    service = args[++index];
                    break;
                case "--scripts-root" when index + 1 < args.Length:
                    scriptsRoot = Path.GetFullPath(args[++index]);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(service))
        {
            throw new ArgumentException("The --service argument is required.");
        }

        return new MigrationOptions(service, scriptsRoot, dryRun);
    }
}
