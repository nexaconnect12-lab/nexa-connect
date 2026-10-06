extern alias POS;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Reporting;
using D=POS::NexaConnect.Services.POS.Domain.DayClose;
using A=POS::NexaConnect.Services.POS.Application.DayClose;
using S=POS::NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.IntegrationTests;

public sealed class DayCloseHttpTests
{
    private static readonly D.DayIdentity Day=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
    private static string Path=>$"/api/pos/v1/customer/organizations/{Day.OrganizationId:D}/day-close-preparations";
    private static D.PreparationCommand Command(long version=0)=>new(Day.BranchId,Day.BusinessDate,Guid.NewGuid(),version,"routine_close");
    [Fact]public async Task HTTP_preparation_revalidation_and_customer_correlation_use_the_reporting_adapter()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        using var anonymous=await client.GetAsync(Path+$"?branchId={Day.BranchId:D}&businessDate=2026-09-01");Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);Assert.True(anonymous.Headers.CacheControl?.NoStore);
        client.DefaultRequestHeaders.Authorization=new("Bearer","workload");using var workload=await client.PostAsJsonAsync(Path,Command());Assert.Equal(HttpStatusCode.Forbidden,workload.StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer","manager");client.DefaultRequestHeaders.Add("X-Correlation-ID","day-close-http-test");
        var command=Command();using var prepared=await client.PostAsJsonAsync(Path,command);prepared.EnsureSuccessStatusCode();var ready=await prepared.Content.ReadFromJsonAsync<A.PreparationView>();
        Assert.Equal("ready_for_review",ready!.Status);Assert.Equal(2,ready.Version);Assert.Equal("day-close-http-test",factory.Correlation);Assert.Equal("Bearer manager",factory.Token);Assert.Contains($"organizations/{Day.OrganizationId:D}/reports/end-of-day",factory.UpstreamPath);
        using var replay=await client.PostAsJsonAsync(Path,command);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(2,factory.Port.State!.Version);
        factory.Hash=new('d',64);using var changed=await client.GetAsync(Path+$"?branchId={Day.BranchId:D}&businessDate=2026-09-01");
        var blocked=await changed.Content.ReadFromJsonAsync<A.PreparationView>();Assert.Equal("blocked",blocked!.Status);Assert.Contains("source_evidence_changed",blocked.Blockers);Assert.Equal(new('a',64),blocked.Snapshot!.OrderVersion);
        using var stale=await client.PostAsJsonAsync(Path,Command(2));Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);Assert.True(stale.Headers.CacheControl?.NoStore);
        using var foreign=await client.GetAsync(Path.Replace(Day.OrganizationId.ToString("D"),Guid.NewGuid().ToString("D"))+$"?branchId={Day.BranchId:D}&businessDate=2026-09-01");Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer","accountant");using var read=await client.GetAsync(Path+$"?branchId={Day.BranchId:D}&businessDate=2026-09-01");Assert.False((await read.Content.ReadFromJsonAsync<A.PreparationView>())!.CanPrepare);
        using var denied=await client.PostAsJsonAsync(Path,Command(3));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [Theory][InlineData("wrong_scope")][InlineData("mixed_currency")][InlineData("missing_projection")][InlineData("source_outage")][InlineData("oversized")]
    public async Task Invalid_source_observations_fail_closed_and_do_not_expose_provider_details(string fault)
    {
        await using var factory=new Factory{Fault=fault};using var client=factory.CreateClient(new(){BaseAddress=new("https://localhost")});client.DefaultRequestHeaders.Authorization=new("Bearer","manager");
        using var response=await client.PostAsJsonAsync(Path,Command());Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        var view=await response.Content.ReadFromJsonAsync<A.PreparationView>();Assert.Equal("blocked",view!.Status);Assert.Contains("source_unavailable",view.Blockers);Assert.Null(view.Snapshot);Assert.DoesNotContain("restricted-source",await response.Content.ReadAsStringAsync());
    }
    private sealed class Factory:WebApplicationFactory<POS::PosProgram>
    {
        public Port Port=new();public string Hash=new('a',64),Fault="";public string? Correlation,Token,UpstreamPath;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder,"pos");
            builder.ConfigureServices(services=>
            {
                services.RemoveAll<A.IDayCloseStore>();services.AddSingleton<A.IDayCloseStore>(Port);
                services.RemoveAll<S.IRestaurantScopeReader>();services.AddSingleton<S.IRestaurantScopeReader>(Port);
                services.RemoveAll<S.IAuthorizationDecisionClient>();services.AddSingleton<S.IAuthorizationDecisionClient>(Port);
                services.AddHttpClient<A.IDayCloseEvidenceReader,POS::NexaConnect.Services.POS.Infrastructure.DayClose.HttpDayCloseEvidenceReader>().ConfigurePrimaryHttpMessageHandler(()=>new Handler(request=>
                {
                    Correlation=request.Headers.GetValues("X-Correlation-ID").Single();Token=request.Headers.Authorization!.ToString();UpstreamPath=request.RequestUri!.PathAndQuery;
                    if(Fault=="source_outage")return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("restricted-source")};
                    if(Fault=="oversized")return new(HttpStatusCode.OK){Content=new StringContent(new string('x',131073))};
                    var w=new EndOfDayWindow(Day.OrganizationId,Day.RestaurantId,Day.BranchId,new(2026,9,1,0,0,0,TimeSpan.Zero),new(2026,9,2,0,0,0,TimeSpan.Zero));var now=DateTimeOffset.UtcNow;
                    return new(HttpStatusCode.OK){Content=JsonContent.Create(new{businessDate=Day.BusinessDate,status="draft",branch=new{organizationId=Fault=="wrong_scope"?Guid.NewGuid():Day.OrganizationId,restaurantId=Day.RestaurantId,branchId=Day.BranchId,timeZone="UTC",currency=Fault=="mixed_currency"?"USD":"THB"},window=w,grossSales=100,completedRefunds=25,netSales=75,cashVariance=0,tenders=new[]{new{method="cash",currency="THB",amount=100}},
                        order=new OrderDaySummary(w,now,100,1,0,0,[new("cash","THB",100)],["THB"],Hash),payment=new PaymentDaySummary(w,now,25,0,0,0,["THB"],new('b',64)),pos=new PosDaySummary(w,now,0,0,0,0,["THB"],new('c',64)),issues=new[]{"recorded_check_is_historical"},projection=Fault=="missing_projection"?null:new{completedOrders=1,grossSales=100,refunded=25,currency="THB"},recordedCompleteness=new{checkId=Guid.NewGuid(),status="observed_complete",checkedAtUtc=now,range=new{organizationId=Day.OrganizationId,branchId=Day.BranchId,fromUtc=w.FromUtc,toUtc=w.ToUtc}}})};
                }));
                services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="PrepTest";o.DefaultChallengeScheme="PrepTest";o.DefaultForbidScheme="PrepTest";}).AddScheme<AuthenticationSchemeOptions,Auth>("PrepTest",_=>{});
            });
        }
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request));}
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(string.IsNullOrWhiteSpace(Request.Headers.Authorization))return Task.FromResult(AuthenticateResult.NoResult());
            string token=Request.Headers.Authorization.ToString().Split(' ')[1];string role=token=="workload"?"service-workload":token=="accountant"?"customer-viewer":"customer-manager";
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new("sub",token),new(ClaimTypes.Role,role)],Scheme.Name)),Scheme.Name)));
        }
    }
    private sealed class Port:A.IDayCloseStore,S.IRestaurantScopeReader,S.IAuthorizationDecisionClient
    {
        public D.PreparationState? State;private Guid? completed;
        public Task<S.RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new S.RestaurantAuthorizationScope(Day.OrganizationId,Day.RestaurantId,Day.BranchId));
        public Task<S.AuthorizationDecision> DecideAsync(S.PosUserContext user,S.RestaurantAuthorizationScope scope,string permission,CancellationToken ct)=>Task.FromResult(new S.AuthorizationDecision(Guid.NewGuid(),permission!=A.DayClosePreparation.PreparePermission||user.Subject!="accountant",null));
        public Task<D.PreparationState?> ReadAsync(D.DayIdentity day,CancellationToken ct)=>Task.FromResult(State);
        public Task<A.PreparationLease> BeginAsync(D.DayIdentity day,D.PreparationCommand command,A.PreparationActor actor,DateTimeOffset now,CancellationToken ct)
        {if(completed==command.OperationId)return Task.FromResult(new A.PreparationLease(State!,null));var aggregate=State is null?D.BranchDayClose.New(day,now):D.BranchDayClose.Restore(State);var claim=Guid.NewGuid();aggregate.Begin(command,actor.Subject,claim,now,false);State=aggregate.Export();return Task.FromResult(new A.PreparationLease(State,claim));}
        public Task<D.PreparationState> CompleteAsync(D.DayIdentity day,Guid claim,D.DayEvidence? evidence,A.PreparationActor actor,DateTimeOffset now,CancellationToken ct){completed=State!.OperationId;var aggregate=D.BranchDayClose.Restore(State);aggregate.Complete(claim,evidence,now);State=aggregate.Export();return Task.FromResult(State);}
        public Task<D.PreparationState> ValidateAsync(D.DayIdentity day,long version,D.DayEvidence? evidence,A.PreparationActor actor,DateTimeOffset now,CancellationToken ct){var aggregate=D.BranchDayClose.Restore(State!);aggregate.Validate(evidence,now);State=aggregate.Export();return Task.FromResult(State);}
    }
}
