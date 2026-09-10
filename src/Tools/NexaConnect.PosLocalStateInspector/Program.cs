using Microsoft.Data.Sqlite;
using NexaConnect.PosLocalStateInspector;

if (args.Length != 2 || !string.Equals(args[0], "--database", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: NexaConnect.PosLocalStateInspector --database <absolute-path>");
    return 2;
}

string databasePath = Path.GetFullPath(args[1]);
if (!Path.IsPathFullyQualified(databasePath) || !File.Exists(databasePath))
{
    Console.Error.WriteLine("The local POS database does not exist.");
    return 3;
}

try
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        LocalStateInspection.Read(databasePath),
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    return 0;
}
catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("The local POS database could not be inspected safely.");
    return 4;
}
