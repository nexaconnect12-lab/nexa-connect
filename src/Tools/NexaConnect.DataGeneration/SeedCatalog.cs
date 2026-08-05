using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal sealed record SeedDefinition(
    int Sequence,
    string Name,
    string Path,
    string Checksum,
    int RequiredSchemaVersion)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

internal sealed class SeedCatalog
{
    private static readonly Regex FilePattern = new(
        "^(?<sequence>[0-9]{4})_(?<name>[a-z0-9][a-z0-9_]*)[.]sql$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SchemaVersionPattern = new(
        "^--[ ]*requires-schema-version:[ ]*(?<version>[0-9]+)[ ]*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private SeedCatalog(string service, IReadOnlyList<SeedDefinition> seeds)
    {
        Service = service;
        Seeds = seeds;
    }

    public string Service { get; }

    public IReadOnlyList<SeedDefinition> Seeds { get; }

    public static async Task<SeedCatalog> LoadAsync(
        string seedsRoot,
        string requestedService,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(seedsRoot))
        {
            throw new DataGenerationException($"Seeds root does not exist: {seedsRoot}");
        }

        string? serviceDirectory = Directory.EnumerateDirectories(seedsRoot)
            .SingleOrDefault(path => string.Equals(
                System.IO.Path.GetFileName(path),
                requestedService,
                StringComparison.OrdinalIgnoreCase));

        if (serviceDirectory is null)
        {
            string available = string.Join(", ", Directory.EnumerateDirectories(seedsRoot)
                .Select(System.IO.Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal));
            throw new DataGenerationException(
                $"Unknown service '{requestedService}'. Available services: {available}");
        }

        string service = System.IO.Path.GetFileName(serviceDirectory);
        var seeds = new List<SeedDefinition>();

        foreach (string path in Directory.EnumerateFiles(serviceDirectory, "*.sql")
                     .OrderBy(System.IO.Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = System.IO.Path.GetFileName(path);
            Match fileMatch = FilePattern.Match(fileName);
            if (!fileMatch.Success)
            {
                throw new DataGenerationException(
                    $"Invalid seed file '{fileName}'. Expected <four-digit-sequence>_<name>.sql.");
            }

            string sql = await File.ReadAllTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new DataGenerationException($"Seed file is empty: {path}");
            }

            Match versionMatch = SchemaVersionPattern.Match(sql);
            if (!versionMatch.Success)
            {
                throw new DataGenerationException(
                    $"Seed {fileName} must declare '-- requires-schema-version: <number>'.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            seeds.Add(new SeedDefinition(
                int.Parse(fileMatch.Groups["sequence"].Value),
                fileMatch.Groups["name"].Value,
                path,
                Convert.ToHexString(SHA256.HashData(bytes)),
                int.Parse(versionMatch.Groups["version"].Value)));
        }

        if (seeds.Count == 0)
        {
            throw new DataGenerationException(
                $"No seed scripts found for {service} in {serviceDirectory}.");
        }

        for (int index = 0; index < seeds.Count; index++)
        {
            int expected = index + 1;
            if (seeds[index].Sequence != expected)
            {
                throw new DataGenerationException(
                    $"Seed sequence for {service} is not linear. " +
                    $"Expected {expected:D4}, found {seeds[index].Sequence:D4}.");
            }
        }

        return new SeedCatalog(service, seeds);
    }
}
