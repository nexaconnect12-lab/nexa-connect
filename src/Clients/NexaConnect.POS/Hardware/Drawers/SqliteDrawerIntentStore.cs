using System.IO;
using Microsoft.Data.Sqlite;

namespace NexaConnect.POS.Hardware.Drawers;

// Infrastructure: separate local intent evidence, not a financial ledger/server audit.
// No production construction or file creation occurs while UX8 is hardware gated.
public sealed class SqliteDrawerIntentStore(string file) : IDrawerIntentStore
{
    private SqliteConnection Open()
    {
        string path=Path.GetFullPath(file);PosInstallationPolicy.AssertNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,DefaultTimeout=5}.ToString());
        connection.Open();
        try
        {
            using var schema=connection.CreateCommand();
            schema.CommandText="CREATE TABLE IF NOT EXISTS drawer_intents (intent_key TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, cause TEXT NOT NULL, reason TEXT NULL, outcome TEXT NOT NULL, reserved_utc TEXT NOT NULL, completed_utc TEXT NULL);";
            schema.ExecuteNonQuery();return connection;
        }
        catch{connection.Dispose();throw;}
    }
    public DrawerReservation Reserve(DrawerRequest request)
    {
        request.Validate();using var connection=Open();
        using var transaction=connection.BeginTransaction();
        using var insert=connection.CreateCommand();insert.Transaction=transaction;
        insert.CommandText="INSERT INTO drawer_intents(intent_key,fingerprint,cause,reason,outcome,reserved_utc) VALUES($key,$fingerprint,$cause,$reason,'Reserved',$utc) ON CONFLICT(intent_key) DO NOTHING;";
        insert.Parameters.AddWithValue("$key",request.Key);insert.Parameters.AddWithValue("$fingerprint",request.Fingerprint);
        insert.Parameters.AddWithValue("$cause",request.Cause.ToString());insert.Parameters.AddWithValue("$reason",request.Reason is { } r?r.ToString():DBNull.Value);
        insert.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));
        if(insert.ExecuteNonQuery()==1){transaction.Commit();return DrawerReservation.Created;}
        using var read=connection.CreateCommand();read.Transaction=transaction;
        read.CommandText="SELECT fingerprint FROM drawer_intents WHERE intent_key=$key;";read.Parameters.AddWithValue("$key",request.Key);
        bool same=read.ExecuteScalar() as string==request.Fingerprint;transaction.Commit();
        return same?DrawerReservation.AlreadyAttempted:DrawerReservation.Conflict;
    }
    public void Complete(DrawerRequest request,DrawerOutcome outcome)
    {
        request.Validate();if(outcome is not (DrawerOutcome.Submitted or DrawerOutcome.Unknown or DrawerOutcome.Cancelled))throw new ArgumentException("Invalid attempt outcome.");
        using var connection=Open();using var update=connection.CreateCommand();
        update.CommandText="UPDATE drawer_intents SET outcome=$outcome,completed_utc=$utc WHERE intent_key=$key AND fingerprint=$fingerprint AND outcome='Reserved';";
        update.Parameters.AddWithValue("$outcome",outcome.ToString());update.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$key",request.Key);update.Parameters.AddWithValue("$fingerprint",request.Fingerprint);
        if(update.ExecuteNonQuery()!=1)throw new InvalidOperationException("Drawer intent is not reserved.");
    }
}
