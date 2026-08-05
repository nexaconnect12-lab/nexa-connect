using System.Reflection;

internal enum MigrationCommand
{
    Status,
    Plan,
    Confirm
}

internal sealed record MigrationOptions(
    string Service,
    string ScriptsRoot,
    string? EnvironmentFile,
    MigrationCommand Command,
    int? TargetVersion,
    string ApplicationVersion,
    bool AllowTransformative,
    bool AllowDestructive,
    bool BackupVerified)
{
    public static MigrationOptions? Parse(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string? service = null;
        string scriptsRoot = Path.Combine(AppContext.BaseDirectory, "Scripts");
        string? environmentFile = null;
        MigrationCommand? command = null;
        int? targetVersion = null;
        string applicationVersion =
            Environment.GetEnvironmentVariable("NEXACONNECT_APPLICATION_VERSION") ??
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ??
            "0.0.0";
        bool allowTransformative = false;
        bool allowDestructive = false;
        bool backupVerified = false;

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
                case "--environment-file" when index + 1 < args.Length:
                    environmentFile = Path.GetFullPath(args[++index]);
                    break;
                case "--target" when index + 1 < args.Length &&
                    int.TryParse(args[index + 1], out int parsedTarget):
                    targetVersion = parsedTarget;
                    index++;
                    break;
                case "--application-version" when index + 1 < args.Length:
                    applicationVersion = args[++index];
                    break;
                case "--status":
                    command = SetCommand(command, MigrationCommand.Status);
                    break;
                case "--plan":
                case "--dry-run":
                    command = SetCommand(command, MigrationCommand.Plan);
                    break;
                case "--confirm":
                    command = SetCommand(command, MigrationCommand.Confirm);
                    break;
                case "--allow-transformative":
                    allowTransformative = true;
                    break;
                case "--allow-destructive":
                    allowDestructive = true;
                    break;
                case "--backup-verified":
                    backupVerified = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(service))
        {
            throw new ArgumentException("The --service argument is required.");
        }

        if (command is null)
        {
            throw new ArgumentException("Specify exactly one of --status, --plan, or --confirm.");
        }

        if (command == MigrationCommand.Status && targetVersion is not null)
        {
            throw new ArgumentException("--status cannot be combined with --target.");
        }

        if (command != MigrationCommand.Status && targetVersion is null)
        {
            throw new ArgumentException("The --target argument is required for --plan and --confirm.");
        }

        if (targetVersion < 0)
        {
            throw new ArgumentException("The --target version cannot be negative.");
        }

        return new MigrationOptions(
            service,
            scriptsRoot,
            environmentFile,
            command.Value,
            targetVersion,
            applicationVersion,
            allowTransformative,
            allowDestructive,
            backupVerified);
    }

    private static MigrationCommand SetCommand(MigrationCommand? current, MigrationCommand next)
    {
        if (current is not null)
        {
            throw new ArgumentException("Specify exactly one of --status, --plan, or --confirm.");
        }

        return next;
    }
}
