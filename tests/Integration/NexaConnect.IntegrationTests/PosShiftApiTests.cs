extern alias POS;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using IAuthorizationDecisionClient = POS::NexaConnect.Services.POS.Application.Shifts.IAuthorizationDecisionClient;
using IRestaurantScopeReader = POS::NexaConnect.Services.POS.Application.Shifts.IRestaurantScopeReader;
using IShiftStore = POS::NexaConnect.Services.POS.Application.Shifts.IShiftStore;
using ICashSessionStore = POS::NexaConnect.Services.POS.Infrastructure.Persistence.ICashSessionStore;
using ITerminalStore = POS::NexaConnect.Services.POS.Infrastructure.Persistence.ITerminalStore;
using PosUserContext = POS::NexaConnect.Services.POS.Application.Shifts.PosUserContext;
using RestaurantAuthorizationScope = POS::NexaConnect.Services.POS.Application.Shifts.RestaurantAuthorizationScope;
using AuthorizationDecision = POS::NexaConnect.Services.POS.Application.Shifts.AuthorizationDecision;
using ShiftSnapshot = POS::NexaConnect.Services.POS.Application.Shifts.ShiftSnapshot;
using Shift = POS::NexaConnect.Services.POS.Domain.Shifts.Shift;
using ShiftStatus = POS::NexaConnect.Services.POS.Domain.Shifts.ShiftStatus;
using PosProgram = POS::PosProgram;

namespace NexaConnect.IntegrationTests;

public sealed class PosShiftApiTests : IClassFixture<PosShiftApiFactory>
{
    private readonly PosShiftApiFactory _factory;

    public PosShiftApiTests(PosShiftApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Open_requires_a_valid_access_token()
    {
        using var client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/pos/v1/shifts/open",
            Request());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Open_rejects_invalid_request_at_the_api_boundary()
    {
        using var client = AuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/pos/v1/shifts/open",
            new { branchId = Guid.Empty, storeId = Guid.Empty, terminalId = Guid.Empty, shiftNumber = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Open_and_close_use_the_application_workflow()
    {
        _factory.Reset();
        using var client = AuthenticatedClient();

        HttpResponseMessage open = await client.PostAsJsonAsync(
            "/api/pos/v1/shifts/open",
            Request());

        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var opened = await open.Content.ReadFromJsonAsync<OpenResponse>();
        Assert.NotNull(opened);
        Assert.NotEqual(Guid.Empty, opened.ShiftId);

        HttpResponseMessage close = await client.PostAsync(
            $"/api/pos/v1/shifts/{opened.ShiftId}/close",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.True(_factory.Store.WasClosed(opened.ShiftId));
    }

    [Fact]
    public async Task Sign_in_open_and_close_shift_end_to_end()
    {
        _factory.Reset();
        using var client = _factory.CreateClient();

        HttpResponseMessage beforeSignIn = await client.PostAsJsonAsync(
            "/api/pos/v1/shifts/open",
            Request("SHIFT-E2E-001"));
        Assert.Equal(HttpStatusCode.Unauthorized, beforeSignIn.StatusCode);

        // The factory's signed test token represents the successful identity-provider sign-in.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.SignIn());

        HttpResponseMessage open = await client.PostAsJsonAsync(
            "/api/pos/v1/shifts/open",
            Request("SHIFT-E2E-001"));
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var opened = await open.Content.ReadFromJsonAsync<OpenResponse>();
        Assert.NotNull(opened);

        HttpResponseMessage close = await client.PostAsync(
            $"/api/pos/v1/shifts/{opened!.ShiftId:D}/close",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.True(_factory.Store.WasClosed(opened.ShiftId));
    }

    [Fact]
    public async Task Sign_in_enrolls_terminal_and_manages_cash_session_lifecycle()
    {
        _factory.Reset();
        using var client = AuthenticatedClient();

        HttpResponseMessage enroll = await client.PostAsJsonAsync(
            "/api/pos/v1/terminals/enroll",
            new
            {
                branchId = PosShiftApiFactory.BranchId,
                storeId = PosShiftApiFactory.StoreId,
                terminalId = PosShiftApiFactory.TerminalId,
                code = "POS-001",
                deviceType = "pos"
            });
        Assert.Equal(HttpStatusCode.Created, enroll.StatusCode);
        Assert.True(_factory.Terminals.WasEnrolled(PosShiftApiFactory.TerminalId));

        Guid shiftId = Guid.NewGuid();
        HttpResponseMessage open = await client.PostAsJsonAsync(
            "/api/pos/v1/cash-sessions/open",
            new { shiftId, storeId = PosShiftApiFactory.StoreId, currency = "USD", openingAmount = 100m });
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var opened = await open.Content.ReadFromJsonAsync<CashSessionResponse>();
        Assert.NotNull(opened);

        HttpResponseMessage movement = await client.PostAsJsonAsync(
            $"/api/pos/v1/cash-sessions/{opened!.CashSessionId:D}/movements",
            new { movementType = "pay_in", amount = 25m, reasonCode = "FLOAT" });
        Assert.Equal(HttpStatusCode.Accepted, movement.StatusCode);

        HttpResponseMessage close = await client.PostAsJsonAsync(
            $"/api/pos/v1/cash-sessions/{opened.CashSessionId:D}/close",
            new { actualClosingAmount = 125m });
        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.True(_factory.CashSessions.WasClosed(opened.CashSessionId));
    }

    [Fact]
    public async Task Open_maps_unavailable_restaurant_to_service_unavailable()
    {
        _factory.Reset();
        _factory.ScopeReader.Fail = true;
        using var client = AuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/pos/v1/shifts/open",
            Request());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("identity.tests", await response.Content.ReadAsStringAsync());
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateToken());
        return client;
    }

    private static object Request(string shiftNumber = "SHIFT-001") => new
    {
        branchId = PosShiftApiFactory.BranchId,
        storeId = PosShiftApiFactory.StoreId,
        terminalId = PosShiftApiFactory.TerminalId,
        shiftNumber
    };

    private sealed record OpenResponse(Guid ShiftId, Guid AuthorizationDecisionId);
    private sealed record CashSessionResponse(Guid CashSessionId, string OpenedBy);
}

public sealed class PosShiftApiFactory : WebApplicationFactory<PosProgram>
{
    public const string Issuer = "https://identity.tests/realms/nexa-test";
    public const string Audience = "nexaconnect-api";
    public const string Subject = "550e8400-e29b-41d4-a716-446655440000";
    public static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid RestaurantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid BranchId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid StoreId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid TerminalId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly RSA _signingKey = RSA.Create(2048);
    internal InMemoryShiftStore Store { get; } = new();
    internal InMemoryCashSessionStore CashSessions { get; } = new();
    internal InMemoryTerminalStore Terminals { get; } = new();
    internal TestScopeReader ScopeReader { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = Audience,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["ConnectionStrings:POS"] = "Host=localhost;Database=unused"
            });
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(new RsaSecurityKey(_signingKey));
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(_signingKey),
                    ClockSkew = TimeSpan.Zero
                };
            });
            services.RemoveAll<IShiftStore>();
            services.RemoveAll<ICashSessionStore>();
            services.RemoveAll<ITerminalStore>();
            services.RemoveAll<IRestaurantScopeReader>();
            services.RemoveAll<IAuthorizationDecisionClient>();
            services.AddSingleton<IShiftStore>(Store);
            services.AddSingleton<ICashSessionStore>(CashSessions);
            services.AddSingleton<ITerminalStore>(Terminals);
            services.AddSingleton<IRestaurantScopeReader>(ScopeReader);
            services.AddSingleton<IAuthorizationDecisionClient, TestAuthorizationClient>();
        });
    }

    public string CreateToken()
    {
        DateTime now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim("sub", Subject),
            new Claim("preferred_username", "integration-test-user")
        };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(_signingKey),
                SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string SignIn() => CreateToken();

    public void Reset()
    {
        Store.Reset();
        CashSessions.Reset();
        Terminals.Reset();
        ScopeReader.Fail = false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _signingKey.Dispose();
        }
    }
}

internal sealed class TestScopeReader : IRestaurantScopeReader
{
    public bool Fail { get; set; }

    public Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new HttpRequestException("identity.tests dependency unavailable");
        }

        return Task.FromResult(new RestaurantAuthorizationScope(
            PosShiftApiFactory.OrganizationId,
            PosShiftApiFactory.RestaurantId,
            branchId));
    }
}

internal sealed class TestAuthorizationClient : IAuthorizationDecisionClient
{
    public Task<AuthorizationDecision> DecideAsync(
        PosUserContext user,
        RestaurantAuthorizationScope scope,
        string permission,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AuthorizationDecision(Guid.NewGuid(), true, null));
}

internal sealed class InMemoryTerminalStore : ITerminalStore
{
    private readonly HashSet<Guid> _enrolled = [];

    public Task<bool> EnrollAsync(
        Guid organizationId,
        Guid restaurantId,
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        string code,
        string deviceType,
        CancellationToken cancellationToken)
    {
        _enrolled.Add(terminalId);
        return Task.FromResult(true);
    }

    public bool WasEnrolled(Guid terminalId) => _enrolled.Contains(terminalId);

    public void Reset() => _enrolled.Clear();
}

internal sealed class InMemoryCashSessionStore : ICashSessionStore
{
    private readonly Dictionary<Guid, CashSessionState> _sessions = [];

    public Task<Guid> OpenAsync(
        Guid shiftId,
        Guid storeId,
        string currency,
        decimal openingAmount,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        _sessions[id] = new CashSessionState(shiftId, storeId, openingAmount, openingAmount, false);
        return Task.FromResult(id);
    }

    public Task RecordMovementAsync(
        Guid cashSessionId,
        string movementType,
        decimal amount,
        string recordedBy,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(cashSessionId, out CashSessionState? session) || session.Closed)
        {
            throw new InvalidOperationException("The cash session is not open.");
        }

        decimal signedAmount = movementType is "sale" or "pay_in" or "float_adjustment" ? amount : -amount;
        _sessions[cashSessionId] = session with { ExpectedAmount = session.ExpectedAmount + signedAmount };
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid cashSessionId, decimal actualClosingAmount, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(cashSessionId, out CashSessionState? session) || session.Closed)
        {
            throw new InvalidOperationException("The cash session is missing or already closed.");
        }

        _sessions[cashSessionId] = session with { Closed = true };
        return Task.CompletedTask;
    }

    public bool WasClosed(Guid cashSessionId) =>
        _sessions.TryGetValue(cashSessionId, out CashSessionState? session) && session.Closed;

    public void Reset() => _sessions.Clear();

    private sealed record CashSessionState(
        Guid ShiftId,
        Guid StoreId,
        decimal OpeningAmount,
        decimal ExpectedAmount,
        bool Closed);
}

internal sealed class InMemoryShiftStore : IShiftStore
{
    private readonly Dictionary<Guid, ShiftSnapshot> _shifts = [];

    public bool WasClosed(Guid shiftId) =>
        _shifts.TryGetValue(shiftId, out ShiftSnapshot? shift) && shift.Status == ShiftStatus.Closed;

    public Task<bool> TerminalMatchesAsync(
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        Guid restaurantId,
        CancellationToken cancellationToken) => Task.FromResult(true);

    public Task CreateAsync(Shift shift, CancellationToken cancellationToken)
    {
        _shifts[shift.Id] = Snapshot(shift, ShiftStatus.Open, null, null, null, 1);
        return Task.CompletedTask;
    }

    public Task<ShiftSnapshot?> FindOpenAsync(Guid shiftId, CancellationToken cancellationToken) =>
        Task.FromResult(_shifts.TryGetValue(shiftId, out ShiftSnapshot? shift) && shift.Status == ShiftStatus.Open
            ? shift
            : null);

    public Task<bool> TryCloseAsync(Shift shift, CancellationToken cancellationToken)
    {
        if (!_shifts.TryGetValue(shift.Id, out ShiftSnapshot? current) ||
            current.ConcurrencyVersion != shift.ConcurrencyVersion - 1)
        {
            return Task.FromResult(false);
        }

        _shifts[shift.Id] = current with
        {
            Status = ShiftStatus.Closed,
            ClosedAtUtc = shift.ClosedAtUtc,
            ClosedBy = shift.ClosedBy,
            CloseAuthorizationDecisionId = shift.CloseAuthorizationDecisionId,
            ConcurrencyVersion = shift.ConcurrencyVersion
        };
        return Task.FromResult(true);
    }

    public void Reset() => _shifts.Clear();

    private static ShiftSnapshot Snapshot(
        Shift shift,
        ShiftStatus status,
        DateTimeOffset? closedAt,
        string? closedBy,
        Guid? closeDecision,
        long version) => new(
            shift.Id,
            shift.StoreId,
            shift.TerminalId,
            PosShiftApiFactory.RestaurantId,
            PosShiftApiFactory.BranchId,
            shift.EmployeeSubject,
            shift.ShiftNumber,
            status,
            shift.OpenedAtUtc,
            closedAt,
            shift.OpenedBy,
            closedBy,
            shift.AuthorizationDecisionId,
            closeDecision,
            version);
}
