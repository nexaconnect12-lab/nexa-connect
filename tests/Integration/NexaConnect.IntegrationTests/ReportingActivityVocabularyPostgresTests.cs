extern alias REPORTING;

using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using ReportingActivityCommand = REPORTING::NexaConnect.Services.Reporting.Application.ProjectAuditActivityCommand;
using ReportingActivityRepository = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresActivityProjectionRepository;

namespace NexaConnect.IntegrationTests;

public sealed class ReportingActivityVocabularyPostgresTests : IAsyncLifetime
{
    private readonly string? connectionString=Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [ReportingDatabaseFact]
    public async Task Migration_4_accepts_payment_audit_and_downgrade_removes_incompatible_projection()
    {
        string migration=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts","Reporting","0004_activity_vocabulary");
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(connection,Path.Combine(migration,"up.sql"));
        var repository=new ReportingActivityRepository(dataSource!);
        var audit=new PlatformAuditEventV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,"payment-service",Guid.NewGuid(),"payment.intent.created","payment-intent",Guid.NewGuid().ToString("D"),"succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit,"nexa_connect","payment"),CancellationToken.None));
        await using(var inbox=new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())",connection)){inbox.Parameters.AddWithValue(audit.EventId);await inbox.ExecuteNonQueryAsync();}
        await ExecuteAsync(connection,Path.Combine(migration,"down.sql"));
        Assert.Equal(0L,(long)(await new NpgsqlCommand("SELECT count(*) FROM activity_records",connection).ExecuteScalarAsync()??0L));
        Assert.Equal(0L,(long)(await new NpgsqlCommand("SELECT count(*) FROM inbox_messages",connection).ExecuteScalarAsync()??0L));
        await ExecuteAsync(connection,Path.Combine(migration,"up.sql"));
        var inboxStore=new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed,await inboxStore.ClaimAsync(audit.EventId,"reporting.activity.v1",TimeSpan.FromMinutes(2),CancellationToken.None));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit,"nexa_connect","payment"),CancellationToken.None));
        await inboxStore.MarkCompletedAsync(audit.EventId,"reporting.activity.v1",CancellationToken.None);
    }

    [ReportingDatabaseFact]
    public async Task Migration_5_accepts_kitchen_audit_and_replays_after_re_upgrade()
    {
        string root=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts","Reporting");
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();await ExecuteAsync(connection,Path.Combine(root,"0004_activity_vocabulary","up.sql"));string migration=Path.Combine(root,"0005_kitchen_activity_vocabulary");await ExecuteAsync(connection,Path.Combine(migration,"up.sql"));var repository=new ReportingActivityRepository(dataSource!);var audit=new PlatformAuditEventV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,"kitchen-operator",Guid.NewGuid(),"kitchen.ticket.ready","kitchen-ticket",Guid.NewGuid().ToString("D"),"succeeded");Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit,"nexa_connect","kitchen"),default));await using(var inbox=new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())",connection)){inbox.Parameters.AddWithValue(audit.EventId);await inbox.ExecuteNonQueryAsync();}await ExecuteAsync(connection,Path.Combine(migration,"down.sql"));Assert.Equal(0L,(long)(await new NpgsqlCommand("SELECT count(*) FROM activity_records",connection).ExecuteScalarAsync()??0L));await ExecuteAsync(connection,Path.Combine(migration,"up.sql"));var store=new PostgresInboxStore(dataSource!);Assert.Equal(InboxClaimResult.Claimed,await store.ClaimAsync(audit.EventId,"reporting.activity.v1",TimeSpan.FromMinutes(2),default));Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit,"nexa_connect","kitchen"),default));
    }

    [ReportingDatabaseFact]
    public async Task Migration_6_accepts_customer_audit_and_replays_after_re_upgrade()
    {
        string root=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts","Reporting");
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(connection,Path.Combine(root,"0004_activity_vocabulary","up.sql"));
        await ExecuteAsync(connection,Path.Combine(root,"0005_kitchen_activity_vocabulary","up.sql"));
        string migration=Path.Combine(root,"0006_customer_activity_vocabulary");
        await ExecuteAsync(connection,Path.Combine(migration,"up.sql"));
        var repository=new ReportingActivityRepository(dataSource!);
        var audit=new PlatformAuditEventV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,"customer-user",Guid.NewGuid(),"customer.profile.created","customer-profile",Guid.NewGuid().ToString("D"),"succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit,"nexa_connect","customer"),default));
        await using(var inbox=new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())",connection)){inbox.Parameters.AddWithValue(audit.EventId);await inbox.ExecuteNonQueryAsync();}
        await ExecuteAsync(connection,Path.Combine(migration,"down.sql"));
        await using(var count=new NpgsqlCommand("SELECT count(*) FROM activity_records WHERE event_id=$1",connection)){count.Parameters.AddWithValue(audit.EventId);Assert.Equal(0L,(long)(await count.ExecuteScalarAsync()??0L));}
        await ExecuteAsync(connection,Path.Combine(migration,"up.sql"));
        var store=new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed,await store.ClaimAsync(audit.EventId,"reporting.activity.v1",TimeSpan.FromMinutes(2),default));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit,"nexa_connect","customer"),default));
    }

    public async Task InitializeAsync(){string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");if(string.IsNullOrWhiteSpace(connectionString)||environment is not ("Development" or "Test" or "Testing"))return;schema=$"reporting_vocabulary_it_{Guid.NewGuid():N}";var builder=new NpgsqlConnectionStringBuilder(connectionString){SearchPath=schema};dataSource=NpgsqlDataSource.Create(builder.ConnectionString);await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",connection).ExecuteNonQueryAsync();await new NpgsqlCommand(SchemaSql,connection).ExecuteNonQueryAsync();}
    public async Task DisposeAsync(){if(dataSource is null||schema is null)return;await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync();await dataSource.DisposeAsync();}
    private static async Task ExecuteAsync(NpgsqlConnection connection,string path){await using var command=new NpgsqlCommand(await File.ReadAllTextAsync(path),connection);await command.ExecuteNonQueryAsync();}
    private static string FindRepositoryRoot(){DirectoryInfo? directory=new(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"NexaConnect.sln")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException("Could not locate repository root.");}
    private const string SchemaSql="""
        CREATE TABLE activity_records(event_id uuid PRIMARY KEY,organization_id uuid NOT NULL,application_code text NOT NULL,source_service text NOT NULL,actor_subject_id text NOT NULL,action text NOT NULL,resource_type text NOT NULL,resource_id text NOT NULL,outcome text NOT NULL,occurred_at_utc timestamptz NOT NULL,projected_at_utc timestamptz NOT NULL,CONSTRAINT ck_activity_records_text CHECK(application_code='nexa_connect' AND char_length(source_service) BETWEEN 1 AND 64 AND char_length(actor_subject_id) BETWEEN 1 AND 200 AND char_length(resource_id) BETWEEN 1 AND 300),CONSTRAINT ck_activity_records_action CHECK(action IN('customer-membership.changed','branch.created','branch.updated','branch.configuration.updated','media.asset.created','media.asset.deleted')),CONSTRAINT ck_activity_records_resource CHECK(resource_type IN('organization-membership','branch','branch-configuration','media-asset')),CONSTRAINT ck_activity_records_outcome CHECK(outcome IN('succeeded','failed','denied')));
        CREATE TABLE inbox_messages(message_id uuid NOT NULL,consumer_name text NOT NULL,status text NOT NULL,attempts integer NOT NULL DEFAULT 0,locked_until_utc timestamptz NULL,processed_at_utc timestamptz NULL,last_error_category text NULL,PRIMARY KEY(message_id,consumer_name));
        """;
}

public sealed class ReportingDatabaseFactAttribute : FactAttribute
{
    public ReportingDatabaseFactAttribute(){string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB"))||environment is not ("Development" or "Test" or "Testing"))Skip="NEXACONNECT_REPORTING_INTEGRATION_DB and a safe environment are required.";}
}
