extern alias MIGRATIONS;
extern alias NOTIFICATION;

using DeliveryPolicy = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryPolicy;
using MutationContext = NOTIFICATION::NexaConnect.Services.Notification.Application.Messages.NotificationMutationContext;
using ProviderOutcome = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationProviderOutcome;
using ProviderResult = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationProviderResult;
using DeliveryRepository = NOTIFICATION::NexaConnect.Services.Notification.Infrastructure.PostgresNotificationDeliveryRepository;
using NotificationSender = NOTIFICATION::NexaConnect.Services.Notification.Infrastructure.PostgresNotificationSender;
using SendNotification = NOTIFICATION::NexaConnect.Services.Notification.Application.Messages.SendNotification;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Notification migration runner acceptance")]
public sealed class NotificationMigrationRunnerAcceptanceTests
{
    [NotificationMigrationFact]
    public async Task Empty_database_runs_0_to_3_to_2_to_3()
    {
        string admin = Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string database = $"nexaconnect_notification_clean_it_{Guid.NewGuid():N}";
        Validate(database);
        var adminBuilder = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" };
        await using var adminSource = NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateAsync(adminSource, database);
        string? previous = Environment.GetEnvironmentVariable("NEXACONNECT_NOTIFICATION_DB");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin) { Database = database };
            Environment.SetEnvironmentVariable("NEXACONNECT_NOTIFICATION_DB", builder.ConnectionString);
            string root = Path.Combine(RepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");
            Assert.Equal(0, await RunAsync(root, 3));
            await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            var sender = new NotificationSender(dataSource);
            Guid correlation = Guid.NewGuid();
            var first = sender.Send(new SendNotification(Guid.NewGuid(), "email", "private@example.test", "Subject", "Body"),
                new MutationContext("migration-test", correlation, correlation.ToString("D")));
            var repository = new DeliveryRepository(dataSource);
            var work = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
                await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
            var accepted = new ProviderResult(ProviderOutcome.Accepted, "migration-provider", "receipt-1");
            await repository.RecordAsync(work, accepted,
                DeliveryPolicy.Decide(work, accepted, 4, DateTimeOffset.UtcNow), default);
            Assert.Equal(1L, await CountAsync(dataSource,
                "SELECT count(*) FROM notification_delivery_attempts WHERE notification_id=$1", first.Id));

            Assert.Equal(0, await RunAsync(root, 2, destructive: true));
            Assert.Null(await RelationAsync(dataSource, "notification_delivery_attempts"));
            Assert.Equal("outbox_messages", await RelationAsync(dataSource, "outbox_messages"));
            Assert.Equal(1L, await CountAsync(dataSource, "SELECT count(*) FROM notifications WHERE id=$1", first.Id));

            Assert.Equal(0, await RunAsync(root, 3));
            Assert.Equal("provider_accepted", await ValueAsync(dataSource,
                "SELECT status FROM notifications WHERE id=$1", first.Id));
            Assert.NotNull(await ValueAsync(dataSource,
                "SELECT next_receipt_attempt_at_utc::text FROM notifications WHERE id=$1", first.Id));
            var second = sender.Send(new SendNotification(Guid.NewGuid(), "sms", "+15555550100", "Alert", "Body"),
                new MutationContext("migration-test", Guid.NewGuid(), Guid.NewGuid().ToString("D")));
            Assert.Equal(1L, await CountAsync(dataSource, "SELECT count(*) FROM notifications WHERE id=$1", second.Id));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_NOTIFICATION_DB", previous);
            await DropAsync(adminSource, database);
        }
    }

    private static Task<int> RunAsync(string root, int target, bool destructive = false)
    {
        var args = new List<string> { "--service", "Notification", "--scripts-root", root, "--target", target.ToString(),
            "--application-version", "0.10.0", "--confirm" };
        if (destructive) args.AddRange(["--allow-destructive", "--backup-verified"]);
        return MigrationApplication.RunAsync(args.ToArray());
    }

    private static async Task<long> CountAsync(NpgsqlDataSource source, string sql, Guid value) =>
        Convert.ToInt64(await ScalarAsync(source, sql, value));

    private static async Task<string?> ValueAsync(NpgsqlDataSource source, string sql, Guid value) =>
        Convert.ToString(await ScalarAsync(source, sql, value));

    private static async Task<object?> ScalarAsync(NpgsqlDataSource source, string sql, Guid value)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<string?> RelationAsync(NpgsqlDataSource source, string relation)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass($1)::text", connection);
        command.Parameters.AddWithValue($"public.{relation}");
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task CreateAsync(NpgsqlDataSource source, string database)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(database)}", connection)
            .ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(NpgsqlDataSource source, string database)
    {
        Validate(database);
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(database)} WITH (FORCE)", connection)
            .ExecuteNonQueryAsync();
    }

    private static void Validate(string database)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(database,
                "^nexaconnect_notification_clean_it_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe Notification acceptance database name.");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

[CollectionDefinition("Notification migration runner acceptance", DisableParallelization = true)]
public sealed class NotificationMigrationRunnerCollection;

public sealed class NotificationMigrationFactAttribute : FactAttribute
{
    public NotificationMigrationFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (Environment.GetEnvironmentVariable("NEXACONNECT_NOTIFICATION_CLEAN_INSTALL_ACCEPTANCE") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB"))
            || environment is not ("Development" or "Test" or "Testing"))
            Skip = "Notification migration acceptance requires opt-in, administrator connection, and a safe environment.";
    }
}
