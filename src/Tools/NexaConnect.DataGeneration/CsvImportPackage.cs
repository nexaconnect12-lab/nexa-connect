using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal sealed record CsvImportTable(
    string Table,
    string File,
    IReadOnlyList<string> KeyColumns,
    int MinimumRows,
    IReadOnlyList<string> Columns,
    int RowCount,
    string Path);

internal sealed record CsvImportPackage(
    string Root,
    string Service,
    int RequiredSchemaVersion,
    int MinimumTotalRows,
    IReadOnlyList<CsvImportTable> Tables)
{
    private static readonly Regex IdentifierPattern = new(
        "^[a-z][a-z0-9_]{0,62}$",
        RegexOptions.CultureInvariant);

    public static async Task<CsvImportPackage> LoadAsync(
        string root,
        CancellationToken cancellationToken)
    {
        string packageRoot = Path.GetFullPath(root);
        string manifestPath = Path.Combine(packageRoot, "manifest.json");
        if (!Directory.Exists(packageRoot) || !File.Exists(manifestPath))
        {
            throw new DataGenerationException(
                $"CSV import package must contain manifest.json: {packageRoot}");
        }

        ImportManifest? manifest;
        try
        {
            await using FileStream stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ImportManifest>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
                },
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new DataGenerationException($"Invalid import manifest: {exception.Message}");
        }

        if (manifest is null || manifest.FormatVersion != 1)
        {
            throw new DataGenerationException("Import manifest formatVersion must be 1.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Service) || manifest.RequiredSchemaVersion < 1)
        {
            throw new DataGenerationException(
                "Import manifest requires a service and requiredSchemaVersion greater than zero.");
        }

        if (manifest.MinimumTotalRows < 1 || manifest.Tables is not { Count: > 0 })
        {
            throw new DataGenerationException(
                "Import manifest requires minimumTotalRows and at least one table.");
        }

        var tables = new List<CsvImportTable>(manifest.Tables.Count);
        var tableNames = new HashSet<string>(StringComparer.Ordinal);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalRows = 0;

        foreach (ImportTableManifest entry in manifest.Tables)
        {
            ValidateIdentifier(entry.Table, "table");
            if (entry.Table.StartsWith("nexaconnect_", StringComparison.Ordinal))
            {
                throw new DataGenerationException(
                    $"Table {entry.Table} is reserved for NexaConnect operational data and cannot be imported.");
            }
            if (!tableNames.Add(entry.Table))
            {
                throw new DataGenerationException($"Duplicate table in import manifest: {entry.Table}");
            }

            if (entry.MinimumRows < 1 || entry.KeyColumns is not { Count: > 0 })
            {
                throw new DataGenerationException(
                    $"Table {entry.Table} requires minimumRows and at least one key column.");
            }

            foreach (string keyColumn in entry.KeyColumns)
            {
                ValidateIdentifier(keyColumn, $"key column for {entry.Table}");
            }
            if (entry.KeyColumns.Distinct(StringComparer.Ordinal).Count() != entry.KeyColumns.Count)
            {
                throw new DataGenerationException(
                    $"Table {entry.Table} contains duplicate key columns.");
            }

            if (string.IsNullOrWhiteSpace(entry.File) ||
                !string.Equals(Path.GetExtension(entry.File), ".csv", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(entry.File) != entry.File ||
                !fileNames.Add(entry.File))
            {
                throw new DataGenerationException(
                    $"Table {entry.Table} must reference a unique CSV file name without a path.");
            }

            string csvPath = Path.Combine(packageRoot, entry.File);
            if (!File.Exists(csvPath))
            {
                throw new DataGenerationException($"CSV file does not exist: {csvPath}");
            }

            string csv = await ReadUtf8Async(csvPath, cancellationToken);
            IReadOnlyList<string[]> rows = CsvParser.Parse(csv, entry.File);
            if (rows.Count < 2)
            {
                throw new DataGenerationException($"CSV file has no data rows: {entry.File}");
            }

            string[] columns = rows[0];
            if (columns.Length == 0 || columns.Any(string.IsNullOrWhiteSpace))
            {
                throw new DataGenerationException($"CSV file has an empty header: {entry.File}");
            }

            var uniqueColumns = new HashSet<string>(StringComparer.Ordinal);
            foreach (string column in columns)
            {
                ValidateIdentifier(column, $"column in {entry.File}");
                if (!uniqueColumns.Add(column))
                {
                    throw new DataGenerationException(
                        $"CSV file has a duplicate column '{column}': {entry.File}");
                }
            }

            if (entry.KeyColumns.Any(key => !uniqueColumns.Contains(key)))
            {
                throw new DataGenerationException(
                    $"Every key column for {entry.Table} must appear in {entry.File}.");
            }

            int[] keyIndexes = entry.KeyColumns
                .Select(key => Array.IndexOf(columns, key))
                .ToArray();
            var rowKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (rows[rowIndex].Length != columns.Length)
                {
                    throw new DataGenerationException(
                        $"CSV {entry.File} row {rowIndex + 1} has {rows[rowIndex].Length} " +
                        $"fields; expected {columns.Length}.");
                }

                if (keyIndexes.Any(keyIndex => rows[rowIndex][keyIndex] == "\\N"))
                {
                    throw new DataGenerationException(
                        $"CSV {entry.File} row {rowIndex + 1} contains NULL in an import key.");
                }

                string rowKey = string.Concat(keyIndexes.Select(keyIndex =>
                {
                    string value = rows[rowIndex][keyIndex];
                    return $"{value.Length}:{value}";
                }));
                if (!rowKeys.Add(rowKey))
                {
                    throw new DataGenerationException(
                        $"CSV {entry.File} row {rowIndex + 1} duplicates an import key.");
                }
            }

            int rowCount = rows.Count - 1;
            if (rowCount < entry.MinimumRows)
            {
                throw new DataGenerationException(
                    $"CSV {entry.File} has {rowCount} rows; minimumRows is {entry.MinimumRows}.");
            }

            totalRows += rowCount;
            tables.Add(new CsvImportTable(
                entry.Table,
                entry.File,
                entry.KeyColumns,
                entry.MinimumRows,
                columns,
                rowCount,
                csvPath));
        }

        if (totalRows < manifest.MinimumTotalRows)
        {
            throw new DataGenerationException(
                $"Import package has {totalRows} rows; minimumTotalRows is {manifest.MinimumTotalRows}.");
        }

        return new CsvImportPackage(
            packageRoot,
            manifest.Service,
            manifest.RequiredSchemaVersion,
            manifest.MinimumTotalRows,
            tables);
    }

    private static async Task<string> ReadUtf8Async(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var encoding = new UTF8Encoding(false, true);
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return encoding.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            throw new DataGenerationException($"CSV file must be valid UTF-8: {path}");
        }
    }

    private static void ValidateIdentifier(string identifier, string description)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierPattern.IsMatch(identifier))
        {
            throw new DataGenerationException(
                $"Invalid {description} identifier '{identifier}'. Use lowercase snake_case.");
        }
    }

    private sealed record ImportManifest(
        int FormatVersion,
        string Service,
        int RequiredSchemaVersion,
        int MinimumTotalRows,
        IReadOnlyList<ImportTableManifest> Tables);

    private sealed record ImportTableManifest(
        string Table,
        string File,
        IReadOnlyList<string> KeyColumns,
        int MinimumRows);
}
