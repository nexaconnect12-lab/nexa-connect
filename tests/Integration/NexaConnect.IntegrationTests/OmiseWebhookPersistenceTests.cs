extern alias PAYMENT;
using Microsoft.Extensions.Options;
using Npgsql;
using App = PAYMENT::NexaConnect.Services.Payment.Application.Webhooks;
using Intents = PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using Infra = PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using Hooks = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Webhooks;
using Providers = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.IntegrationTests;

public sealed class OmiseWebhookDatabaseFactAttribute : FactAttribute
{
    public OmiseWebhookDatabaseFactAttribute()
    {
        string? value=Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_WEBHOOK_INTEGRATION_DB");
        if (string.IsNullOrWhiteSpace(value) || Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") is not ("Development" or "Testing"))
        { Skip="Requires an explicitly configured local test database."; return; }
        var builder=new NpgsqlConnectionStringBuilder(value);
        if (builder.Host is not ("127.0.0.1" or "localhost") || builder.Database is not ("postgres" or "NexaConnect_Payment"))
            Skip="Requires a loopback local test database.";
    }
}

public sealed class OmiseWebhookPersistenceTests : IAsyncLifetime
{
    private NpgsqlDataSource? source;
    private string? schema;
    private Hooks.PostgresOmiseWebhookInbox Inbox => new(source!);
    private static string EventId()=>"evnt_test_"+Guid.NewGuid().ToString("N");
    public async Task InitializeAsync()
    {
        string? connection=Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_WEBHOOK_INTEGRATION_DB");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder=new NpgsqlConnectionStringBuilder(connection);
        if (Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") is not ("Development" or "Testing")
            || builder.Host is not ("127.0.0.1" or "localhost") || builder.Database is not ("postgres" or "NexaConnect_Payment")) return;
        schema="omise_webhook_it_"+Guid.NewGuid().ToString("N");
        builder.SearchPath=schema; source=NpgsqlDataSource.Create(builder.ConnectionString);
        await using var con=await source.OpenConnectionAsync();
        await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",con)) await create.ExecuteNonQueryAsync();
        string root=Root();
        foreach(var directory in Directory.GetDirectories(Path.Combine(root,"src/Tools/NexaConnect.DataMigration/Scripts/Payment")).Order())
        {
            await using var command=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")),con);
            await command.ExecuteNonQueryAsync();
        }
    }
    public async Task DisposeAsync()
    {
        if(source is null || schema is null) return;
        await using var con=await source.OpenConnectionAsync();
        await using var drop=new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",con);
        await drop.ExecuteNonQueryAsync(); await source.DisposeAsync();
    }
    private static string Root()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"NexaConnect.sln"))) dir=dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository missing.");
    }
    private async Task Expire(string eventId)
    {
        await using var command=source!.CreateCommand("UPDATE omise_webhook_inbox SET locked_until_utc=now()-interval '1 second' WHERE event_id=$1");
        command.Parameters.AddWithValue(eventId); await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Status(string eventId)
    {
        await using var command=source!.CreateCommand("SELECT status FROM omise_webhook_inbox WHERE event_id=$1");
        command.Parameters.AddWithValue(eventId); return (string)(await command.ExecuteScalarAsync())!;
    }
    [OmiseWebhookDatabaseFact]
    public async Task Duplicate_delivery_is_claimed_once_and_completed_delivery_is_not_requeued()
    {
        string id=EventId(); Guid correlation=Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Inbox.EnqueueAsync(id,correlation,default,"checkout.webhook:test")));
        var claims=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default)));
        var claim=Assert.Single(claims,c=>c is not null)!;
        Assert.Equal(correlation,claim.CorrelationId);
        Assert.Equal("checkout.webhook:test",claim.TraceCorrelationId);
        await Inbox.FinishAsync(claim,"completed",TimeSpan.FromSeconds(30),20,default);
        await Inbox.EnqueueAsync(id,Guid.NewGuid(),default);
        Assert.Null(await Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default));
        Assert.Equal("completed",await Status(id));
    }
    [OmiseWebhookDatabaseFact]
    public async Task Expired_claim_is_recoverable_and_stale_owner_cannot_complete_new_claim()
    {
        string id=EventId(); await Inbox.EnqueueAsync(id,Guid.NewGuid(),default);
        var old=(await Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default))!;
        await Expire(id); var current=(await Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default))!;
        Assert.NotEqual(old.Fence,current.Fence); Assert.Equal(2,current.Attempts);
        await Inbox.FinishAsync(old,"rejected",TimeSpan.FromSeconds(30),20,default);
        Assert.Equal("processing",await Status(id));
        await Inbox.FinishAsync(current,"completed",TimeSpan.FromSeconds(30),20,default);
        Assert.Equal("completed",await Status(id));
    }
    [OmiseWebhookDatabaseFact]
    public async Task Exhausted_delivery_remains_evidence_and_prevents_destructive_downgrade()
    {
        string id=EventId(); await Inbox.EnqueueAsync(id,Guid.NewGuid(),default);
        var claim=(await Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default))!;
        await Inbox.FinishAsync(claim,"retry",TimeSpan.FromSeconds(30),1,default);
        Assert.Equal("exhausted",await Status(id)); Assert.Null(await Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default));
        await using var down=source!.CreateCommand(await File.ReadAllTextAsync(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/Payment/0008_omise_webhook_inbox/down.sql")));
        await Assert.ThrowsAsync<PostgresException>(async()=>await down.ExecuteNonQueryAsync());
    }
    private sealed class Verifier(App.VerifiedOmiseEvent message) : App.IOmiseEventVerifier
    { public Task<App.OmiseEventLookup> VerifyAsync(string id,CancellationToken token)=>Task.FromResult(new App.OmiseEventLookup(message,false)); }
    private sealed class Provider : Providers.IPaymentProvider
    {
        public int Reads;
        public Task<Providers.ProviderAuthorizationResult> AuthorizeAsync(Intents.PaymentIntent intent,CancellationToken token)=>throw new InvalidOperationException("No financial commands allowed.");
        public Task<Providers.ProviderCaptureResult> GetCaptureStatusAsync(Intents.PaymentIntent intent,CancellationToken token)
        { Reads++; return Task.FromResult(new Providers.ProviderCaptureResult(Providers.ProviderCaptureOutcome.Captured,intent.ProviderAuthorizationId,null)); }
    }
    [OmiseWebhookDatabaseFact]
    public async Task Crash_after_financial_commit_replays_inbox_without_duplicate_transition_or_payment_command()
    {
        var intents=new Infra.PostgresPaymentIntents(source!,Options.Create(new Providers.PaymentProviderOptions()));
        Guid org=Guid.NewGuid(); var context=new Intents.PaymentMutationContext("test",Guid.NewGuid());
        var intent=intents.Create(org,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"crash-fixture",50,"THB","card"),context);
        string charge="chrg_test_"+Guid.NewGuid().ToString("N");
        var authorization=intents.BeginAuthorization(org,intent.Id,context);
        intents.CompleteAuthorization(org,intent.Id,authorization.Intent.ConcurrencyVersion,true,charge,null,context);
        var capture=intents.BeginCapture(org,intent.Id,context);
        intents.CompleteCapture(org,intent.Id,capture.Intent.ConcurrencyVersion,Providers.ProviderCaptureOutcome.Unknown,null,"provider_timeout",context);
        var provider=new Provider();
        var recovery=new App.WebhookPaymentRecovery(intents,new(intents,provider),new(intents,provider),new(intents,provider));
        var processor=new App.OmiseWebhookProcessor(new Verifier(new(org,intent.Id,intent.OrderId,charge,5000,"THB")),intents,recovery);
        string id=EventId(); await Inbox.EnqueueAsync(id,context.CorrelationId,default);
        var first=(await Inbox.ClaimAsync(TimeSpan.FromMinutes(5),default))!;
        Assert.Equal("completed",await processor.ProcessAsync(first,default));
        long version=intents.Get(org,intent.Id)!.ConcurrencyVersion;
        // Simulate loss of the worker after Payment/outbox commit, before inbox acknowledgement.
        await Expire(id); var resumed=(await new Hooks.PostgresOmiseWebhookInbox(source!).ClaimAsync(TimeSpan.FromMinutes(5),default))!;
        Assert.Equal("completed",await processor.ProcessAsync(resumed,default));
        await Inbox.FinishAsync(resumed,"completed",TimeSpan.FromSeconds(30),20,default);
        Assert.Equal(1,provider.Reads); Assert.Equal(version,intents.Get(org,intent.Id)!.ConcurrencyVersion);
        await using var count=source!.CreateCommand("SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.capture-reconciled.v1'");
        count.Parameters.AddWithValue(intent.Id); Assert.Equal(1L,await count.ExecuteScalarAsync());
    }
}
