using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using NexaConnect.Services.Authorization.Application.Assignments;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using NexaConnect.Services.POS.Infrastructure.Persistence;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Domain.CashReviews;
using NexaConnect.Services.POS.Domain.Shifts;
using NexaConnect.CashClosePortalAcceptance.Infrastructure;
using Npgsql;

string stage = "options";
try
{
    var options = FixtureOptions.Read();
    if (args.Length != 1 || args[0] is not ("provision" or "approve" or "late" or "revoke")) return 2;
    stage = "connections";
    await using var platformDb = NpgsqlDataSource.Create(options.Connection("platform"));
    await using var restaurantDb = NpgsqlDataSource.Create(options.Connection("restaurant"));
    await using var authorizationDb = NpgsqlDataSource.Create(options.Connection("authorization"));
    await using var posDb = NpgsqlDataSource.Create(options.Connection("pos"));
    var fixtureStore = new FixtureStore(posDb, authorizationDb);
    var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    if (args[0] == "provision")
    {
        stage = "empty-check";
        var platformRepository = new PostgresPlatformDirectoryManagementRepository(platformDb);
        var restaurantRepository = new PostgresRestaurantProvisioningRepository(restaurantDb);
        var authorizationRepository = new PostgresAuthorizationAssignmentRepository(authorizationDb);
        if (!await platformRepository.IsEmptyAsync(default) || !await restaurantRepository.IsEmptyAsync(default) ||
            !await authorizationRepository.IsEmptyAsync(default) || !await fixtureStore.IsEmptyAsync())
            throw new InvalidOperationException("Fresh fixture required.");
        stage = "platform";
        var platform = new PlatformDirectoryManagementService(platformRepository);
        string actor = "cash-close-fixture:" + options.RunId;
        var org = await platform.CreateOrganizationAsync(new("cash-" + options.RunId[..8], "Cash Close Acceptance", "Etc/UTC"), actor, default);
        var other = await platform.CreateOrganizationAsync(new("other-" + options.RunId[..8], "Other Tenant", "Etc/UTC"), actor, default);
        await platform.RegisterProductAsync(new("nexa_connect", "NexaConnect"), actor, default);
        foreach (Guid id in new[] { org.OrganizationId, other.OrganizationId })
        {
            if (!await platform.ChangeProductAccessAsync(id, new("nexa_connect", "enabled"), actor, default) ||
                !await platform.ChangeMembershipAsync(id, options.Resolver, new(options.Resolver, "active"), actor, default)) throw new InvalidOperationException();
            if (!await platform.ChangeMembershipAsync(id, options.Reader, new(options.Reader, "active"), actor, default)) throw new InvalidOperationException();
        }
        if (!await platform.ChangeMembershipAsync(org.OrganizationId, options.Reader, new(options.Reader, "active"), actor, default)) throw new InvalidOperationException();
        stage = "restaurant";
        var restaurant = new RestaurantProvisioningService(restaurantRepository);
        var r = await restaurant.CreateRestaurantAsync(new(org.OrganizationId, "cash", "Cash Close Restaurant", "THB", "Etc/UTC"), actor, default);
        var branch = await restaurant.CreateBranchAsync(r.RestaurantId, new("cash", "Cash Branch", "THB", "Etc/UTC"), actor, default) ?? throw new InvalidOperationException();
        var deniedBranch = await restaurant.CreateBranchAsync(r.RestaurantId, new("denied", "Denied Branch", "THB", "Etc/UTC"), actor, default) ?? throw new InvalidOperationException();
        stage = "assignments";
        var assignments = new AuthorizationAssignmentService(authorizationRepository);
        foreach (var (subject, role) in new[] { (options.Reader, "accountant"), (options.Resolver, "store-manager") })
            await assignments.AssignAsync(new(subject, org.OrganizationId, r.RestaurantId, role == "store-manager" ? null : branch.BranchId, role), actor, default);
        stage = "stores";
        Guid store = Guid.NewGuid(), deniedStore = Guid.NewGuid(), terminal = Guid.NewGuid();
        await fixtureStore.CreateStoreAsync(store, r.RestaurantId, branch.BranchId);
        await fixtureStore.CreateStoreAsync(deniedStore, r.RestaurantId, deniedBranch.BranchId);
        if (!await new PostgresTerminalStore(posDb).EnrollAsync(org.OrganizationId, r.RestaurantId, branch.BranchId, store, terminal, "acceptance", "pos", default)) throw new InvalidOperationException();
        stage = "cash";
        var shift = Shift.Open(Guid.NewGuid(), store, terminal, options.Resolver, "ACCEPTANCE", Guid.NewGuid(), await fixtureStore.NowAsync());
        await new PostgresShiftStore(posDb).CreateAsync(shift, default);
        var cash = new PostgresCashSessionStore(posDb);
        Guid session = await cash.OpenAsync(shift.Id, store, "THB", 100m, default);
        var occurred = await fixtureStore.NowAsync();
        await cash.CloseAsync(session, 95m, 1, options.Resolver, terminal, default);
        var state = new FixtureState(options.RunId, org.OrganizationId, other.OrganizationId, r.RestaurantId, branch.BranchId,
            store, deniedBranch.BranchId, deniedStore, terminal, session, occurred);
        await File.WriteAllTextAsync(options.StatePath, JsonSerializer.Serialize(state, json));
        Console.WriteLine(JsonSerializer.Serialize(state, json));
    }
    else
    {
        var state = JsonSerializer.Deserialize<FixtureState>(await File.ReadAllTextAsync(options.StatePath), json) ?? throw new InvalidOperationException();
        if (state.RunId != options.RunId) throw new InvalidOperationException();
        var reviews = new PostgresCashReviewStore(posDb);
        var scope = new CashReviewScope(state.OrganizationId, state.RestaurantId, state.BranchId, state.StoreId);
        var detail = await reviews.GetAsync(scope, state.SessionId, default) ?? throw new InvalidOperationException();
        if (args[0] == "approve")
        {
            if (detail.Session.SessionVersion != 2 || detail.Session.ReviewVersion != 0) throw new InvalidOperationException("Fresh decision required.");
            await reviews.ResolveAsync(scope, state.SessionId, CashReviewDecision.Create("approve", "acceptance"), options.Resolver,
                Guid.NewGuid(), 2, 0, Guid.NewGuid(), new string('a', 64), await fixtureStore.NowAsync(), default);
        }
        else if (args[0] == "late")
        {
            if (detail.Session.SessionVersion != 2 || detail.Session.ReviewStatus != "approved") throw new InvalidOperationException("Approved fixture required.");
            await new PostgresOrderSettlementProjectionStore(posDb).ProjectAsync(new OrderManualTenderSettledV1(Guid.NewGuid(), Guid.NewGuid(), state.OccurredAtUtc,
                state.OrganizationId, state.RestaurantId, state.BranchId, Guid.NewGuid(), Guid.NewGuid(), state.TerminalId, "cash", 10m, "THB"), default);
        }
        else await fixtureStore.RevokeReadAsync(options.Reader);
        Console.WriteLine("Fixture operation completed.");
    }
    return 0;
}
catch (Exception exception) { Console.Error.WriteLine($"Cash-close fixture failed at {stage} ({exception.GetType().Name}, SQLSTATE={(exception as PostgresException)?.SqlState ?? "none"}); sensitive details suppressed."); return 1; }

internal sealed record FixtureState(string RunId, Guid OrganizationId, Guid OtherOrganizationId, Guid RestaurantId,
    Guid BranchId, Guid StoreId, Guid DeniedBranchId, Guid DeniedStoreId, Guid TerminalId, Guid SessionId, DateTimeOffset OccurredAtUtc);
internal sealed record FixtureOptions(string RunId, string Reader, string Resolver, string StatePath)
{
    public static FixtureOptions Read()
    {
        string Required(string key) => Environment.GetEnvironmentVariable("NEXACONNECT_CASH_PORTAL_" + key) ?? throw new ArgumentException();
        string run = Required("RUN_ID");
        if (Required("ENABLED") != "1" || Required("CONFIRM_DISPOSABLE") != "1" || !System.Text.RegularExpressions.Regex.IsMatch(run, "^[a-f0-9]{32}$")) throw new ArgumentException();
        string reader = Required("READER_SUBJECT"), resolver = Required("RESOLVER_SUBJECT");
        if (!Guid.TryParse(reader, out _) || !Guid.TryParse(resolver, out _) || reader == resolver) throw new ArgumentException();
        string path = Path.GetFullPath(Required("STATE_PATH"));
        if (Path.GetFileName(path) != "fixture.json" || new DirectoryInfo(Path.GetDirectoryName(path)!).Name != run) throw new ArgumentException();
        return new(run, reader, resolver, path);
    }
    public string Connection(string suffix)
    {
        if (suffix is not ("platform" or "restaurant" or "authorization" or "pos")) throw new ArgumentException();
        string value = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_PORTAL_DB_" + suffix.ToUpperInvariant()) ?? throw new ArgumentException();
        var b = new NpgsqlConnectionStringBuilder(value);
        if (b.Host != "127.0.0.1" || b.Database != $"nexa_review_it_{RunId}_{suffix}") throw new ArgumentException();
        return value;
    }
}
