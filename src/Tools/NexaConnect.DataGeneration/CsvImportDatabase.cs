using Npgsql;

internal sealed class CsvImportDatabase(NpgsqlConnection connection)
{
    private const int CommandTimeoutSeconds = 60;
    public async Task<IReadOnlyList<CsvImportResult>> ImportAsync(
        CsvImportPackage package,
        CancellationToken cancellationToken)
    {
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        var results = new List<CsvImportResult>(package.Tables.Count);

        try
        {
            for (int index = 0; index < package.Tables.Count; index++)
            {
                CsvImportTable table = package.Tables[index];
                string temporaryTable = $"nexaconnect_csv_import_{index}";
                await CreateTemporaryTableAsync(
                    table.Table,
                    temporaryTable,
                    transaction,
                    cancellationToken);
                await CopyCsvAsync(table, temporaryTable, cancellationToken);
                int affectedRows = await UpsertAsync(
                    table,
                    temporaryTable,
                    transaction,
                    cancellationToken);
                results.Add(new CsvImportResult(table.Table, table.RowCount, affectedRows));
            }

            await transaction.CommitAsync(cancellationToken);
            return results;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task CreateTemporaryTableAsync(
        string targetTable,
        string temporaryTable,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        string sql = $"""
            CREATE TEMP TABLE {Quote(temporaryTable)}
                (LIKE public.{Quote(targetTable)} INCLUDING DEFAULTS)
                ON COMMIT DROP;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CopyCsvAsync(
        CsvImportTable table,
        string temporaryTable,
        CancellationToken cancellationToken)
    {
        string columns = string.Join(", ", table.Columns.Select(Quote));
        string copy =
            $"COPY {Quote(temporaryTable)} ({columns}) FROM STDIN " +
            "(FORMAT CSV, HEADER TRUE, NULL '\\N', ENCODING 'UTF8')";
        await using TextWriter writer = await connection.BeginTextImportAsync(copy, cancellationToken);
        using var reader = new StreamReader(
            table.Path,
            new System.Text.UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[81920];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await writer.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private async Task<int> UpsertAsync(
        CsvImportTable table,
        string temporaryTable,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        string columns = string.Join(", ", table.Columns.Select(Quote));
        string keys = string.Join(", ", table.KeyColumns.Select(Quote));
        string[] updateColumns = table.Columns
            .Except(table.KeyColumns, StringComparer.Ordinal)
            .ToArray();
        string conflictAction = updateColumns.Length == 0
            ? "DO NOTHING"
            : "DO UPDATE SET " + string.Join(", ", updateColumns.Select(
                column => $"{Quote(column)} = EXCLUDED.{Quote(column)}"));
        string sql = $"""
            INSERT INTO public.{Quote(table.Table)} ({columns})
            SELECT {columns} FROM {Quote(temporaryTable)}
            ON CONFLICT ({keys}) {conflictAction};
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds
        };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";
}

internal sealed record CsvImportResult(string Table, int SourceRows, int AffectedRows);
