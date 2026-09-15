using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexaConnect.PosLocalStateInspector;

public sealed record LocalStateInspectionResult(
    bool IntegrityOk,
    int SchemaVersion,
    int OperationalStateCount,
    int PendingCashReviewCount,
    int UnresolvedOutboxCount,
    int InterruptedSendCount);

public static class LocalStateInspection
{
    public static LocalStateInspectionResult Read(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using (SqliteCommand queryOnly = connection.CreateCommand())
        {
            queryOnly.CommandText = "PRAGMA query_only=ON;";
            queryOnly.ExecuteNonQuery();
        }

        string integrity = ScalarText(connection, "PRAGMA quick_check;");
        return new LocalStateInspectionResult(
            string.Equals(integrity, "ok", StringComparison.Ordinal),
            ScalarInt(connection, "PRAGMA user_version;"),
            ScalarInt(connection, """
                SELECT count(*) FROM local_state
                WHERE state_key IN ('active-shift','cash-session','pending-checkout','pending-settlement','pending-cash-review');
                """),
            ScalarInt(connection, "SELECT count(*) FROM local_state WHERE state_key='pending-cash-review';"),
            ScalarInt(connection, "SELECT count(*) FROM outbox_operations WHERE state <> 'completed';"),
            ScalarInt(connection, "SELECT count(*) FROM outbox_operations WHERE state = 'sending';"));
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
