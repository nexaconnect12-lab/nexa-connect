extern alias ORDER;
extern alias PAYMENT;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using NexaConnect.Services.Authorization.Application.Assignments;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using NexaConnect.RefundAcceptance.Infrastructure;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;

string stage = "guard";
try
{
    var options = AcceptanceOptions.Read();
    if (args.Length != 1 || args[0] is not ("provision" or "run")) return 2;
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    var ct = deadline.Token;
    await using var platformDb = NpgsqlDataSource.Create(options.Connection("platform"));
    await using var restaurantDb = NpgsqlDataSource.Create(options.Connection("restaurant"));
    await using var authorizationDb = NpgsqlDataSource.Create(options.Connection("authorization"));
    await using var orderDb = NpgsqlDataSource.Create(options.Connection("order"));
    await using var paymentDb = NpgsqlDataSource.Create(options.Connection("payment"));
    await using var reportDb = NpgsqlDataSource.Create(options.Connection("reporting"));
    var store = new FixtureStore(authorizationDb, paymentDb, reportDb, orderDb);
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var platformRepository = new PostgresPlatformDirectoryManagementRepository(platformDb);
    var platform = new PlatformDirectoryManagementService(platformRepository);
    string actor = "refund-fixture:" + options.RunId;
    using var providerClient = new HttpClient(ProviderAcceptanceTls.CreateHandler(options.Omise ? null : AcceptanceOptions.Required("SIMULATOR_PIN"),
        options.ProviderUrl, true)) { BaseAddress = new Uri(options.ProviderUrl), Timeout = TimeSpan.FromSeconds(10) };
    var providerOptions = Options.Create(new PaymentProviderOptions { BaseUrl = options.ProviderUrl,
        ApiKey = AcceptanceOptions.Required("PROVIDER_KEY"), OmiseSecretKey = options.Omise ? AcceptanceOptions.Required("PROVIDER_KEY") : null!,
        Adapter = options.Omise ? "Omise" : "GenericHttp", LeaseDuration = TimeSpan.FromSeconds(options.Omise ? 55 : 15),
        RequestTimeout = TimeSpan.FromSeconds(10), MaximumRefundRecoveryAttempts = 2 });
    IPaymentProvider provider = options.Omise ? new OmisePaymentProvider(providerClient, providerOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<OmisePaymentProvider>.Instance) : new HttpPaymentProvider(providerClient, providerOptions);
    if (args[0] == "provision")
    {
        stage = "empty-fixture";
        var restaurantsRepository = new PostgresRestaurantProvisioningRepository(restaurantDb);
        var assignmentsRepository = new PostgresAuthorizationAssignmentRepository(authorizationDb);
        if (File.Exists(options.StatePath) || !await platformRepository.IsEmptyAsync(ct)
            || !await restaurantsRepository.IsEmptyAsync(ct) || !await assignmentsRepository.IsEmptyAsync(ct)) throw new InvalidOperationException();
        await store.AssertEmptyAsync(ct);
        var org = await platform.CreateOrganizationAsync(new("refund-" + options.RunId[..8], "Refund Acceptance", "Etc/UTC"), actor, ct);
        var other = await platform.CreateOrganizationAsync(new("other-" + options.RunId[..8], "Other Tenant", "Etc/UTC"), actor, ct);
        await platform.RegisterProductAsync(new("nexa_connect", "NexaConnect"), actor, ct);
        foreach (Guid id in new[] { org.OrganizationId, other.OrganizationId })
        {
            if (!await platform.ChangeProductAccessAsync(id, new("nexa_connect", "enabled"), actor, ct)) throw new InvalidOperationException();
            foreach (string subject in new[] { options.Reader, options.Manager })
                if (!await platform.ChangeMembershipAsync(id, subject, new(subject, "active"), actor, ct)) throw new InvalidOperationException();
        }
        var restaurants = new RestaurantProvisioningService(restaurantsRepository);
        var restaurant = await restaurants.CreateRestaurantAsync(new(org.OrganizationId, "refund", "Refund Restaurant", "THB", "Etc/UTC"), actor, ct);
        var branch = await restaurants.CreateBranchAsync(restaurant.RestaurantId, new("allowed", "Allowed", "THB", "Etc/UTC"), actor, ct) ?? throw new InvalidOperationException();
        var denied = await restaurants.CreateBranchAsync(restaurant.RestaurantId, new("denied", "Denied", "THB", "Etc/UTC"), actor, ct) ?? throw new InvalidOperationException();
        var assignments = new AuthorizationAssignmentService(assignmentsRepository);
        await assignments.AssignAsync(new(options.Reader, org.OrganizationId, restaurant.RestaurantId, branch.BranchId, "accountant"), actor, ct);
        await assignments.AssignAsync(new(options.Manager, org.OrganizationId, restaurant.RestaurantId, null, "store-manager"), actor, ct);
        await store.GrantBranchCreateAsync(options.Reader, ct);
        await store.LimitAsync(restaurant.RestaurantId, options.Manager, 100, ct);
        stage = "provider-backed-captures";
        var context = new PaymentMutationContext(actor, Guid.NewGuid());
        var intents = new PostgresPaymentIntents(paymentDb, providerOptions);
        var orders = new PostgresOrderRepository(orderDb);
        var ids = new List<Guid>();
        int count = options.Omise ? 3 : 5;
        for (int i = 0; i < count; i++)
        {
            Guid scope = i == 1 ? denied.BranchId : branch.BranchId;
            var intent = intents.Create(org.OrganizationId, new(restaurant.RestaurantId, scope, Guid.NewGuid(), Guid.NewGuid().ToString("D"), 100, "THB", "card"), context);
            intent = await new PaymentAuthorizationService(intents, provider).AuthorizeAsync(org.OrganizationId, intent.Id, context,
                options.Omise ? AcceptanceOptions.Required("TOKEN_" + i) : null, ct) ?? throw new InvalidOperationException();
            if (intent.Status != "authorized") throw new InvalidOperationException("Provider authorization did not complete.");
            intent = await new PaymentCaptureService(intents, provider).CaptureAsync(org.OrganizationId, intent.Id, context, ct) ?? throw new InvalidOperationException();
            if (intent.Status != "captured") throw new InvalidOperationException("Provider capture did not complete.");
            var order = OrderAggregate.Create(intent.OrderId, org.OrganizationId, scope,
                [new OrderLine(Guid.NewGuid(), "Acceptance sale", 100, 1, "kitchen")], "THB", restaurantId: restaurant.RestaurantId, workflowPaymentMethod: "card");
            order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted(); await orders.SaveAsync(order, ct);
            order.MarkPaid(intent.Id); order.IssueReceipt(DateTimeOffset.UtcNow, "card"); await orders.SaveAsync(order, ct);
            ids.Add(intent.Id);
        }
        await File.WriteAllTextAsync(options.StatePath, JsonSerializer.Serialize(new FixtureState(options.RunId, org.OrganizationId,
            other.OrganizationId, restaurant.RestaurantId, branch.BranchId, denied.BranchId, ids.ToArray(), DateTimeOffset.UtcNow.AddDays(-1)), json), ct);
        return 0;
    }
    var state = JsonSerializer.Deserialize<FixtureState>(await File.ReadAllTextAsync(options.StatePath, ct), json) ?? throw new InvalidOperationException();
    if (state.RunId != options.RunId || state.Intents.Length != (options.Omise ? 3 : 5)) throw new InvalidOperationException();
    stage = "live-authentication";
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    async Task<string> Token(string username, string password)
    {
        using var response = await http.PostAsync(options.Authority + "/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "password", ["client_id"] = "refund-acceptance", ["username"] = username, ["password"] = password }), ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Disposable identity login failed.");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return payload.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException();
    }
    string managerToken = await Token(AcceptanceOptions.Required("MANAGER_USERNAME"), AcceptanceOptions.Required("MANAGER_PASSWORD"));
    string readerToken = await Token(AcceptanceOptions.Required("READER_USERNAME"), AcceptanceOptions.Required("READER_PASSWORD"));
    var cases = new List<string>();
    void Passed(string name) { cases.Add(name); Console.WriteLine("passed: " + name); }
    async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, Guid intent, object? payload = null,
        string? token = null, Guid? organization = null, string suffix = "")
    {
        using var request = new HttpRequestMessage(method, options.PaymentUrl + $"api/payment/v1/intents/{intent:D}/refunds{suffix}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? managerToken);
        request.Headers.Add("X-Nexa-Organization-Id", (organization ?? state.OrganizationId).ToString());
        request.Headers.Add("X-Nexa-Application-Code", "nexa_connect"); request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, string.IsNullOrWhiteSpace(body) || response.Content.Headers.ContentType?.MediaType?.Contains("json") != true
            ? default : JsonDocument.Parse(body).RootElement.Clone());
    }
    object Command(Guid operation, decimal amount, string currency = "THB", string reasonCode = "customer_request") => new { operationId = operation, amount, currency, reasonCode };
    void Require(bool condition) { if (!condition) throw new InvalidOperationException("Acceptance assertion failed."); }
    void Status((HttpStatusCode Status, JsonElement Body) result, HttpStatusCode expected) { if (result.Status != expected) { Console.Error.WriteLine($"Unexpected HTTP status {(int)result.Status}; expected {(int)expected}."); Require(false); } }
    async Task Control(int index, string mode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.ProviderUrl + "acceptance/refunds/" + state.Intents[index]);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AcceptanceOptions.Required("PROVIDER_KEY"));
        request.Content = JsonContent.Create(new { mode }); using var response = await providerClient.SendAsync(request, ct); Require(response.IsSuccessStatusCode);
    }
    async Task Proof(Guid refund, bool recovered)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.ProviderUrl + "acceptance/refunds/" + refund);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AcceptanceOptions.Required("PROVIDER_KEY"));
        using var response = await providerClient.SendAsync(request, ct); Require(response.IsSuccessStatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Require(body.RootElement.GetProperty("commandRequests").GetInt32() == 1);
        if (recovered) Require(body.RootElement.GetProperty("statusRequests").GetInt32() >= 1);
    }
    var factory = new ConnectionFactory { Uri = new Uri(AcceptanceOptions.Required("BROKER")) };
    await using var broker = await factory.CreateConnectionAsync(ct);
    await using var channel = await broker.CreateChannelAsync(cancellationToken: ct);
    await channel.ExchangeDeclareAsync("nexaconnect.events", ExchangeType.Topic, durable: true, cancellationToken: ct);
    string evidenceQueue = "nexaconnect.acceptance.refunds." + options.RunId;
    await channel.QueueDeclareAsync(evidenceQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
    await channel.QueueBindAsync(evidenceQueue, "nexaconnect.events", "#", cancellationToken: ct);
    await using var host = new PaymentHost(options);
    await host.StartAsync(false, ct);
    stage = "runtime-privileges";
    await store.RuntimePrivilegesAsync(ct); Passed("Payment runtime reads version only and cannot mutate migration history");
    stage = "authorization-boundaries";
    Status(await Send(HttpMethod.Post, state.Intents[0], Command(Guid.NewGuid(), 10), readerToken), HttpStatusCode.Forbidden);
    Passed("permission without financial limit denied");
    Status(await Send(HttpMethod.Post, state.Intents[0], Command(Guid.NewGuid(), 100.01m)), HttpStatusCode.Forbidden);
    Passed("amount above approval limit denied");
    await store.LimitAsync(state.RestaurantId, options.Reader, 100, ct);
    Status(await Send(HttpMethod.Post, state.Intents[1], Command(Guid.NewGuid(), 10), readerToken), HttpStatusCode.Forbidden);
    Status(await Send(HttpMethod.Get, state.Intents[1], token: readerToken), HttpStatusCode.NotFound);
    Passed("branch isolation enforced on create and read");
    Status(await Send(HttpMethod.Post, state.Intents[0], Command(Guid.NewGuid(), 10), organization: state.OtherOrganizationId), HttpStatusCode.NotFound);
    Status(await Send(HttpMethod.Get, state.Intents[0], organization: state.OtherOrganizationId), HttpStatusCode.NotFound);
    Passed("foreign tenant resources undisclosed");
    stage = "partial-full-replay";
    Guid operation = Guid.NewGuid();
    var first = await Send(HttpMethod.Post, state.Intents[0], Command(operation, 25)); Status(first, HttpStatusCode.Created);
    Guid firstId = first.Body.GetProperty("id").GetGuid();
    Require(first.Body.GetProperty("receipt").GetProperty("cumulativeRefundedAmount").GetDecimal() == 25);
    Passed("partial refund returns immutable receipt");
    var replay = await Send(HttpMethod.Post, state.Intents[0], Command(operation, 25)); Status(replay, HttpStatusCode.Created);
    Require(replay.Body.GetProperty("id").GetGuid() == firstId);
    if (!options.Omise) await Proof(firstId, false);
    Passed(options.Omise ? "identical operation replay retains original refund identity" : "identical operation replay sends one provider command");
    foreach (object changed in new[] { Command(operation, 26), Command(operation, 25, reasonCode: "other") })
        Status(await Send(HttpMethod.Post, state.Intents[0], changed), HttpStatusCode.Conflict);
    Passed("changed operation payload conflicts");
    var full = await Send(HttpMethod.Post, state.Intents[0], Command(Guid.NewGuid(), 75)); Status(full, HttpStatusCode.Created);
    Require(full.Body.GetProperty("receipt").GetProperty("cumulativeRefundedAmount").GetDecimal() == 100);
    Status(await Send(HttpMethod.Post, state.Intents[0], Command(Guid.NewGuid(), 1)), HttpStatusCode.Conflict);
    Passed("full remaining refund prevents over-refund");
    stage = "concurrent-total";
    var races = await Task.WhenAll(Send(HttpMethod.Post, state.Intents[1], Command(Guid.NewGuid(), 60)), Send(HttpMethod.Post, state.Intents[1], Command(Guid.NewGuid(), 60)));
    Require(races.Count(x => x.Status == HttpStatusCode.Created) == 1 && races.Count(x => x.Status == HttpStatusCode.Conflict) == 1);
    Passed("concurrent refunds serialize captured balance");
    stage = "response-loss-restart";
    Guid recoveryOperation = Guid.NewGuid();
    if (!options.Omise) await Control(2, "drop_response");
    var uncertain = await Send(HttpMethod.Post, state.Intents[2], Command(recoveryOperation, 25));
    Status(uncertain, options.Omise ? HttpStatusCode.Created : HttpStatusCode.Accepted);
    Guid recoveredId = uncertain.Body.GetProperty("id").GetGuid();
    if (!options.Omise) Require(uncertain.Body.GetProperty("status").GetString() == "refund_unknown");
    // The initiating response is deliberately discarded. Recovery must use the protected operation identity.
    await host.StopAsync(); await host.StartAsync(true, ct);
    JsonElement recovered;
    while (true)
    {
        var lookup = await Send(HttpMethod.Get, state.Intents[2], suffix: "/by-operation/" + recoveryOperation); Status(lookup, HttpStatusCode.OK);
        recovered = lookup.Body; if (recovered.GetProperty("status").GetString() == "completed") break;
        await Task.Delay(200, ct);
    }
    Require(recovered.GetProperty("id").GetGuid() == recoveredId && recovered.GetProperty("receipt").ValueKind == JsonValueKind.Object);
    if (!options.Omise) await Proof(recoveredId, true);
    Passed(options.Omise ? "completed provider refund survives host restart and response discard" : "lost provider response recovers after Payment restart without command replay");
    int completed = 4; decimal refunded = 185;
    if (!options.Omise)
    {
        stage = "failed-and-uncertain-reservations";
        await Control(3, "fail");
        var failure = await Send(HttpMethod.Post, state.Intents[3], Command(Guid.NewGuid(), 10)); Status(failure, HttpStatusCode.Accepted);
        Require(failure.Body.GetProperty("status").GetString() == "failed"); await Control(3, "normal");
        Status(await Send(HttpMethod.Post, state.Intents[3], Command(Guid.NewGuid(), 10)), HttpStatusCode.Created);
        Passed("definitive failure releases reservation for new operation"); completed++; refunded += 10;
        await Control(4, "unknown_status");
        var unknown = await Send(HttpMethod.Post, state.Intents[4], Command(Guid.NewGuid(), 20)); Status(unknown, HttpStatusCode.Accepted);
        Guid unknownId = unknown.Body.GetProperty("id").GetGuid();
        while (true)
        {
            var read = await Send(HttpMethod.Get, state.Intents[4], suffix: "/" + unknownId); Status(read, HttpStatusCode.OK);
            if (read.Body.GetProperty("status").GetString() == "review_required") break;
            await Task.Delay(300, ct);
        }
        Status(await Send(HttpMethod.Post, state.Intents[4], Command(Guid.NewGuid(), 81)), HttpStatusCode.Conflict);
        await Proof(unknownId, true); Passed("exhausted ambiguity remains reserved for review");
    }
    await store.ImmutableAsync(firstId, ct); Passed("completed receipt update and deletion rejected");
    stage = "broker-financial-reporting";
    await store.VerifyAsync(state.OrganizationId, completed, refunded, state.Intents.Length, ct);
    foreach (var scope in new[] { (state.BranchId, 125m + (options.Omise ? 0 : 10)), (state.DeniedBranchId, 60m) })
    {
        string path = options.ReportingUrl + $"api/reporting/v1/customer/organizations/{state.OrganizationId}/reports/sales?branchId={scope.Item1}&fromUtc={Uri.EscapeDataString(state.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Authorization = new("Bearer", managerToken);
        using var response = await http.SendAsync(request, ct); Require(response.IsSuccessStatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        decimal gross = (state.Intents.Length - 1) * 100;
        Require(body.RootElement.GetProperty("refundedAmount").GetDecimal() == scope.Item2);
        Require(body.RootElement.GetProperty("netSales").GetDecimal() == (scope.Item1 == state.BranchId ? gross : 100) - scope.Item2);
    }
    Passed("real outbox consumers reconcile branch refund and net-sales totals");
    stage = "live-revocation";
    await store.RevokeAsync(options.Manager, ct);
    Status(await Send(HttpMethod.Post, state.Intents[2], Command(Guid.NewGuid(), 1)), HttpStatusCode.Forbidden);
    Passed("revoked permission rejects existing authenticated session");
    if (!await platform.ChangeMembershipAsync(state.OrganizationId, options.Reader, new(options.Reader, "suspended"), actor, ct)) throw new InvalidOperationException();
    Status(await Send(HttpMethod.Get, state.Intents[0], token: readerToken), HttpStatusCode.NotFound);
    Passed("revoked membership removes receipt access");
    Require(cases.Count == (options.Omise ? 15 : 17) && cases.Distinct().Count() == cases.Count);
    await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "matrix.json"), JsonSerializer.Serialize(new
    { runId = options.RunId, passed = cases.Count, total = cases.Count, verified = true, externalProviderVerified = options.Omise, cases }, json), ct);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Refund acceptance failed at {stage} ({exception.GetType().Name}); sensitive details suppressed.");
    return 1;
}

internal sealed record FixtureState(string RunId, Guid OrganizationId, Guid OtherOrganizationId, Guid RestaurantId, Guid BranchId, Guid DeniedBranchId, Guid[] Intents, DateTimeOffset FromUtc);
internal sealed record AcceptanceOptions(string RunId, string Reader, string Manager, string StatePath, string Root, bool Omise)
{
    public static string Required(string key) => Environment.GetEnvironmentVariable("NEXACONNECT_REFUND_" + key) ?? throw new ArgumentException("Missing acceptance setting.");
    public string Authority => Local("AUTHORITY", "http").TrimEnd('/');
    public string PaymentUrl => Local("PAYMENT_URL", "http");
    public string ReportingUrl => Local("REPORTING_URL", "http");
    public string ProviderUrl => Omise ? "https://api.omise.co/" : Local("PROVIDER_URL", "https");
    internal static string Local(string key, string scheme)
    { var uri = new Uri(Required(key)); if (uri.Scheme != scheme || uri.Host != "127.0.0.1" || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException(); return uri.AbsoluteUri; }
    public string Connection(string suffix)
    {
        if (suffix is not ("platform" or "restaurant" or "authorization" or "order" or "payment" or "reporting")) throw new ArgumentException();
        string value = Required("DB_" + suffix.ToUpperInvariant()); var parsed = new NpgsqlConnectionStringBuilder(value);
        if (parsed.Host != "127.0.0.1" || parsed.Database != $"nexa_review_it_{RunId}_{suffix}" || !string.IsNullOrEmpty(parsed.SearchPath)) throw new ArgumentException();
        return value;
    }
    public static AcceptanceOptions Read()
    {
        string run = Required("RUN_ID"), reader = Required("READER_SUBJECT"), manager = Required("MANAGER_SUBJECT");
        if (Required("ENABLED") != "1" || Required("CONFIRM_DISPOSABLE") != "1" || !System.Text.RegularExpressions.Regex.IsMatch(run, "^[a-f0-9]{32}$")
            || !Guid.TryParse(reader, out var r) || r == Guid.Empty || !Guid.TryParse(manager, out var m) || m == Guid.Empty || r == m) throw new ArgumentException();
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "NexaConnect.sln"))) root = root.Parent;
        if (root is null) throw new ArgumentException();
        string path = Path.GetFullPath(Required("STATE_PATH"));
        if (path != Path.Combine(root.FullName, ".runstate", "refund-hosted", run, "fixture.json")) throw new ArgumentException();
        bool omise = Required("ADAPTER") == "Omise";
        if (!omise && Required("ADAPTER") != "GenericHttp") throw new ArgumentException();
        var options = new AcceptanceOptions(run, reader, manager, path, root.FullName, omise);
        foreach (string suffix in new[] { "platform", "restaurant", "authorization", "order", "payment", "reporting" }) options.Connection(suffix);
        if (!options.Authority.EndsWith("/realms/nexa-review-it-" + run, StringComparison.Ordinal)) throw new ArgumentException();
        _ = options.PaymentUrl; _ = options.ReportingUrl; _ = options.ProviderUrl;
        foreach (string name in new[] { "PLATFORMDIRECTORY", "AUTHORIZATION", "RESTAURANT", "ORDER" }) Local("SERVICE_" + name, "http");
        var broker = new Uri(Required("BROKER")); if (broker.Scheme != "amqp" || broker.Host != "127.0.0.1") throw new ArgumentException();
        if (omise && (Required("CONFIRM_OMISE_TEST") != "1" || !OmisePaymentProvider.IsTestSecret(Required("PROVIDER_KEY")))) throw new ArgumentException();
        return options;
    }
}

internal sealed class PaymentHost(AcceptanceOptions options) : IAsyncDisposable
{
    private Process? process;
    private Task<string>? output, error;
    public async Task StartAsync(bool recover, CancellationToken ct)
    {
        string marker = Path.Combine(Path.GetDirectoryName(options.StatePath)!, "payment-cleanup.json");
        if (File.Exists(marker)) File.Delete(marker);
        string directory = Path.Combine(options.Root, "src/Services/NexaConnect.Services.Payment");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(directory, "bin/Debug/net10.0/NexaConnect.Services.Payment.dll"));
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("NEXACONNECT_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("PaymentProvider__", StringComparison.OrdinalIgnoreCase) || key.StartsWith("OmiseWebhooks__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        foreach (string key in new[] { "Authority", "Audience", "RequireHttpsMetadata" }) start.Environment["Authentication__" + key] = key switch { "Authority" => options.Authority, "Audience" => "nexaconnect-api", _ => "false" };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing"; start.Environment["DOTNET_ENVIRONMENT"] = "Testing"; start.Environment["ASPNETCORE_URLS"] = options.PaymentUrl;
        start.Environment["Persistence__Provider"] = "PostgreSQL"; start.Environment["ConnectionStrings__Payment"] = options.Connection("payment");
        foreach (string name in new[] { "PlatformDirectory", "Authorization", "Restaurant", "Order" }) start.Environment["Services__" + name] = AcceptanceOptions.Required("SERVICE_" + name.ToUpperInvariant());
        start.Environment["WorkloadIdentity__Authority"] = options.Authority; start.Environment["WorkloadIdentity__ClientId"] = "nexaconnect-payment-service";
        start.Environment["WorkloadIdentity__ClientSecret"] = AcceptanceOptions.Required("CLIENT_SECRET");
        start.Environment["PaymentProvider__Adapter"] = options.Omise ? "Omise" : "GenericHttp";
        start.Environment["PaymentProvider__BaseUrl"] = options.ProviderUrl;
        start.Environment[options.Omise ? "PaymentProvider__OmiseSecretKey" : "PaymentProvider__ApiKey"] = AcceptanceOptions.Required("PROVIDER_KEY");
        if (!options.Omise) start.Environment["PaymentProvider__SimulatorCertificateSha256"] = AcceptanceOptions.Required("SIMULATOR_PIN");
        start.Environment["PaymentProvider__RequestTimeout"] = "00:00:10"; start.Environment["PaymentProvider__LeaseDuration"] = options.Omise ? "00:00:55" : "00:00:15";
        start.Environment["PaymentProvider__RecoveryInterval"] = "00:00:01"; start.Environment["PaymentProvider__MaximumRefundRecoveryAttempts"] = "2";
        foreach (string kind in new[] { "Authorization", "Capture", "Void" }) start.Environment["PaymentProvider__" + kind + "RecoveryEnabled"] = "false";
        start.Environment["PaymentProvider__RefundRecoveryEnabled"] = recover.ToString(); start.Environment["OmiseWebhooks__Enabled"] = "false";
        start.Environment["Outbox__Enabled"] = "true"; start.Environment["Outbox__ConnectionString"] = AcceptanceOptions.Required("BROKER");
        start.Environment["Outbox__Exchange"] = "nexaconnect.events"; start.Environment["Outbox__PollInterval"] = "00:00:01";
        process = Process.Start(start) ?? throw new InvalidOperationException(); output = process.StandardOutput.ReadToEndAsync(); error = process.StandardError.ReadToEndAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (true)
        {
            ct.ThrowIfCancellationRequested(); if (process.HasExited) throw new InvalidOperationException("Payment host exited.");
            try { using var response = await client.GetAsync(options.PaymentUrl + "health/ready", ct); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(200, ct);
        }
    }
    public async Task StopAsync()
    {
        if (process is null) return;
        if (!process.HasExited) { process.Kill(true); if (!process.WaitForExit(10000)) throw new InvalidOperationException("Payment cleanup timed out."); }
        await File.AppendAllTextAsync(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "payment.log"), await output!);
        await File.AppendAllTextAsync(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "payment.error.log"), await error!);
        process.Dispose(); process = null;
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "payment-cleanup.json"),
            JsonSerializer.Serialize(new { runId = options.RunId, cleanupVerified = true }));
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
