internal enum DataGenerationCommand
{
    Plan,
    Confirm
}

internal sealed record DataGenerationOptions(
    string? Service,
    bool AllServices,
    string SeedsRoot,
    string? EnvironmentFile,
    DataGenerationCommand Command,
    string? ImportPackage)
{
    public static DataGenerationOptions? Parse(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string? service = null;
        bool allServices = false;
        string seedsRoot = Path.Combine(AppContext.BaseDirectory, "Seeds");
        string? environmentFile = null;
        string? importPackage = null;
        DataGenerationCommand? command = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--service" when index + 1 < args.Length:
                    service = args[++index];
                    break;
                case "--all":
                    allServices = true;
                    break;
                case "--seeds-root" when index + 1 < args.Length:
                    seedsRoot = Path.GetFullPath(args[++index]);
                    break;
                case "--environment-file" when index + 1 < args.Length:
                    environmentFile = Path.GetFullPath(args[++index]);
                    break;
                case "--import-package" when index + 1 < args.Length:
                    importPackage = Path.GetFullPath(args[++index]);
                    break;
                case "--plan":
                case "--dry-run":
                    command = SetCommand(command, DataGenerationCommand.Plan);
                    break;
                case "--confirm":
                    command = SetCommand(command, DataGenerationCommand.Confirm);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(service) == !allServices)
        {
            throw new ArgumentException("Specify exactly one of --service <name> or --all.");
        }

        if (command is null)
        {
            throw new ArgumentException("Specify exactly one of --plan or --confirm.");
        }

        return new DataGenerationOptions(
            service,
            allServices,
            seedsRoot,
            environmentFile,
            command.Value,
            importPackage);
    }

    private static DataGenerationCommand SetCommand(
        DataGenerationCommand? current,
        DataGenerationCommand next)
    {
        if (current is not null)
        {
            throw new ArgumentException("Specify exactly one of --plan or --confirm.");
        }

        return next;
    }
}

internal static class EnvironmentFile
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new DataGenerationException($"Environment file does not exist: {path}");
        }

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new DataGenerationException($"Invalid environment-file entry: {line}");
            }

            string name = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}

internal sealed class DataGenerationException(string message) : Exception(message);
