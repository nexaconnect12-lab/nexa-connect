extern alias MIGRATIONS;
extern alias REPORTING;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.IntegrationTests;

[Collection("Order migration runner acceptance")]
public sealed class SaleReportingMigrationAcceptanceTests
{
    [OrderMigrationAcceptanceFact]
    public async Task Clean_reporting_runner_upgrade_downgrade_reupgrade_and_evidence_guard()
    {
        string cs = Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string name = $"nexaconnect_reporting_sale_it_{Guid.NewGuid():N}";
        if (!System.Text.RegularExpressions.Regex.IsMatch(name,"^nexaconnect_reporting_sale_it_[a-f0-9]{32}$")) throw new InvalidOperationException();
        string quoted = new NpgsqlCommandBuilder().QuoteIdentifier(name);
        await using var admin = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { Database = "postgres" }.ConnectionString);
        await using var connection = await admin.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE DATABASE {quoted}",connection).ExecuteNonQueryAsync();
        string? previous = Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_DB");
        try
        {
            var db = new NpgsqlConnectionStringBuilder(cs) { Database = name }.ConnectionString;
            Environment.SetEnvironmentVariable("NEXACONNECT_REPORTING_DB",db);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName,"NexaConnect.sln"))) root = root.Parent!;
            string scripts = Path.Combine(root.FullName,"src/Tools/NexaConnect.DataMigration/Scripts");
            Task<int> Run(int target,bool down=false) => MIGRATIONS::MigrationApplication.RunAsync(
                ["--service","Reporting","--scripts-root",scripts,"--target",target.ToString(),"--application-version","0.22.0","--confirm",..(down ? new[] {"--allow-destructive","--backup-verified"} : [])]);
            Assert.Equal(0,await Run(19)); Assert.Equal(0,await Run(18,true)); Assert.Equal(0,await Run(19));
            await using var source = NpgsqlDataSource.Create(db);
            var service = new REPORTING::NexaConnect.Services.Reporting.Application.SaleFinancialReporting(
                new REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresSaleFinancialFactRepository(source));
            var now = DateTimeOffset.UtcNow;
            var value = new OrderSaleCompletedV1(Guid.NewGuid(),Guid.NewGuid(),now,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
                Guid.NewGuid(),Guid.NewGuid(),"manual_settlement","cash","THB","pos","takeaway",now.AddMinutes(-1),now,"R-TEST",10,0,0,10);
            Assert.True(await service.ProjectAsync(value,default));
            Assert.NotEqual(0,await Run(18,true));
            Assert.False(await service.ProjectAsync(value,default));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_REPORTING_DB",previous);
            await new NpgsqlCommand($"DROP DATABASE {quoted} WITH (FORCE)",connection).ExecuteNonQueryAsync();
        }
    }
}
