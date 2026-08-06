using Npgsql;

internal sealed class ImportDatabaseSession(NpgsqlConnection connection)
{
    private const int CommandTimeoutSeconds = 60;
    public async Task<int> ReadSchemaVersionAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                WHEN to_regclass('public.nexaconnect_schema_migrations') IS NULL THEN -1
                ELSE (SELECT COALESCE(max(version), 0) FROM public.nexaconnect_schema_migrations)
            END;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        int version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (version < 0)
        {
            throw new DataGenerationException(
                "Migration history is missing. Run NexaConnect.DataMigration before generating data.");
        }

        return version;
    }

    public async Task AcquireLockAsync(string service, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_lock(hashtextextended($1, 0));",
            connection);
        command.Parameters.AddWithValue($"nexaconnect:data-generation:{service}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseLockAsync(string service, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(hashtextextended($1, 0));",
            connection);
        command.Parameters.AddWithValue($"nexaconnect:data-generation:{service}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
