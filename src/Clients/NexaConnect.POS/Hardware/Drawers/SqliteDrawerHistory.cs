using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace NexaConnect.POS.Hardware.Drawers;

public sealed partial class SqliteDrawerIntentStore
{
    private void SaveContext(SqliteConnection connection,SqliteTransaction transaction,DrawerRequest request,DateTimeOffset reservedAt)
    {
        // Legacy constructors retain hash-only deduplication. Production supplies issuer;
        // recovery metadata is protected before the reservation can commit.
        if(string.IsNullOrWhiteSpace(authority))return;
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new DrawerProtectedContext(1,authority,request,reservedAt));
        if(bytes.Length>8192)throw new InvalidDataException();
        var protectedBytes=(protector??new WindowsDrawerHistoryProtector()).Protect(bytes);
        if(protectedBytes.Length is <1 or >16384)throw new InvalidDataException();
        using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="CREATE TABLE IF NOT EXISTS drawer_history_context(intent_key TEXT PRIMARY KEY REFERENCES drawer_intents(intent_key),actor_scope_hash TEXT NOT NULL,protected_context BLOB NOT NULL);CREATE INDEX IF NOT EXISTS ix_drawer_history_actor_scope ON drawer_history_context(actor_scope_hash);";command.ExecuteNonQuery();
        command.CommandText="INSERT INTO drawer_history_context(intent_key,actor_scope_hash,protected_context) VALUES($key,$scope,$bytes);";
        command.Parameters.AddWithValue("$key",request.Key);command.Parameters.AddWithValue("$scope",DrawerHistoryIdentity.Hash(authority,request.OrganizationId,request.RestaurantId,request.BranchId,request.StoreId,request.TerminalId,request.Subject));command.Parameters.AddWithValue("$bytes",protectedBytes);command.ExecuteNonQuery();
    }
    public IReadOnlyList<DrawerLocalHistoryEntry> ReadHistory(PosClientConfiguration c,string subject,DateTimeOffset from,DateTimeOffset to,int limit=50)
    {
        c.ValidateCheckout();if(string.IsNullOrWhiteSpace(subject)||subject.Length>200||subject.Any(char.IsControl)||from>=to||to-from>TimeSpan.FromDays(31)||limit is <1 or >50||authority!=c.Authority)throw new ArgumentException();
        string path=Path.GetFullPath(file);PosInstallationPolicy.AssertNoLinks(path);if(!File.Exists(path))return [];
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=5}.ToString());connection.Open();
        using var command=connection.CreateCommand();command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='drawer_history_context';";if(Convert.ToInt32(command.ExecuteScalar())==0)return [];
        command.CommandText="SELECT d.intent_key,d.fingerprint,d.outcome,d.reserved_utc,CASE WHEN length(h.protected_context)<=16384 THEN h.protected_context ELSE NULL END FROM drawer_history_context h JOIN drawer_intents d USING(intent_key) WHERE h.actor_scope_hash=$scope AND d.reserved_utc>=$from AND d.reserved_utc<$to ORDER BY d.reserved_utc DESC,d.intent_key DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$scope",DrawerHistoryIdentity.Hash(c.Authority,c.OrganizationId,c.RestaurantId,c.BranchId,c.StoreId,c.TerminalId,subject));command.Parameters.AddWithValue("$from",from.ToUniversalTime().ToString("O"));command.Parameters.AddWithValue("$to",to.ToUniversalTime().ToString("O"));command.Parameters.AddWithValue("$limit",limit);
        var rows=new List<DrawerLocalHistoryEntry>();using var reader=command.ExecuteReader();
        while(reader.Read()){
            if(reader.IsDBNull(4))throw new InvalidDataException();
            var bytes=(protector??new WindowsDrawerHistoryProtector()).Unprotect(reader.GetFieldValue<byte[]>(4));if(bytes.Length>8192)throw new InvalidDataException();
            var context=JsonSerializer.Deserialize<DrawerProtectedContext>(bytes)??throw new InvalidDataException();var r=context.Request??throw new InvalidDataException();r.Validate();
            if(context.Version!=1||context.Authority!=c.Authority||r.Subject!=subject||r.OrganizationId!=c.OrganizationId||r.RestaurantId!=c.RestaurantId||r.BranchId!=c.BranchId||r.StoreId!=c.StoreId||r.TerminalId!=c.TerminalId||r.Key!=reader.GetString(0)||r.Fingerprint!=reader.GetString(1)
                ||context.ReservedAtUtc<from||context.ReservedAtUtc>=to||context.ReservedAtUtc!=DateTimeOffset.Parse(reader.GetString(3))||reader.GetString(2) is not ("Reserved" or "Submitted" or "Unknown" or "Cancelled"))throw new InvalidDataException();
            rows.Add(new(r,context.ReservedAtUtc,reader.GetString(2)));
        }
        return rows;
    }
}
