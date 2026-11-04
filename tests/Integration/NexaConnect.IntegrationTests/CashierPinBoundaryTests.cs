extern alias POS;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.POS;
using POS::NexaConnect.Services.POS.Application.CashierPins;
using POS::NexaConnect.Services.POS.Application.Shifts;
using POS::NexaConnect.Services.POS.Controllers;
using POS::NexaConnect.Services.POS.Domain.CashierPins;
using POS::NexaConnect.Services.POS.Domain.Shifts;

namespace NexaConnect.IntegrationTests;

public sealed class CashierPinBoundaryTests
{
    [Fact]
    public async Task Authentication_live_permission_tenant_and_terminal_boundaries_precede_credential_access()
    {
        var p=new Ports(); await using var app=await Host(p); using var client=app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(p.Path)).StatusCode);
        Identify(client); p.Granted=false; Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(p.Path)).StatusCode);
        p.Granted=true; Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(p.Path.Replace(p.Organization.ToString(),Guid.NewGuid().ToString()))).StatusCode);
        p.TerminalMatches=false; Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(p.Path)).StatusCode);
        Assert.Equal(0,p.Reads); Assert.Equal(0,p.Writes);
    }
    [Fact]
    public async Task Credential_lifecycle_is_private_scoped_and_versioned_with_separate_administrator_authority()
    {
        var p=new Ports(); await using var app=await Host(p); using var client=app.GetTestClient(); Identify(client);
        using var status=await client.GetAsync(p.Path); Assert.True(status.Headers.CacheControl?.NoStore);
        Assert.Equal("unset",(await status.Content.ReadFromJsonAsync<CashierPinStatusV1>())!.State);
        using var enrolled=await client.PutAsJsonAsync(p.Root,new CashierPinChangeV1(p.Scope,"826491",null,0,Guid.NewGuid()));
        Assert.True((await enrolled.Content.ReadFromJsonAsync<CashierPinResultV1>())!.Accepted);
        Assert.DoesNotContain("826491",await enrolled.Content.ReadAsStringAsync());
        Assert.Equal("cashier",p.LastKey!.Subject); Assert.Equal("https://configured/realm",p.LastKey.Issuer);
        using var changed=await client.PutAsJsonAsync(p.Root,new CashierPinChangeV1(p.Scope,"123456","000000",1,Guid.NewGuid()));
        Assert.False((await changed.Content.ReadFromJsonAsync<CashierPinResultV1>())!.Accepted);
        using var denied=await client.PostAsJsonAsync(p.Root+"/staff",new CashierPinAdminV1(p.Scope,"cashier",1,Guid.NewGuid(),"revoke"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        p.Manage=true;
        using var revoked=await client.PostAsJsonAsync(p.Root+"/staff",new CashierPinAdminV1(p.Scope,"cashier",1,Guid.NewGuid(),"revoke"));
        Assert.True((await revoked.Content.ReadFromJsonAsync<CashierPinResultV1>())!.Accepted);
        using var verify=await client.PostAsJsonAsync(p.Root+"/verify",new CashierPinVerifyV1(p.Scope,"826491",1,Guid.NewGuid()));
        Assert.False((await verify.Content.ReadFromJsonAsync<CashierPinResultV1>())!.Accepted);
    }
    [Fact]
    public async Task Validation_safe_dependency_failure_and_rate_limiting_fail_closed()
    {
        var p=new Ports(); await using var app=await Host(p); using var client=app.GetTestClient(); Identify(client);
        using var invalid=await client.PutAsJsonAsync(p.Root,new CashierPinChangeV1(p.Scope,"１２３４５６",null,0,Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode); Assert.Equal(0,p.Writes);
        p.Unavailable=true; using var failed=await client.GetAsync(p.Path); Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);
        Assert.DoesNotContain("private dependency",await failed.Content.ReadAsStringAsync()); p.Unavailable=false;
        for(int i=0;i<28;i++) await client.GetAsync(p.Path);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await client.GetAsync(p.Path)).StatusCode);
    }
    private static void Identify(HttpClient client)
    { client.DefaultRequestHeaders.Authorization=new("Bearer","synthetic"); client.DefaultRequestHeaders.Add("X-Test-Subject","cashier"); }
    private static async Task<WebApplication> Host(Ports p)
    {
        var builder=WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Services.AddLogging(l=>l.ClearProviders());
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Identity>("Test",_=>{}); builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(CashierPinsController).Assembly);
        builder.Services.AddSingleton<ICashierPinRepository>(p); builder.Services.AddSingleton<ICashierPinProtector>(p);
        builder.Services.AddSingleton<IRestaurantScopeReader>(p); builder.Services.AddSingleton<IShiftStore>(p); builder.Services.AddSingleton<IAuthorizationDecisionClient>(p);
        builder.Services.AddSingleton(new CashierPinAuthority("https://configured/realm")); builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddScoped<CashierPins>();
        builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("cashier-pins",c=>RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirst("sub")?.Value??"anonymous",_=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
        var app=builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers(); await app.StartAsync(); return app;
    }
    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.ContainsKey("X-Test-Subject")
            ?AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub",Request.Headers["X-Test-Subject"].ToString())],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
    }
    // In-memory test ports only. Real encryption, concurrency and durability are verified separately.
    private sealed class Ports:ICashierPinRepository,ICashierPinProtector,IRestaurantScopeReader,IShiftStore,IAuthorizationDecisionClient
    {
        public Guid Organization=Guid.NewGuid(),Restaurant=Guid.NewGuid(); public CashierPinScopeV1 Scope=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        public bool Granted=true,TerminalMatches=true,Manage,Unavailable; public int Reads,Writes; public CashierPinKey? LastKey;
        private readonly CashierPinCredential credential=new();
        public string Root=>$"/api/pos/v1/customer/organizations/{Organization}/cashier-pin";
        public string Path=>Root+$"?branchId={Scope.BranchId}&storeId={Scope.StoreId}&terminalId={Scope.TerminalId}";
        private CashierPinStatusV1 Status=>new(credential.State,credential.Version,credential.LockedUntilUtc);
        public Task<CashierPinStatusV1> ReadAsync(CashierPinKey key,CancellationToken ct){if(Unavailable)throw new HttpRequestException("private dependency");Reads++;LastKey=key;return Task.FromResult(Status);}
        public Task<CashierPinResultV1> MutateAsync(CashierPinKey key,CashierPinAudit audit,long version,Func<CashierPinCredential,bool> transition,CancellationToken ct){Writes++;LastKey=key;bool accepted=transition(credential);return Task.FromResult(new CashierPinResultV1(accepted,Status));}
        public string Create(CashierPinKey key,string pin)=>pin;
        public bool Matches(CashierPinKey key,string value,string pin)=>value==pin;
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new RestaurantAuthorizationScope(Organization,Restaurant,Scope.BranchId));
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user,RestaurantAuthorizationScope scope,string permission,CancellationToken ct)=>Task.FromResult(new AuthorizationDecision(Guid.NewGuid(),Granted&&(permission!="pos.cashier-pin.manage"||Manage),null));
        public Task<bool> TerminalMatchesAsync(Guid branch,Guid store,Guid terminal,Guid restaurant,CancellationToken ct)=>Task.FromResult(TerminalMatches&&branch==Scope.BranchId&&store==Scope.StoreId&&terminal==Scope.TerminalId&&restaurant==Restaurant);
        public Task CreateAsync(Shift shift,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ShiftSnapshot?> FindOpenAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> TryCloseAsync(Shift shift,CancellationToken ct)=>throw new NotSupportedException();
    }
}
