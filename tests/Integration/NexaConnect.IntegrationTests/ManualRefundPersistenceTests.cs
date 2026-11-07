extern alias POS;
extern alias REPORTING;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.POS;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using Npgsql;
using RabbitMQ.Client;
using POS::NexaConnect.Services.POS.Application.ManualRefunds;
using POS::NexaConnect.Services.POS.Application.Shifts;
using POS::NexaConnect.Services.POS.Domain.ManualRefunds;
using POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using REPORTING::NexaConnect.Services.Reporting.Application;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging;

namespace NexaConnect.IntegrationTests;

public sealed class ManualRefundPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Cash_and_promptpay_refunds_preserve_custody_and_atomic_drawer_evidence()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$", settings.Database!);
        string schema = "refund_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
                await create.ExecuteNonQueryAsync();
            settings.SearchPath = schema;
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/POS"));
            foreach (string migration in Directory.GetDirectories(scripts).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(migration).StartsWith("0027_", StringComparison.Ordinal)) break;
                await Run(await File.ReadAllTextAsync(Path.Combine(migration, "up.sql")));
            }
            string up = await File.ReadAllTextAsync(Path.Combine(scripts, "0027_manual_refunds/up.sql"));
            string down = await File.ReadAllTextAsync(Path.Combine(scripts, "0027_manual_refunds/down.sql"));
            await Run(up);
            await Run("BEGIN;\n" + down + "\nCOMMIT;");
            await Run(up);
            string management = await File.ReadAllTextAsync(Path.Combine(scripts, "0028_manual_refund_management/up.sql"));
            await Run(management);
            await Run(await File.ReadAllTextAsync(Path.Combine(scripts, "0029_manual_refund_day_evidence/up.sql")));

            var (org, restaurant, branch, store, terminal, shift, drawer) =
                (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            // Singapore midnight separates the original sale from both completed refunds.
            var now = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(16), TimeSpan.Zero);
            await Run("""
                INSERT INTO stores (id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by)
                VALUES ($1,$2,$3,'test','Test','active',now(),'fixture',now(),'fixture')
                """, store, restaurant, branch);
            await Run("""
                INSERT INTO terminals (id,restaurant_id,store_id,code,device_type,registration_status,created_at_utc,updated_at_utc)
                VALUES ($1,$2,$3,'test','pos','active',now(),now())
                """, terminal, restaurant, store);
            await Run("""
                INSERT INTO shifts (id,store_id,terminal_id,employee_identity_subject_id,shift_number,status,
                    opened_at_utc,opened_by,created_at_utc,updated_at_utc,authorization_decision_id)
                VALUES ($1,$2,$3,'cashier','test','open',$4,'cashier',$4,$4,$5)
                """, shift, store, terminal, now.AddHours(-1), Guid.NewGuid());
            await Run("""
                INSERT INTO cash_sessions (id,store_id,shift_id,currency,opening_amount,status,opened_at_utc,created_at_utc,updated_at_utc)
                VALUES ($1,$2,$3,'THB',1000,'open',$4,$4,$4)
                """, drawer, store, shift, now.AddHours(-1));
            var protector = new EphemeralDataProtectionProvider();
            var repository = new PostgresManualRefundStore(source, protector);
            var sale = new OriginalManualSale(org, restaurant, branch, Guid.NewGuid(), Guid.NewGuid(),
                ManualRefundTender.Cash, 100, "THB", now.AddMinutes(-10));
            ManualRefundApproval Approval(decimal amount) => new(Guid.NewGuid(), amount, "customer_request", "manager", Guid.NewGuid(), "cashier", now);
            async Task<ManualRefundWrite?> Reserve(ManualRefundApproval approval)
            {
                try { return await repository.ApproveAsync(sale, approval, default); }
                catch (InvalidOperationException e) when (e.Message.Contains("remaining paid amount")) { return null; }
            }
            var approvals = await Task.WhenAll(Reserve(Approval(75)), Reserve(Approval(75)));
            var original = Assert.Single(approvals, a => a is not null)!.Refund.Approval;
            Assert.True((await repository.ApproveAsync(sale, original, default)).Replayed);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.ApproveAsync(sale with { OrganizationId = Guid.NewGuid() }, original, default));
            var custody = new ManualRefundPayout(terminal, shift, drawer, "cashier", now.AddMinutes(1));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.BeginAsync(sale, original.OperationId, custody with { CashSessionId = Guid.NewGuid() }, default));
            Assert.False((await repository.BeginAsync(sale, original.OperationId, custody, default)).Replayed);
            foreach (var close in new[] { "UPDATE cash_sessions SET status='closed' WHERE id=$1", "UPDATE shifts SET status='closed' WHERE id=$1" })
            {
                var blocked = await Assert.ThrowsAsync<PostgresException>(() => Run(close, close.Contains("cash_sessions") ? drawer : shift));
                Assert.Equal("PMR01", blocked.SqlState);
            }
            var restarted = new PostgresManualRefundStore(source, protector);
            Assert.True((await restarted.BeginAsync(sale, original.OperationId, custody, default)).Replayed);
            await Run("""
                CREATE FUNCTION reject_fixture_completion() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'fixture rollback'; END $$;
                CREATE TRIGGER reject_fixture_completion BEFORE INSERT ON manual_refund_completions
                FOR EACH STATEMENT EXECUTE FUNCTION reject_fixture_completion();
                """);
            await Assert.ThrowsAsync<PostgresException>(() =>
                restarted.CompleteAsync(sale, original.OperationId, custody, false, null, now.AddMinutes(2), default));
            Assert.Equal(0L, await Count("SELECT count(*) FROM cash_movements"));
            await Run("DROP TRIGGER reject_fixture_completion ON manual_refund_completions");
            var completed = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                restarted.CompleteAsync(sale, original.OperationId, custody, false, null, now.AddMinutes(2), default)));
            Assert.Single(completed, result => !result.Replayed);
            Assert.Equal(1L, await Count("""
                SELECT count(*) FROM manual_refund_completions c JOIN cash_movements m ON m.id=c.cash_movement_id
                WHERE m.order_id=$1 AND m.cash_session_id=$2 AND m.movement_type='refund' AND m.amount=75
                """, sale.OrderId, drawer));
            var cash = new PostgresCashSessionStore(source);
            var summary = await cash.GetSummaryAsync(drawer, "cashier", terminal, default);
            Assert.NotNull(summary);
            await cash.CloseAsync(drawer, 925, summary.ConcurrencyVersion, "cashier", terminal, default);
            Assert.True((await restarted.CompleteAsync(sale, original.OperationId, custody, false, null, now.AddMinutes(3), default)).Replayed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReleaseAsync(sale, original.OperationId,
                new("manager", Guid.NewGuid(), now.AddMinutes(3)), default));

            var promptPay = sale with { OrderId = Guid.NewGuid(), SettlementId = Guid.NewGuid(), Tender = ManualRefundTender.PromptPay };
            var transfer = Approval(30);
            await repository.ApproveAsync(promptPay, transfer, default);
            var transferCustody = custody with { CashSessionId = null };
            await repository.BeginAsync(promptPay, transfer.OperationId, transferCustody, default);
            await repository.CompleteAsync(promptPay, transfer.OperationId, transferCustody, true, "synthetic-proof", now.AddMinutes(2), default);
            Assert.True((await restarted.CompleteAsync(promptPay, transfer.OperationId, transferCustody, true, "synthetic-proof", now.AddMinutes(3), default)).Replayed);
            Assert.Equal(1L, await Count("SELECT count(*) FROM cash_movements"));
            Assert.Equal(1L, await Count("""
                SELECT count(*) FROM manual_refund_completions
                WHERE operation_id=$1 AND cash_movement_id IS NULL AND protected_transfer_reference <> $2
                """, transfer.OperationId, "synthetic-proof"));
            await VerifyRefundReportingAsync(admin, settings, schema, source, sale, now);
            await VerifyHttpAsync(repository, promptPay with { OrderId = Guid.NewGuid(), SettlementId = Guid.NewGuid() },
                new(terminal, shift, null));
            await Assert.ThrowsAsync<PostgresException>(() => Run("UPDATE manual_refund_approvals SET amount=1"));
            await Assert.ThrowsAsync<PostgresException>(() => Run("BEGIN;\n" + down + "\nCOMMIT;"));

            async Task Run(string sql, params object[] args)
            {
                await using var command = source.CreateCommand(sql);
                foreach (var arg in args) command.Parameters.AddWithValue(arg);
                await command.ExecuteNonQueryAsync();
            }

            async Task<long> Count(string sql, params object[] args)
            {
                await using var command = source.CreateCommand(sql);
                foreach (var arg in args) command.Parameters.AddWithValue(arg);
                return (long)(await command.ExecuteScalarAsync())!;
            }
        }
        finally
        {
            Assert.Matches("^refund_[a-f0-9]{32}$", schema);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyRefundReportingAsync(NpgsqlConnection admin, NpgsqlConnectionStringBuilder settings,
        string posSchema, NpgsqlDataSource pos, OriginalManualSale sale, DateTimeOffset now)
    {
        string schema = posSchema + "_reporting";
        Assert.Matches("^refund_[a-f0-9]{32}_reporting$", schema);
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var reportingSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { SearchPath = schema };
            await using var reporting = NpgsqlDataSource.Create(reportingSettings.ConnectionString);
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Reporting"));
            foreach (var name in new[] { "0001_initial_schema", "0018_payment_refund_facts", "0023_manual_refund_facts" })
            {
                string path = Path.Combine(root, name, "up.sql");
                await using var migrate = reporting.CreateCommand(await File.ReadAllTextAsync(path));
                await migrate.ExecuteNonQueryAsync();
            }
            var events = new List<PosManualRefundCompletedV1>();
            await using (var select = pos.CreateCommand("SELECT payload::text FROM outbox_messages WHERE event_type='pos.manual-refund-completed.v1'"))
            await using (var rows = await select.ExecuteReaderAsync())
                while (await rows.ReadAsync()) events.Add(JsonSerializer.Deserialize<PosManualRefundCompletedV1>(rows.GetString(0))!);
            Assert.Equal(2, events.Count);
            Assert.NotNull(Assert.Single(events, value => value.Method == "cash").CashMovementId);
            Assert.Null(Assert.Single(events, value => value.Method == "promptpay_manual").CashMovementId);
            await using (var insert = reporting.CreateCommand("""
                INSERT INTO sales_facts(order_id,source_event_id,organization_id,restaurant_id,branch_id,channel,service_type,
                    currency,subtotal_amount,discount_amount,service_charge_amount,tax_amount,total_amount,order_status,
                    ordered_at_utc,completed_at_utc,projected_at_utc,source_event_version)
                VALUES($1,$2,$3,$4,$5,'pos','dine_in','THB',100,0,0,0,100,'completed',$6,$6,now(),1)
                """))
            {
                foreach (var value in new object[] { sale.OrderId, Guid.NewGuid(), sale.OrganizationId, sale.RestaurantId, sale.BranchId, sale.PaidAtUtc })
                    insert.Parameters.AddWithValue(value);
                await insert.ExecuteNonQueryAsync();
            }
            var projector = new ManualRefundFinancialReporting(new PostgresManualRefundFinancialFacts(reporting));
            bool delivered = await DeliverManualRefundsAsync(reporting, events);
            foreach (var value in events)
            {
                Assert.Equal(!delivered, await projector.ProjectAsync(value, default));
                Assert.False(await projector.ProjectAsync(value with { CorrelationId = Guid.NewGuid() }, default));
            }
            await Assert.ThrowsAsync<ArgumentException>(() => projector.ProjectAsync(events[0] with { Amount = 1 }, default));
            var report = new PostgresReportingReadRepository(reporting);
            var previous = await report.SalesAsync(new(sale.OrganizationId, sale.BranchId, now.AddDays(-1), now), default);
            Assert.Equal(100, previous.NetSales);
            Assert.Equal(0, previous.RefundedAmount);
            var range = new ReportingRange(sale.OrganizationId, sale.BranchId, now, now.AddDays(1));
            var current = await report.DashboardAsync(range, default);
            Assert.Equal(75, current.ManualCashRefunds);
            Assert.Equal(30, current.ManualPromptPayRefunds);
            Assert.Equal(-105, current.NetSales);
            var window = new EndOfDayWindow(sale.OrganizationId, sale.RestaurantId, sale.BranchId, range.FromUtc, range.ToUtc);
            var source = await new PostgresPosDayReader(pos).ReadAsync(window, default);
            Assert.NotNull(source.ManualRefunds);
            Assert.Equal(0, source.ManualRefunds.DrawerEvidenceGaps);
            Assert.Equal(2, source.ManualRefunds.CompletedCount);
            Assert.NotNull(source.ManualRefunds.EvidenceVersion);
            Assert.Equal(source.ManualRefunds.EvidenceVersion, current.ManualRefundEvidenceVersion);
            Assert.Equal(source.ManualRefunds,await new PostgresManualRefundFinancialFacts(reporting).ReadDayAsync(window,default));
            Assert.Equal(0,(await new PostgresManualRefundFinancialFacts(reporting).ReadDayAsync(window with{RestaurantId=Guid.NewGuid()},default)).CompletedCount);
            await Assert.ThrowsAsync<SnapshotOperationConflictException>(() => new PostgresPosDayFenceStore(pos).ExecuteAsync(
                new(Guid.NewGuid(), window, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1)), "fixture", false, default));
            Assert.Equal(0, (await report.DashboardAsync(range with { BranchId = Guid.NewGuid() }, default)).ManualRefundCount);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> DeliverManualRefundsAsync(NpgsqlDataSource reporting, IReadOnlyList<PosManualRefundCompletedV1> events)
    {
        string? broker = Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_BROKER");
        if (string.IsNullOrWhiteSpace(broker)) return false;
        Assert.True(new Uri(broker).IsLoopback);
        string name = "manual-refund-fixture-" + Guid.NewGuid().ToString("N");
        using var provider = new ServiceCollection().AddSingleton(reporting)
            .AddScoped<IManualRefundFinancialFacts, PostgresManualRefundFinancialFacts>()
            .AddScoped<ManualRefundFinancialReporting>().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ManualRefundConsumer:ConnectionString"] = broker,
            ["ManualRefundConsumer:Exchange"] = name,
            ["ManualRefundConsumer:Queue"] = name,
            ["ManualRefundConsumer:PrefetchCount"] = "1"
        }).Build();
        using var consumer = new ManualRefundFinancialConsumer(provider.GetRequiredService<IServiceScopeFactory>(),
            configuration, NullLogger<PaymentRefundFinancialConsumer>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var connection = await new ConnectionFactory { Uri = new Uri(broker) }.CreateConnectionAsync(deadline.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: deadline.Token);
        try
        {
            await consumer.StartAsync(deadline.Token);
            await consumer.WaitUntilReadyAsync(deadline.Token);
            foreach (var value in new[] { events[0], events[0], events[1] })
                await channel.BasicPublishAsync(name, PosManualRefundCompletedV1.EventType,
                    JsonSerializer.SerializeToUtf8Bytes(value), deadline.Token);
            while (true)
            {
                await using var count = reporting.CreateCommand("SELECT count(*) FROM manual_refund_facts");
                if (Convert.ToInt32(await count.ExecuteScalarAsync(deadline.Token)) == events.Count) break;
                await Task.Delay(50, deadline.Token);
            }
            await using var checkpoint = reporting.CreateCommand("SELECT position FROM projection_checkpoints WHERE projector_name='manual-refund-financial'");
            Assert.Equal(events.Count, Convert.ToInt32(await checkpoint.ExecuteScalarAsync(deadline.Token)));
            return true;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await consumer.StopAsync(cleanup.Token);
            if (channel.IsOpen)
            {
                await channel.QueueDeleteAsync(name, cancellationToken: cleanup.Token);
                await channel.QueueDeleteAsync(name + ".dead", cancellationToken: cleanup.Token);
                await channel.ExchangeDeleteAsync(name, cancellationToken: cleanup.Token);
            }
        }
    }

    private static async Task VerifyHttpAsync(IManualRefundStore repository, OriginalManualSale original, ManualRefundCustodyV1 custody)
    {
        using var factory = new PosShiftApiFactory();
        var fixture = new RefundAuthority(original);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ManualRefunds:Enabled"] = "false" }).Build();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, app) => app.AddConfiguration(configuration));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRestaurantScopeReader>();
                services.RemoveAll<IAuthorizationDecisionClient>();
                services.RemoveAll<IManualRefundStore>();
                services.AddSingleton<IRestaurantScopeReader>(fixture);
                services.AddSingleton<IAuthorizationDecisionClient>(fixture);
                services.AddSingleton<IManualRefundMembership>(fixture);
                services.AddSingleton<IManualRefundSaleReader>(fixture);
                services.AddSingleton(repository);
            });
        });
        string path = $"/api/pos/v1/customer/organizations/{original.OrganizationId:D}/branches/{original.BranchId:D}/orders/{original.OrderId:D}/manual-refunds";
        using var manager = Client("manager");
        using var cashier = Client("cashier");
        using var wrongCashier = Client("other-cashier");
        var approval = new ApproveManualRefundV1(Guid.NewGuid(), 30, "customer_request", "cashier", custody.ShiftId);
        string operation = $"{path}/{approval.OperationId:D}";

        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync($"{path}/approve", approval)).StatusCode);
        configuration["ManualRefunds:Enabled"] = "true";
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync($"{path}/approve", approval with { Amount = 51 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{path}/approve", approval with { CashierSubject = "manager" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsJsonAsync($"{path}/approve", approval with { CashierSubject = "other-cashier" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(
            $"{path.Replace(original.BranchId.ToString("D"), Guid.NewGuid().ToString("D"))}/approve", approval)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"{path}/approve", approval)).StatusCode);
        string available = $"/api/pos/v1/customer/organizations/{original.OrganizationId:D}/branches/{original.BranchId:D}/manual-refunds/available";
        var ready = new ManualRefundAvailabilityV1(custody.TerminalId, custody.ShiftId);
        Assert.Equal(HttpStatusCode.NotFound, (await wrongCashier.PostAsJsonAsync(available, ready)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync(available, ready)).StatusCode);
        Assert.Equal(custody.ShiftId, Assert.Single((await manager.GetFromJsonAsync<ManualRefundCashiersV1>($"{path}/cashiers"))!.Items).ShiftId);
        var unused = approval with { OperationId = Guid.NewGuid(), Amount = 50 };
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{path}/approve", unused)).StatusCode);
        string unusedPath = $"{path}/{unused.OperationId:D}";
        var released = await (await manager.PostAsync($"{unusedPath}/release", null)).Content.ReadFromJsonAsync<ManualRefundStatusV1>();
        Assert.Equal("released", released!.Status);
        Assert.True((await (await manager.PostAsync($"{unusedPath}/release", null)).Content.ReadFromJsonAsync<ManualRefundStatusV1>())!.Replayed);
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync($"{unusedPath}/start", custody)).StatusCode);
        Assert.Equal(100, (await manager.GetFromJsonAsync<ManualRefundPreviewV1>($"{path}/preview"))!.AvailableAmount);
        using var approved = await manager.PostAsJsonAsync($"{path}/approve", approval);
        approved.EnsureSuccessStatusCode();
        Assert.Equal("approved", (await approved.Content.ReadFromJsonAsync<ManualRefundStatusV1>())!.Status);
        string assigned = $"/api/pos/v1/customer/organizations/{original.OrganizationId:D}/branches/{original.BranchId:D}/manual-refunds/assigned";
        var namedQueue = await cashier.GetFromJsonAsync<AssignedManualRefundsV1>(assigned);
        Assert.Equal(approval.OperationId, Assert.Single(namedQueue!.Items).OperationId);
        Assert.Empty((await wrongCashier.GetFromJsonAsync<AssignedManualRefundsV1>(assigned))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(assigned)).StatusCode);
        var preview = await manager.GetFromJsonAsync<ManualRefundPreviewV1>($"{path}/preview");
        Assert.Equal(70, preview!.AvailableAmount);
        Assert.Equal(HttpStatusCode.NotFound, (await wrongCashier.PostAsJsonAsync($"{operation}/start", custody)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync($"{operation}/start", custody)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await wrongCashier.GetAsync(operation)).StatusCode);
        var started = await (await cashier.PostAsJsonAsync($"{operation}/start", custody)).Content.ReadFromJsonAsync<ManualRefundStatusV1>();
        Assert.Equal("started", started!.Status);
        Assert.False(started.Replayed);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync($"{operation}/release", null)).StatusCode);
        Assert.True((await (await cashier.PostAsJsonAsync($"{operation}/start", custody)).Content.ReadFromJsonAsync<ManualRefundStatusV1>())!.Replayed);

        configuration["ManualRefunds:Enabled"] = "false";
        Assert.Equal("started", Assert.Single((await cashier.GetFromJsonAsync<AssignedManualRefundsV1>(assigned))!.Items).Status);
        var completion = new CompleteManualRefundV1(custody, true, "fixture-return");
        using var completionResponse = await cashier.PostAsJsonAsync($"{operation}/complete", completion);
        completionResponse.EnsureSuccessStatusCode(); // Recovery deliberately ignores the completion response body.
        using var recovered = await cashier.GetAsync(operation);
        Assert.True(recovered.Headers.CacheControl?.NoStore);
        string body = await recovered.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fixture-return", body);
        Assert.Equal("completed", (await recovered.Content.ReadFromJsonAsync<ManualRefundStatusV1>())!.Status);
        Assert.Empty((await cashier.GetFromJsonAsync<AssignedManualRefundsV1>(assigned))!.Items);
        configuration["ManualRefunds:Enabled"] = "true";
        var competing = approval with { OperationId = Guid.NewGuid(), Amount = 20 };
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{path}/approve", competing)).StatusCode);
        string competingPath = $"{path}/{competing.OperationId:D}";
        var race = await Task.WhenAll(manager.PostAsync($"{competingPath}/release", null),
            cashier.PostAsJsonAsync($"{competingPath}/start", custody));
        Assert.Single(race, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(race, response => response.StatusCode == HttpStatusCode.Conflict);
        fixture.Revoked = true;
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(operation)).StatusCode);

        HttpClient Client(string subject)
        {
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(subject));
            client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, original.OrganizationId.ToString("D"));
            client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
            return client;
        }
    }

    private sealed class RefundAuthority(OriginalManualSale sale) : IRestaurantScopeReader, IAuthorizationDecisionClient,
        IManualRefundMembership, IManualRefundSaleReader
    {
        public bool Revoked { get; set; }
        public Task<bool> HasAccessAsync(Guid organizationId, PosUserContext user, CancellationToken ct) =>
            Task.FromResult(!Revoked && organizationId == sale.OrganizationId);
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken ct) =>
            Task.FromResult(new RestaurantAuthorizationScope(sale.OrganizationId, sale.RestaurantId, sale.BranchId));
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user, RestaurantAuthorizationScope scope, string permission, CancellationToken ct) =>
            Task.FromResult(new AuthorizationDecision(Guid.NewGuid(), permission switch
            {
                ProductPermissions.PosManualRefundApprove => user.Subject == "manager",
                ProductPermissions.PosManualRefundPayout => user.Subject is "cashier" or "other-cashier",
                _ => false
            }, user.Subject == "manager" ? 50 : null));
        public Task<ManualRefundSaleV1?> ReadAsync(RestaurantAuthorizationScope scope, Guid orderId, PosUserContext user, CancellationToken ct) =>
            Task.FromResult<ManualRefundSaleV1?>(orderId != sale.OrderId ? null : new(sale.OrganizationId, sale.RestaurantId, sale.BranchId,
                sale.OrderId, sale.SettlementId, "promptpay_manual", sale.PaidAmount, sale.Currency, sale.PaidAtUtc));
    }
}
