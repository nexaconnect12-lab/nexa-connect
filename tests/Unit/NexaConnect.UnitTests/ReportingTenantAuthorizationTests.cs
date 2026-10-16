using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Infrastructure;
namespace NexaConnect.UnitTests;

public sealed class ReportingTenantAuthorizationTests
{
    [Fact]
    public async Task Owner_hierarchy_and_customer_token_reach_permission_decision_with_workload_scope_read()
    {
        var org=Guid.NewGuid();var restaurant=Guid.NewGuid();var branch=Guid.NewGuid();
        var handler=new Handler(async request=>{
            if(request.RequestUri!.Host=="directory.test")
            {
                Assert.Equal("Bearer customer",request.Headers.Authorization!.ToString());
                return new(HttpStatusCode.OK);
            }
            if(request.RequestUri.Host=="restaurant.test")
            {
                Assert.Equal("Bearer workload-token",request.Headers.Authorization!.ToString());
                Assert.EndsWith($"/branches/{branch:D}/authorization-scope",request.RequestUri.AbsolutePath);
                return new(HttpStatusCode.OK){Content=JsonContent.Create(new ReportingBranchScope(org,restaurant,branch))};
            }
            Assert.Equal("Bearer customer",request.Headers.Authorization!.ToString());
            using var body=System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(restaurant,body.RootElement.GetProperty("restaurantId").GetGuid());
            Assert.Equal(branch,body.RootElement.GetProperty("branchId").GetGuid());
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new{DecisionId=Guid.NewGuid(),Granted=true})};
        });
        var dependencies=new HttpReportingCustomerAuthorizer(new Clients(handler),new Tokens(),
            new ProductAuthorizationClient(new HttpClient(handler){BaseAddress=new Uri("https://authorization.test/")},NullLogger<ProductAuthorizationClient>.Instance));
        Assert.True(await new ReportingCustomerAuthorizer(dependencies).IsGrantedAsync(org,branch,"reporting.sales.read","Bearer customer",default));
    }
    [Theory][InlineData("tenant")][InlineData("branch")][InlineData("restaurant")][InlineData("missing")][InlineData("membership")]
    public async Task Missing_or_wrong_ownership_does_not_reach_permission_decision(string failure)
    {
        var org=Guid.NewGuid();var restaurant=Guid.NewGuid();var branch=Guid.NewGuid();
        var dependencies=new Access{Membership=failure!="membership",Scope=failure=="missing"?null:
            new(failure=="tenant"?Guid.NewGuid():org,failure=="restaurant"?Guid.Empty:restaurant,failure=="branch"?Guid.NewGuid():branch)};
        Assert.False(await new ReportingCustomerAuthorizer(dependencies).IsGrantedAsync(org,branch,"reporting.sales.read","Bearer customer",default));
        Assert.Equal(0,dependencies.Decisions);
    }
    [Theory][InlineData("nexaconnect-reporting-service",true,false)][InlineData("nexaconnect-order-service",true,true)][InlineData("nexaconnect-web-bff",false,false)]
    public async Task Reporting_workload_is_limited_to_branch_metadata_policy(string client,bool scope,bool general)
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddNexaConnectApiAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["Authentication:Authority"]="https://identity.test/realms/test",["Authentication:Audience"]="nexaconnect-api"}).Build());
        await using var provider=services.BuildServiceProvider();
        var authorization=provider.GetRequiredService<IAuthorizationService>();
        var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp",client)],"test"));
        Assert.Equal(scope,(await authorization.AuthorizeAsync(user,null,NexaAuthorizationPolicies.BranchScopeReader)).Succeeded);
        Assert.Equal(general,(await authorization.AuthorizeAsync(user,null,NexaAuthorizationPolicies.ServiceWorkload)).Succeeded);
    }
    private sealed class Access:IReportingAccessDependencies
    {
        public bool Membership=true;public ReportingBranchScope? Scope;public int Decisions;
        public Task<bool> HasOrganizationAccessAsync(Guid organization,string header,CancellationToken ct)=>Task.FromResult(Membership);
        public Task<ReportingBranchScope?> GetBranchScopeAsync(Guid branch,CancellationToken ct)=>Task.FromResult(Scope);
        public Task<bool> HasPermissionAsync(Guid organization,Guid? restaurant,Guid? branch,string permission,string header,CancellationToken ct){Decisions++;return Task.FromResult(true);}
    }
    private sealed class Tokens:IServiceWorkloadTokenProvider{public Task<string> GetAsync(CancellationToken ct)=>Task.FromResult("workload-token");}
    private sealed class Clients(HttpMessageHandler handler):IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(handler){BaseAddress=new Uri(name=="ReportingRestaurant"?"https://restaurant.test/":"https://directory.test/")};
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>response(request);}
}
