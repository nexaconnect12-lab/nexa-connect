using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace NexaConnect.IntegrationTests;

// Fixture administration is confined to the runner's disposable project. Never used by a service.
internal sealed class CashCloseRestrictedCredentials : IAsyncDisposable
{
    private readonly NpgsqlDataSource admin;
    private readonly HttpClient management;
    private bool roleCreated;
    public string Actor { get; } = "cashclose_replay_" + Guid.NewGuid().ToString("N");
    public string Database { get; private set; } = "";
    public string Broker { get; private set; } = "";
    private CashCloseRestrictedCredentials(NpgsqlDataSource admin, HttpClient management)
    { this.admin = admin; this.management = management; }

    public static async Task<CashCloseRestrictedCredentials> CreateAsync(NpgsqlDataSource admin, string database, string schema, string broker, string exchange)
    {
        if (Environment.GetEnvironmentVariable("NEXACONNECT_CASH_CLOSE_ACCEPTANCE") != "1" ||
            Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") != "Testing" ||
            !Regex.IsMatch(schema, "^cashclose_src_[a-f0-9]{32}$")) throw new InvalidOperationException("Disposable fixture required.");
        var brokerUri = new Uri(broker);
        var credentials = brokerUri.UserInfo.Split(':', 2).Select(Uri.UnescapeDataString).ToArray();
        var endpoint = new Uri(Environment.GetEnvironmentVariable("NEXACONNECT_CASH_MANAGEMENT_URI")!);
        if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1") throw new InvalidOperationException("Loopback fixture required.");
        var client = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(':', credentials))));
        var result = new CashCloseRestrictedCredentials(admin, client);
        string password = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        using var quoting = new NpgsqlCommandBuilder();
        string role = quoting.QuoteIdentifier(result.Actor), ownerSchema = quoting.QuoteIdentifier(schema);
        try
        {
            await using var connection = await admin.OpenConnectionAsync();
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                // Utility DDL cannot bind PASSWORD directly. Bind values to transaction-local settings,
                // then let PostgreSQL format identifiers/literals; never interpolate a password into SQL.
                await using var settings = new NpgsqlCommand("SELECT set_config('cashclose_test.actor',$1,true),set_config('cashclose_test.password',$2,true)", connection, transaction);
                settings.Parameters.AddWithValue(result.Actor); settings.Parameters.AddWithValue(password);
                await settings.ExecuteNonQueryAsync();
                await using var create = new NpgsqlCommand("""
                    DO $$ BEGIN
                      EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT',
                        current_setting('cashclose_test.actor'),current_setting('cashclose_test.password'));
                    END; $$;
                    """, connection, transaction);
                await create.ExecuteNonQueryAsync();
                await using var grant = new NpgsqlCommand($"GRANT USAGE ON SCHEMA {ownerSchema} TO {role}; GRANT SELECT ON {ownerSchema}.outbox_messages TO {role}; GRANT INSERT ON {ownerSchema}.cash_close_replay_runs,{ownerSchema}.cash_close_replay_attempts TO {role};", connection, transaction);
                await grant.ExecuteNonQueryAsync();
                await transaction.CommitAsync(); result.roleCreated = true;
            }
            using (var response = await client.PutAsJsonAsync($"api/users/{result.Actor}", new { password, tags = "" })) response.EnsureSuccessStatusCode();
            string permitted = "^" + Regex.Escape(exchange) + "$";
            using (var response = await client.PutAsJsonAsync($"api/permissions/%2F/{result.Actor}", new { configure = permitted, write = permitted, read = "^$" })) response.EnsureSuccessStatusCode();
            result.Database = new NpgsqlConnectionStringBuilder(database) { Username = result.Actor, Password = password, Pooling = false }.ConnectionString;
            result.Broker = new UriBuilder(brokerUri) { UserName = result.Actor, Password = password }.Uri.AbsoluteUri;
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }

    public async Task VerifyDeniedOperationsAsync(string exchange, string queue)
    {
        await using var restricted = NpgsqlDataSource.Create(Database);
        foreach (string sql in new[]
        {
            "UPDATE outbox_messages SET published_at_utc=NULL WHERE false",
            "DELETE FROM cash_close_publications WHERE false",
            "SELECT id FROM cash_sessions LIMIT 0",
            "SELECT id FROM cash_close_replay_runs LIMIT 0",
            "DELETE FROM cash_close_replay_attempts WHERE false",
            "CREATE TABLE forbidden_replay_table(id integer)"
        })
        {
            await using var command = restricted.CreateCommand(sql);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await using var connection = await new ConnectionFactory { Uri = new Uri(Broker) }.CreateConnectionAsync();
        await using (var channel = await connection.CreateChannelAsync())
        {
            var denied = await Assert.ThrowsAsync<OperationInterruptedException>(() => channel.QueueDeclareAsync(queue + ".forbidden", true, false, false));
            Assert.Equal(403, (int)(denied.ShutdownReason?.ReplyCode ?? 0));
        }
        await using (var channel = await connection.CreateChannelAsync())
        {
            var denied = await Assert.ThrowsAsync<OperationInterruptedException>(() => channel.BasicGetAsync(queue, true));
            Assert.Equal(403, (int)(denied.ShutdownReason?.ReplyCode ?? 0));
        }
        await using (var channel = await connection.CreateChannelAsync())
        {
            var denied = await Assert.ThrowsAsync<OperationInterruptedException>(() => channel.ExchangeDeclareAsync(exchange + ".forbidden", ExchangeType.Topic, true));
            Assert.Equal(403, (int)(denied.ShutdownReason?.ReplyCode ?? 0));
        }
        await using (var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true)))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            // The default exchange exists but is outside the replay identity's exact write permission.
            await Assert.ThrowsAnyAsync<Exception>(() => channel.BasicPublishAsync("", queue, true,
                new BasicProperties(), Encoding.UTF8.GetBytes("{}"), timeout.Token).AsTask());
            Assert.Equal(403, (int)(channel.CloseReason?.ReplyCode ?? 0));
        }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            using var response = await management.DeleteAsync($"api/users/{Actor}");
            if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
        }
        finally
        {
            management.Dispose();
            if (roleCreated)
            {
                using var quoting = new NpgsqlCommandBuilder();
                string role = quoting.QuoteIdentifier(Actor);
                await using var command = admin.CreateCommand($"DROP OWNED BY {role}; DROP ROLE {role};");
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
