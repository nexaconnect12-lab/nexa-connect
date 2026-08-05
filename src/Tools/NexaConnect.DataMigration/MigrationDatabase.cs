using Npgsql;

internal sealed class MigrationHistoryStore(NpgsqlConnection connection)
{
    private const string HistoryTable = "public.nexaconnect_schema_migrations";

    public async Task<IReadOnlyList<AppliedMigration>> ReadAppliedAsync(
        CancellationToken cancellationToken)
    {
        HistoryTableLayout layout = await GetLayoutAsync(cancellationToken);
        if (layout == HistoryTableLayout.Missing)
        {
            return [];
        }

        if (layout == HistoryTableLayout.Legacy)
        {
            long rowCount = await CountHistoryRowsAsync(cancellationToken);
            if (rowCount > 0)
            {
                throw new MigrationException(
                    "The database contains legacy script-based migration history. " +
                    "Migrate that history explicitly before using the versioned runner.");
            }

            return [];
        }

        const string sql = """
            SELECT
                version,
                name,
                metadata_checksum_sha256,
                up_checksum_sha256,
                down_checksum_sha256,
                downgrade_safety,
                applied_at_utc,
                application_version,
                execution_id
            FROM public.nexaconnect_schema_migrations
            ORDER BY version;
            """;

        await using var command = CreateCommand(sql);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var migrations = new List<AppliedMigration>();

        while (await reader.ReadAsync(cancellationToken))
        {
            string safetyValue = reader.GetString(5);
            if (!Enum.TryParse(safetyValue, true, out DowngradeSafety safety))
            {
                throw new MigrationException(
                    $"Migration history contains invalid downgrade safety '{safetyValue}'.");
            }

            migrations.Add(new AppliedMigration(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                safety,
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetString(7),
                reader.GetGuid(8)));
        }

        return migrations;
    }

    public async Task EnsureCurrentSchemaAsync(
        string runtimeRole,
        CancellationToken cancellationToken)
    {
        HistoryTableLayout layout = await GetLayoutAsync(cancellationToken);
        if (layout == HistoryTableLayout.Legacy)
        {
            long rowCount = await CountHistoryRowsAsync(cancellationToken);
            if (rowCount > 0)
            {
                throw new MigrationException(
                    "Cannot replace non-empty legacy migration history automatically.");
            }

            await using var drop = CreateCommand($"DROP TABLE {HistoryTable};");
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        const string createSql = """
            CREATE TABLE IF NOT EXISTS public.nexaconnect_schema_migrations
            (
                version integer PRIMARY KEY CHECK (version > 0),
                name text NOT NULL CHECK (char_length(btrim(name)) > 0),
                metadata_checksum_sha256 text NOT NULL CHECK (metadata_checksum_sha256 ~ '^[0-9A-F]{64}$'),
                up_checksum_sha256 text NOT NULL CHECK (up_checksum_sha256 ~ '^[0-9A-F]{64}$'),
                down_checksum_sha256 text NOT NULL CHECK (down_checksum_sha256 ~ '^[0-9A-F]{64}$'),
                downgrade_safety text NOT NULL
                    CHECK (downgrade_safety IN ('safe', 'transformative', 'destructive', 'unsupported')),
                applied_at_utc timestamptz NOT NULL DEFAULT now(),
                application_version text NOT NULL CHECK (char_length(btrim(application_version)) > 0),
                execution_id uuid NOT NULL
            );
            """;

        await using (var create = CreateCommand(createSql))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        const string roleExistsSql = "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1);";
        await using var roleExists = CreateCommand(roleExistsSql);
        roleExists.Parameters.AddWithValue(runtimeRole);
        bool exists = (bool)(await roleExists.ExecuteScalarAsync(cancellationToken) ?? false);

        if (exists)
        {
            string quotedRole = new NpgsqlCommandBuilder().QuoteIdentifier(runtimeRole);
            await using var revoke = CreateCommand(
                $"REVOKE ALL PRIVILEGES ON TABLE {HistoryTable} FROM {quotedRole};");
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task AcquireLockAsync(string service, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "SELECT pg_advisory_lock(hashtextextended($1, 0));");
        command.Parameters.AddWithValue(LockName(service));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseLockAsync(string service, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var command = CreateCommand(
            "SELECT pg_advisory_unlock(hashtextextended($1, 0));");
        command.Parameters.AddWithValue(LockName(service));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordUpgradeAsync(
        MigrationDefinition migration,
        string applicationVersion,
        Guid executionId,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO public.nexaconnect_schema_migrations
            (
                version,
                name,
                metadata_checksum_sha256,
                up_checksum_sha256,
                down_checksum_sha256,
                downgrade_safety,
                application_version,
                execution_id
            )
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8);
            """;

        await using var command = CreateCommand(sql, transaction);
        command.Parameters.AddWithValue(migration.Version);
        command.Parameters.AddWithValue(migration.Metadata.Name);
        command.Parameters.AddWithValue(migration.MetadataChecksum);
        command.Parameters.AddWithValue(migration.UpChecksum);
        command.Parameters.AddWithValue(migration.DownChecksum);
        command.Parameters.AddWithValue(
            migration.Metadata.DowngradeSafety.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue(applicationVersion);
        command.Parameters.AddWithValue(executionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordDowngradeAsync(
        int version,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "DELETE FROM public.nexaconnect_schema_migrations WHERE version = $1;",
            transaction);
        command.Parameters.AddWithValue(version);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new MigrationException(
                $"Expected one migration history row for version {version}, deleted {affected}.");
        }
    }

    private async Task<HistoryTableLayout> GetLayoutAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                to_regclass('public.nexaconnect_schema_migrations') IS NOT NULL,
                EXISTS
                (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'nexaconnect_schema_migrations'
                      AND column_name = 'version'
                );
            """;

        await using var command = CreateCommand(sql);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        bool exists = reader.GetBoolean(0);
        bool hasVersion = reader.GetBoolean(1);
        return !exists
            ? HistoryTableLayout.Missing
            : hasVersion
                ? HistoryTableLayout.Current
                : HistoryTableLayout.Legacy;
    }

    private async Task<long> CountHistoryRowsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT count(*) FROM {HistoryTable};");
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private NpgsqlCommand CreateCommand(string sql, NpgsqlTransaction? transaction = null) =>
        new(sql, connection, transaction) { CommandTimeout = 0 };

    private static string LockName(string service) => $"nexaconnect:migrations:{service}";

    private enum HistoryTableLayout
    {
        Missing,
        Legacy,
        Current
    }
}

internal sealed class MigrationExecutor(
    MigrationHistoryStore history,
    NpgsqlConnection connection)
{
    public async Task ExecuteAsync(
        MigrationStep step,
        string applicationVersion,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        string scriptPath = step.Direction == MigrationDirection.Up
            ? step.Migration.UpPath
            : step.Migration.DownPath;
        string sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);

        if (step.Migration.Metadata.Transactional)
        {
            await ExecuteTransactionalAsync(
                step,
                sql,
                applicationVersion,
                executionId,
                cancellationToken);
        }
        else
        {
            await ExecuteNonTransactionalAsync(
                step,
                sql,
                applicationVersion,
                executionId,
                cancellationToken);
        }

        Console.WriteLine(
            $"{(step.Direction == MigrationDirection.Up ? "Applied" : "Reverted")} " +
            $"{step.Migration.Version:D4}_{step.Migration.Metadata.Name}.");
    }

    private async Task ExecuteTransactionalAsync(
        MigrationStep step,
        string sql,
        string applicationVersion,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteSqlAsync(sql, transaction, cancellationToken);
            await UpdateHistoryAsync(
                step,
                applicationVersion,
                executionId,
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ExecuteNonTransactionalAsync(
        MigrationStep step,
        string sql,
        string applicationVersion,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(sql, null, cancellationToken);
        await UpdateHistoryAsync(
            step,
            applicationVersion,
            executionId,
            null,
            cancellationToken);
    }

    private async Task ExecuteSqlAsync(
        string sql,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 0
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private Task UpdateHistoryAsync(
        MigrationStep step,
        string applicationVersion,
        Guid executionId,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken) =>
        step.Direction == MigrationDirection.Up
            ? history.RecordUpgradeAsync(
                step.Migration,
                applicationVersion,
                executionId,
                transaction,
                cancellationToken)
            : history.RecordDowngradeAsync(
                step.Migration.Version,
                transaction,
                cancellationToken);
}
