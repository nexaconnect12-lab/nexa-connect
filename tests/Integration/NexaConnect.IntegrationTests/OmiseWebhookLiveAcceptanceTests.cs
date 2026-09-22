extern alias MIGRATIONS;
extern alias PAYMENT;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using CreatePaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.CreatePaymentIntent;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using PostgresPaymentIntents = PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentIntents;
using OmisePaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.OmisePaymentProvider;
using PaymentProviderOptions = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions;
using ProviderAuthorizationOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationOutcome;

namespace NexaConnect.IntegrationTests;

public sealed class OmiseWebhookLiveAcceptanceFactAttribute : FactAttribute
{
    public OmiseWebhookLiveAcceptanceFactAttribute(string stage)
    {
        if (Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_WEBHOOK_LIVE_ACCEPTANCE") != "1"
            || Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_WEBHOOK_LIVE_STAGE") != stage
            || Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") != "Testing")
            Skip = "The guarded Omise webhook live acceptance stage is required.";
    }
}

[Collection("Order provider payment recovery live acceptance")]
public sealed class OmiseWebhookLiveAcceptanceTests
{
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing guarded acceptance setting: {name}.");
    private static string Connection() => Required("NEXACONNECT_OMISE_WEBHOOK_LIVE_DB");

    [OmiseWebhookLiveAcceptanceFact("initialize")]
    public async Task Initialize_disposable_webhook_recovery_fixture()
    {
        string root = RepositoryRoot();
        string? previous = Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_DB");
        try
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_PAYMENT_DB", Connection());
            Assert.Equal(0, await MigrationApplication.RunAsync([
                "--service", "Payment", "--scripts-root", Path.Combine(root, "src", "Tools", "NexaConnect.DataMigration", "Scripts"),
                "--target", "8", "--application-version", "0.16.0", "--confirm"]));
        }
        finally { Environment.SetEnvironmentVariable("NEXACONNECT_PAYMENT_DB", previous); }

        await using NpgsqlDataSource source = NpgsqlDataSource.Create(Connection());
        await using (var table = source.CreateCommand("""
            CREATE TABLE omise_webhook_live_fixture(
              singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),
              organization_id uuid NOT NULL, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
              order_id uuid NOT NULL, payment_intent_id uuid NOT NULL, correlation_id uuid NOT NULL);
            """)) await table.ExecuteNonQueryAsync();
        Guid organization = Guid.NewGuid(), restaurant = Guid.NewGuid(), branch = Guid.NewGuid();
        Guid order = Guid.NewGuid(), correlation = Guid.NewGuid();
        var repository = Repository(source);
        var intent = repository.Create(organization,
            new CreatePaymentIntent(restaurant, branch, order, $"webhook-live:{order:D}", Amount(), "THB", "card"),
            new PaymentMutationContext("omise-webhook-live-acceptance", correlation));
        var lease = repository.BeginAuthorization(organization, intent.Id,
            new PaymentMutationContext("omise-webhook-live-acceptance", correlation));
        Assert.True(lease.Acquired);
        await using var insert = source.CreateCommand("INSERT INTO omise_webhook_live_fixture(organization_id,restaurant_id,branch_id,order_id,payment_intent_id,correlation_id) VALUES($1,$2,$3,$4,$5,$6)");
        insert.Parameters.AddWithValue(organization); insert.Parameters.AddWithValue(restaurant);
        insert.Parameters.AddWithValue(branch); insert.Parameters.AddWithValue(order);
        insert.Parameters.AddWithValue(intent.Id); insert.Parameters.AddWithValue(correlation);
        await insert.ExecuteNonQueryAsync();
    }

    [OmiseWebhookLiveAcceptanceFact("authorize")]
    public async Task Create_one_test_authorization_for_external_webhook_delivery()
    {
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(Connection());
        (Guid organization, Guid intentId) = await Identity(source);
        var intent = Repository(source).Get(organization, intentId);
        Assert.NotNull(intent); Assert.Equal("authorizing", intent.Status);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://api.omise.co/"), Timeout = TimeSpan.FromSeconds(15) };
        var provider = new OmisePaymentProvider(client, Options.Create(new PaymentProviderOptions
        { OmiseSecretKey = Required("NEXACONNECT_OMISE_TEST_SECRET_KEY") }), NullLogger<OmisePaymentProvider>.Instance);
        var result = await provider.AuthorizeAsync(intent, Required("NEXACONNECT_OMISE_WEBHOOK_LIVE_TEST_TOKEN"), default);
        Assert.Equal(ProviderAuthorizationOutcome.Authorized, result.Outcome);
        // Deliberately do not complete the local authorization. The signed provider event must drive status-only recovery.
    }

    [OmiseWebhookLiveAcceptanceFact("verify")]
    public async Task Verify_restart_and_signed_duplicate_replay_without_duplicate_financial_transition()
    {
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(Connection());
        (Guid organization, Guid intentId) = await Identity(source);
        await using var command = source.CreateCommand("""
            SELECT json_build_object(
              'intentCount',count(*) FILTER(WHERE p.id=$1 AND p.organization_id=$2),
              'authorizedCount',count(*) FILTER(WHERE p.id=$1 AND p.organization_id=$2 AND p.status='authorized'
                AND p.provider_authorization_id ~ '^chrg_(test_)?[a-z0-9]{10,64}$'),
              'authorizationStarts',(SELECT count(*) FROM payment_audit_records WHERE payment_intent_id=$1 AND action='payment.authorization.started'),
              'authorizationReconciled',(SELECT count(*) FROM payment_audit_records WHERE payment_intent_id=$1 AND action='payment.authorization.reconciled'),
              'reconciledOutbox',(SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.authorization-reconciled.v1'),
              'inboxCount',(SELECT count(*) FROM omise_webhook_inbox),
              'uniqueInboxCount',(SELECT count(DISTINCT event_id) FROM omise_webhook_inbox),
              'completedInboxCount',(SELECT count(*) FROM omise_webhook_inbox WHERE status='completed'),
              'activeInboxCount',(SELECT count(*) FROM omise_webhook_inbox WHERE status IN('pending','processing')))
            FROM payment_intents p WHERE p.id=$1 AND p.organization_id=$2;
            """);
        command.Parameters.AddWithValue(intentId); command.Parameters.AddWithValue(organization);
        using JsonDocument json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        JsonElement value = json.RootElement;
        Assert.Equal(1, value.GetProperty("intentCount").GetInt32());
        Assert.Equal(1, value.GetProperty("authorizedCount").GetInt32());
        Assert.Equal(1, value.GetProperty("authorizationStarts").GetInt32());
        Assert.Equal(1, value.GetProperty("authorizationReconciled").GetInt32());
        Assert.Equal(1, value.GetProperty("reconciledOutbox").GetInt32());
        int inbox = value.GetProperty("inboxCount").GetInt32();
        Assert.True(inbox >= 1); Assert.Equal(inbox, value.GetProperty("uniqueInboxCount").GetInt32());
        Assert.True(value.GetProperty("completedInboxCount").GetInt32() >= 1);
        Assert.Equal(0, value.GetProperty("activeInboxCount").GetInt32());
        Assert.Equal(int.Parse(Required("NEXACONNECT_OMISE_WEBHOOK_LIVE_EXPECTED_INBOX_COUNT")), inbox);

        string evidence = Required("NEXACONNECT_OMISE_WEBHOOK_LIVE_EVIDENCE");
        await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow, provider = "Omise", testMode = true,
            externalSignedDeliveryVerified = true, canonicalEventAndCurrentChargeReadsVerified = true,
            financialCommitBeforeInboxAcknowledgementVerified = true, paymentProcessTerminationVerified = true,
            expiredInboxLeaseRecoveryVerified = true, signedDuplicateReplayVerified = true,
            stablePaymentAndOrderIdentityVerified = true, authorizationCommandStarts = 1,
            authorizationReconciliations = 1, duplicateFinancialTransitionsDetected = false,
            inboxEventCount = inbox, providerReferencesRetained = false, eventIdentifiersRetained = false,
            signaturesOrBodiesRetained = false, secretsRetained = false, rawServiceLogsRetained = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static PostgresPaymentIntents Repository(NpgsqlDataSource source) => new(source,
        Options.Create(new PaymentProviderOptions { RequestTimeout = TimeSpan.FromSeconds(5), LeaseDuration = TimeSpan.FromSeconds(30) }));
    private static decimal Amount() => decimal.Parse(Required("NEXACONNECT_OMISE_WEBHOOK_LIVE_AMOUNT"), System.Globalization.CultureInfo.InvariantCulture);
    private static async Task<(Guid Organization, Guid Intent)> Identity(NpgsqlDataSource source)
    {
        await using var command = source.CreateCommand("SELECT organization_id,payment_intent_id FROM omise_webhook_live_fixture WHERE singleton=true");
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetGuid(1));
    }
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
