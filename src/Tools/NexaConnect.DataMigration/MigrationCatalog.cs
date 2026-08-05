using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal enum DowngradeSafety
{
    Safe,
    Transformative,
    Destructive,
    Unsupported
}

internal enum MigrationDirection
{
    Up,
    Down
}

internal sealed record MigrationMetadata(
    int Version,
    string Name,
    bool Transactional,
    string DowngradeSafety,
    string MinimumApplicationVersion);

internal sealed record MigrationDefinition(
    int Version,
    ValidatedMigrationMetadata Metadata,
    string MetadataPath,
    string UpPath,
    string DownPath,
    string MetadataChecksum,
    string UpChecksum,
    string DownChecksum);

internal sealed record ValidatedMigrationMetadata(
    string Name,
    bool Transactional,
    DowngradeSafety DowngradeSafety,
    string MinimumApplicationVersion);

internal sealed record AppliedMigration(
    int Version,
    string Name,
    string MetadataChecksum,
    string UpChecksum,
    string DownChecksum,
    DowngradeSafety DowngradeSafety,
    DateTimeOffset AppliedAtUtc,
    string ApplicationVersion,
    Guid ExecutionId);

internal sealed record MigrationStep(
    MigrationDirection Direction,
    MigrationDefinition Migration);

internal sealed class MigrationCatalog
{
    private static readonly Regex DirectoryPattern = new(
        "^(?<version>[0-9]{4})_(?<name>[a-z0-9][a-z0-9_]*)$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private MigrationCatalog(string service, IReadOnlyList<MigrationDefinition> migrations)
    {
        Service = service;
        Migrations = migrations;
    }

    public string Service { get; }

    public IReadOnlyList<MigrationDefinition> Migrations { get; }

    public int LatestVersion => Migrations.Count;

    public static async Task<MigrationCatalog> LoadAsync(
        string scriptsRoot,
        string requestedService,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(scriptsRoot))
        {
            throw new MigrationException($"Scripts root does not exist: {scriptsRoot}");
        }

        string? serviceDirectory = Directory.EnumerateDirectories(scriptsRoot)
            .SingleOrDefault(path => string.Equals(
                Path.GetFileName(path),
                requestedService,
                StringComparison.OrdinalIgnoreCase));

        if (serviceDirectory is null)
        {
            string available = string.Join(
                ", ",
                Directory.EnumerateDirectories(scriptsRoot)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal));
            throw new MigrationException(
                $"Unknown service '{requestedService}'. Available services: {available}");
        }

        string service = Path.GetFileName(serviceDirectory);
        var migrations = new List<MigrationDefinition>();

        foreach (string directory in Directory.EnumerateDirectories(serviceDirectory)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directoryName = Path.GetFileName(directory);
            Match match = DirectoryPattern.Match(directoryName);

            if (!match.Success)
            {
                throw new MigrationException(
                    $"Invalid migration directory '{directoryName}' for {service}. " +
                    "Expected <four-digit-version>_<name>.");
            }

            int folderVersion = int.Parse(match.Groups["version"].Value);
            string folderName = match.Groups["name"].Value;
            string metadataPath = RequireFile(directory, "migration.json");
            string upPath = RequireFile(directory, "up.sql");
            string downPath = RequireFile(directory, "down.sql");

            MigrationMetadata metadata;
            try
            {
                string json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                metadata = JsonSerializer.Deserialize<MigrationMetadata>(json, JsonOptions)
                    ?? throw new JsonException("Migration metadata is empty.");
            }
            catch (JsonException exception)
            {
                throw new MigrationException(
                    $"Invalid migration metadata in {metadataPath}: {exception.Message}");
            }

            if (metadata.Version != folderVersion ||
                !string.Equals(metadata.Name, folderName, StringComparison.Ordinal))
            {
                throw new MigrationException(
                    $"Migration metadata does not match directory {directoryName}.");
            }

            if (!Enum.TryParse(metadata.DowngradeSafety, true, out DowngradeSafety downgradeSafety))
            {
                throw new MigrationException(
                    $"Invalid downgradeSafety '{metadata.DowngradeSafety}' in {metadataPath}.");
            }

            if (string.IsNullOrWhiteSpace(metadata.MinimumApplicationVersion))
            {
                throw new MigrationException(
                    $"minimumApplicationVersion is required in {metadataPath}.");
            }

            migrations.Add(new MigrationDefinition(
                metadata.Version,
                new ValidatedMigrationMetadata(
                    metadata.Name,
                    metadata.Transactional,
                    downgradeSafety,
                    metadata.MinimumApplicationVersion),
                metadataPath,
                upPath,
                downPath,
                await ComputeChecksumAsync(metadataPath, cancellationToken),
                await ComputeChecksumAsync(upPath, cancellationToken),
                await ComputeChecksumAsync(downPath, cancellationToken)));
        }

        if (migrations.Count == 0)
        {
            throw new MigrationException(
                $"No versioned migrations found for {service} in {serviceDirectory}.");
        }

        for (int index = 0; index < migrations.Count; index++)
        {
            int expectedVersion = index + 1;
            if (migrations[index].Version != expectedVersion)
            {
                throw new MigrationException(
                    $"Migration sequence for {service} is not linear. " +
                    $"Expected version {expectedVersion}, found {migrations[index].Version}.");
            }
        }

        return new MigrationCatalog(service, migrations);
    }

    public void ValidateAppliedMigrations(IReadOnlyList<AppliedMigration> applied)
    {
        for (int index = 0; index < applied.Count; index++)
        {
            int expectedVersion = index + 1;
            AppliedMigration recorded = applied[index];

            if (recorded.Version != expectedVersion)
            {
                throw new MigrationException(
                    $"Database migration history is not linear. " +
                    $"Expected version {expectedVersion}, found {recorded.Version}.");
            }

            if (recorded.Version > LatestVersion)
            {
                throw new MigrationException(
                    $"Database contains migration {recorded.Version}, but the latest local version is {LatestVersion}.");
            }

            MigrationDefinition local = Migrations[recorded.Version - 1];
            if (!string.Equals(recorded.Name, local.Metadata.Name, StringComparison.Ordinal) ||
                !string.Equals(recorded.MetadataChecksum, local.MetadataChecksum, StringComparison.Ordinal) ||
                !string.Equals(recorded.UpChecksum, local.UpChecksum, StringComparison.Ordinal) ||
                !string.Equals(recorded.DownChecksum, local.DownChecksum, StringComparison.Ordinal) ||
                recorded.DowngradeSafety != local.Metadata.DowngradeSafety)
            {
                throw new MigrationException(
                    $"Applied migration {recorded.Version:D4}_{recorded.Name} differs from the local immutable files.");
            }
        }
    }

    public IReadOnlyList<MigrationStep> CreatePlan(
        IReadOnlyList<AppliedMigration> applied,
        int targetVersion)
    {
        if (targetVersion < 0 || targetVersion > LatestVersion)
        {
            throw new MigrationException(
                $"Target version {targetVersion} is outside the available range 0..{LatestVersion}.");
        }

        int currentVersion = applied.Count;
        if (targetVersion > currentVersion)
        {
            return Migrations
                .Where(migration => migration.Version > currentVersion && migration.Version <= targetVersion)
                .Select(migration => new MigrationStep(MigrationDirection.Up, migration))
                .ToArray();
        }

        if (targetVersion < currentVersion)
        {
            return Migrations
                .Where(migration => migration.Version > targetVersion && migration.Version <= currentVersion)
                .OrderByDescending(migration => migration.Version)
                .Select(migration => new MigrationStep(MigrationDirection.Down, migration))
                .ToArray();
        }

        return [];
    }

    private static string RequireFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        return File.Exists(path)
            ? path
            : throw new MigrationException($"Missing {fileName} in {directory}.");
    }

    private static async Task<string> ComputeChecksumAsync(
        string path,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
