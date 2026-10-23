extern alias RESTAURANT;
using Npgsql;
using NexaConnect.Contracts.RestaurantQr;
using Repo = RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresQrEntryRepository;
using Entry = RESTAURANT::NexaConnect.Services.Restaurant.Domain.Qr.QrEntry;
using Conflict = RESTAURANT::NexaConnect.Services.Restaurant.Domain.Qr.QrConflictException;
using Limit = RESTAURANT::NexaConnect.Services.Restaurant.Domain.Qr.QrLimitException;
using Secrets = RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.QrSecrets;

namespace NexaConnect.IntegrationTests;

public sealed class QrEntryPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Admission_is_durable_scoped_bounded_versioned_and_transactional()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1", settings.Host); Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$", settings.Database!);
        string schema = "qr_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
            settings.SearchPath = schema; await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Restaurant"));
            foreach (var dir in Directory.GetDirectories(scripts).Order()) await Sql(source, await File.ReadAllTextAsync(Path.Combine(dir, "up.sql")));
            Guid org = Guid.NewGuid(), restaurant = Guid.NewGuid(), branch = Guid.NewGuid();
            await using (var seed = source.CreateCommand("INSERT INTO restaurants(id,organization_id,code,name,default_currency,default_time_zone,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'qr','Restaurant','THB','Asia/Singapore','active',now(),now(),'fixture','fixture');"))
            { seed.Parameters.AddWithValue(restaurant); seed.Parameters.AddWithValue(org); await seed.ExecuteNonQueryAsync(); }
            await using (var seed = source.CreateCommand("INSERT INTO branches(id,restaurant_id,code,name,time_zone,currency,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'qr','Branch','Asia/Singapore','THB','active',now(),now(),'fixture','fixture');"))
            { seed.Parameters.AddWithValue(branch); seed.Parameters.AddWithValue(restaurant); await seed.ExecuteNonQueryAsync(); }
            var repo = new Repo(source); var secrets = new Secrets(); var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var scope = (await repo.ScopeAsync(org, branch, default))!;
            Assert.Null(await repo.ScopeAsync(Guid.NewGuid(), branch, default));
            string token = secrets.Generate(); var entry = new Entry(Guid.NewGuid(), scope, "A1", secrets.Hash(token), 1, true, now.AddDays(30));
            await repo.SaveAsync(entry, 0, "fixture", now, default);
            await Assert.ThrowsAsync<Conflict>(() => repo.SaveAsync(new Entry(Guid.NewGuid(), scope, "A1", secrets.Hash(secrets.Generate()), 1, true, now.AddDays(30)), 0, "fixture", now, default));
            var wrong = scope with { OrganizationId = Guid.NewGuid() }; Assert.Empty(await repo.ListAsync(wrong, default)); Assert.Null(await repo.GetAsync(wrong, entry.Id, default));
            Assert.Null(await repo.AdmitAsync(secrets.Hash(secrets.Generate()), secrets.Hash(secrets.Generate()), Guid.NewGuid(), now, default));
            string a = secrets.Generate(), b = secrets.Generate();
            var first = await repo.AdmitAsync(secrets.Hash(token), secrets.Hash(a), Guid.NewGuid(), now, default);
            var second = await repo.AdmitAsync(secrets.Hash(token), secrets.Hash(b), Guid.NewGuid(), now, default);
            Assert.NotEqual(first!.VisitorId, second!.VisitorId); Assert.Equal(entry.Id, first.EntryId); Assert.Equal(org, first.OrganizationId);
            // A fresh repository instance reads the same persisted admission after application restart.
            Assert.Equal(first, await new Repo(source).ContextAsync(secrets.Hash(a), now, default));
            Assert.Null(await repo.ContextAsync(secrets.Hash(a), now.AddHours(2), default));
            await repo.EndAsync(secrets.Hash(a), default); Assert.Null(await repo.ContextAsync(secrets.Hash(a), now, default));
            Assert.NotNull(await repo.ContextAsync(secrets.Hash(b), now, default));
            await Sql(source, "UPDATE branches SET status='suspended';"); Assert.Null(await repo.ContextAsync(secrets.Hash(b), now, default));
            await Sql(source, "UPDATE branches SET status='active';");
            string nextToken = secrets.Generate(); entry.Rotate(1, secrets.Hash(nextToken), now);
            var contender = (await repo.GetAsync(scope, entry.Id, default))!; contender.Revoke(1);
            var results = await Task.WhenAll(Save(repo, entry, now), Save(repo, contender, now)); Assert.Single(results, result => result);
            var current = (await repo.GetAsync(scope, entry.Id, default))!;
            Assert.Null(await repo.ContextAsync(secrets.Hash(b), now, default));
            Assert.Null(await repo.AdmitAsync(secrets.Hash(token), secrets.Hash(secrets.Generate()), Guid.NewGuid(), now, default));
            // Explicit reissue makes the race winner irrelevant and never restores an old version.
            long version = current.Version; current.Rotate(version, secrets.Hash(nextToken), now); await repo.SaveAsync(current, version, "fixture", now, default);
            var sessionTokens = Enumerable.Range(0, 30).Select(_ => secrets.Generate()).ToArray();
            var concurrent = await Task.WhenAll(sessionTokens.Select(t => repo.AdmitAsync(secrets.Hash(nextToken), secrets.Hash(t), Guid.NewGuid(), now, default)));
            Assert.Equal(30, concurrent.Length);
            await Assert.ThrowsAsync<Limit>(() => repo.AdmitAsync(secrets.Hash(nextToken), secrets.Hash(secrets.Generate()), Guid.NewGuid(), now, default));
            foreach (string t in sessionTokens) await repo.EndAsync(secrets.Hash(t), default);
            Assert.Null(await repo.ContextAsync(secrets.Hash(sessionTokens[0]), now, default));
            // Logging out cannot erase starts in the rolling abuse budget.
            await Assert.ThrowsAsync<Limit>(() => repo.AdmitAsync(secrets.Hash(nextToken), secrets.Hash(secrets.Generate()), Guid.NewGuid(), now, default));
            Assert.NotNull(await repo.AdmitAsync(secrets.Hash(nextToken), secrets.Hash(secrets.Generate()), Guid.NewGuid(), now.AddMinutes(1), default));
            await using (var check = source.CreateCommand("SELECT count(*) FROM restaurant_qr_sessions WHERE secret_hash=$1;"))
            { check.Parameters.AddWithValue(a); Assert.Equal(0L, await check.ExecuteScalarAsync()); }
            // Outbox failure rolls back entry mutation AND append-only audit.
            await Sql(source, "ALTER TABLE outbox_messages ADD CONSTRAINT qr_fixture_reject CHECK(false) NOT VALID;");
            version = current.Version; current.Revoke(version);
            await Assert.ThrowsAsync<PostgresException>(() => repo.SaveAsync(current, version, "fixture", now, default));
            Assert.Equal(version, (await repo.GetAsync(scope, entry.Id, default))!.Version);
            await Sql(source, "ALTER TABLE outbox_messages DROP CONSTRAINT qr_fixture_reject;");
            var live = (await repo.GetAsync(scope, entry.Id, default))!; live.Revoke(version); await repo.SaveAsync(live, version, "fixture", now, default);
            Assert.Null(await repo.ContextAsync(secrets.Hash(b), now, default));
            await Assert.ThrowsAsync<PostgresException>(() => Sql(source, "DELETE FROM restaurant_qr_audit;"));
            await Sql(source, await File.ReadAllTextAsync(Path.Combine(scripts, "0004_qr_entry/down.sql")));
            await Sql(source, await File.ReadAllTextAsync(Path.Combine(scripts, "0004_qr_entry/up.sql")));
            Assert.Empty(await repo.ListAsync(scope, default)); Assert.NotNull(await repo.ScopeAsync(org, branch, default));
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin); await drop.ExecuteNonQueryAsync(); }
    }
    private static async Task<bool> Save(Repo repo, Entry entry, DateTimeOffset now)
    { try { await repo.SaveAsync(entry, 1, "fixture", now, default); return true; } catch (Conflict) { return false; } }
    private static async Task Sql(NpgsqlDataSource source, string sql) { await using var cmd = source.CreateCommand(sql); await cmd.ExecuteNonQueryAsync(); }
}
