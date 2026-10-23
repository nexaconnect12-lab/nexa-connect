extern alias RESTAURANT;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.RestaurantQr;
using RESTAURANT::NexaConnect.Services.Restaurant.Application.Qr;
using RESTAURANT::NexaConnect.Services.Restaurant.Application.Branches;
using RESTAURANT::NexaConnect.Services.Restaurant.Domain.Qr;

namespace NexaConnect.IntegrationTests;

public sealed class QrRestaurantBoundaryTests
{
    [Fact] public async Task Owning_http_boundary_requires_live_staff_authority_and_never_lists_secrets()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());var repo=factory.Repository;
        string root=$"/api/restaurant/v1/customer/organizations/{repo.Scope.OrganizationId}/branches/{repo.Scope.BranchId}/qr-entries";
        using(var denied=await client.GetAsync(root))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Staff","yes");client.DefaultRequestHeaders.Authorization=new("Bearer","test-staff");
        using(var denied=await client.PostAsJsonAsync(root,new CreateQrEntry("A1")))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        factory.Authorization.Allowed=true;
        var issue=await (await client.PostAsJsonAsync(root,new CreateQrEntry("A1"))).Content.ReadFromJsonAsync<QrEntryIssue>();Assert.NotNull(issue);
        using var list=await client.GetAsync(root);Assert.True(list.Headers.CacheControl?.NoStore);string json=await list.Content.ReadAsStringAsync();Assert.DoesNotContain(issue.EntryToken,json);Assert.DoesNotContain("secretHash",json);
        using(var wrong=await client.GetAsync(root.Replace(repo.Scope.OrganizationId.ToString(),Guid.NewGuid().ToString())))Assert.Equal(HttpStatusCode.Forbidden,wrong.StatusCode);
        using(var stale=await client.PostAsJsonAsync(root+$"/{issue.Entry.Id}/rotate",new ChangeQrEntry(0)))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        factory.Authorization.Allowed=false;using var revoked=await client.PostAsJsonAsync(root+$"/{issue.Entry.Id}/revoke",new ChangeQrEntry(1));Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);Assert.True(repo.Entry!.Active);
    }
    [Fact] public async Task Guest_http_boundary_uses_explicit_capability_without_staff_or_query_authority()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());
        using var start=await client.PostAsJsonAsync("/api/restaurant/v1/guest/qr/sessions",new QrEntryExchange(new string('E',43)));Assert.Equal(HttpStatusCode.OK,start.StatusCode);
        var issue=await start.Content.ReadFromJsonAsync<QrSessionIssue>();Assert.NotNull(issue);Assert.True(start.Headers.CacheControl?.NoStore);
        using(var missing=await client.GetAsync("/api/restaurant/v1/guest/qr/context?session="+issue.SessionToken))Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
        client.DefaultRequestHeaders.Add("X-Nexa-Guest-Session",issue.SessionToken);using var context=await client.GetAsync("/api/restaurant/v1/guest/qr/context");Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        using var staff=await client.GetAsync($"/api/restaurant/v1/customer/organizations/{factory.Repository.Scope.OrganizationId}/branches/{factory.Repository.Scope.BranchId}/qr-entries");Assert.Equal(HttpStatusCode.Unauthorized,staff.StatusCode);
    }
    [Fact] public async Task Disabled_owning_guest_boundary_does_not_admit_visitors()
    {
        await using var factory=new Factory(false);using var client=factory.CreateClient(Options());
        using var response=await client.PostAsJsonAsync("/api/restaurant/v1/guest/qr/sessions",new QrEntryExchange(new string('E',43)));Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);Assert.Equal(0,factory.Repository.Admissions);
    }
    private static WebApplicationFactoryClientOptions Options()=>new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false};
    private sealed class Factory(bool enabled=true):WebApplicationFactory<RESTAURANT::Program>
    {
        public Repo Repository{get;}=new();public Authorizer Authorization{get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Authentication:Authority"]="https://identity.test/realms/test",["QrEntry:Enabled"]=enabled.ToString(),["ConnectionStrings:Restaurant"]="Host=127.0.0.1;Database=unused",["Services:PlatformDirectory"]="https://directory.test/",["Services:Authorization"]="https://authorization.test/"}));
            builder.ConfigureTestServices(s=>{s.RemoveAll<IQrEntryRepository>();s.AddSingleton<IQrEntryRepository>(Repository);s.RemoveAll<IBranchCustomerAuthorizer>();s.AddSingleton<IBranchCustomerAuthorizer>(Authorization);s.AddAuthentication(o=>{o.DefaultAuthenticateScheme="qr-test";o.DefaultChallengeScheme="qr-test";o.DefaultForbidScheme="qr-test";}).AddScheme<AuthenticationSchemeOptions,StaffHandler>("qr-test",_=>{});});
        }
    }
    private sealed class StaffHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers["X-Test-Staff"]=="yes"?AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.Role,"customer-admin")],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
    }
    private sealed class Authorizer:IBranchCustomerAuthorizer {public bool Allowed;public Task<bool> IsGrantedAsync(Guid org,Guid? restaurant,Guid? branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Allowed&&bearer=="Bearer test-staff");}
    private sealed class Repo:IQrEntryRepository
    {
        public QrScope Scope=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Restaurant","Branch","THB");public QrEntry? Entry;public int Admissions;private string? session;
        public Task<QrScope?> ScopeAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult<QrScope?>(org==Scope.OrganizationId&&branch==Scope.BranchId?Scope:null);
        public Task<IReadOnlyList<QrEntry>> ListAsync(QrScope s,CancellationToken ct)=>Task.FromResult<IReadOnlyList<QrEntry>>(Entry is null?[]:[Entry]);
        public Task<QrEntry?> GetAsync(QrScope s,Guid id,CancellationToken ct)=>Task.FromResult(Entry?.Id==id?Entry:null);
        public Task SaveAsync(QrEntry e,long expected,string actor,DateTimeOffset now,CancellationToken ct){Entry=e;return Task.CompletedTask;}
        public Task<QrGuestContext?> AdmitAsync(string entryHash,string hash,Guid visitor,DateTimeOffset now,CancellationToken ct){Admissions++;session=hash;return Task.FromResult<QrGuestContext?>(Context(visitor,now));}
        public Task<QrGuestContext?> ContextAsync(string hash,DateTimeOffset now,CancellationToken ct)=>Task.FromResult<QrGuestContext?>(hash==session?Context(Guid.NewGuid(),now):null);
        public Task EndAsync(string hash,CancellationToken ct){session=null;return Task.CompletedTask;}
        private QrGuestContext Context(Guid visitor,DateTimeOffset now)=>new(visitor,Guid.NewGuid(),Scope.OrganizationId,Scope.RestaurantId,Scope.BranchId,"Restaurant","Branch","A1","THB",now.AddHours(2));
    }
}
