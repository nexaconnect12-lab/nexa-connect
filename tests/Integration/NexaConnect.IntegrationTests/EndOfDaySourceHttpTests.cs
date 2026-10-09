extern alias ORDER;
extern alias PAYMENT;
extern alias POS;
using System.Net;
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
using OrderApp=ORDER::NexaConnect.Services.Order.Application.Orders;
using OrderAccess=ORDER::NexaConnect.Services.Order.Application.Tenant;
using PaymentApp=PAYMENT::NexaConnect.Services.Payment.Application.Refunds;
using PaymentAccess=PAYMENT::NexaConnect.Services.Payment.Application.Tenant;
using PosApp=POS::NexaConnect.Services.POS.Application.CashReviews;
using PosAccess=POS::NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.IntegrationTests;

public sealed class EndOfDaySourceHttpTests
{
    private static readonly Guid Org=Guid.NewGuid(),Restaurant=Guid.NewGuid(),Branch=Guid.NewGuid();
    [Fact] public Task Order_source_transport_and_access_boundary()=>Verify(new Factory<ORDER::Program>("order"),"order");
    [Fact] public Task Payment_source_transport_and_access_boundary()=>Verify(new Factory<PAYMENT::Program>("payment"),"payment");
    [Fact] public Task Pos_source_transport_and_access_boundary()=>Verify(new Factory<POS::Program>("pos"),"pos");
    private static async Task Verify<T>(Factory<T> factory,string service) where T:class
    {
        await using(factory)
        {
            using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
            string path=$"/api/{service}/v1/customer/end-of-day?organizationId={Org}&restaurantId={Restaurant}&branchId={Branch}&fromUtc=2026-09-01T00:00:00Z&toUtc=2026-09-02T00:00:00Z";
            using var anonymous=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);Assert.True(anonymous.Headers.CacheControl?.NoStore);
            client.DefaultRequestHeaders.Authorization=new("Bearer","workload");using var workload=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,workload.StatusCode);
            client.DefaultRequestHeaders.Authorization=new("Bearer","customer");using var allowed=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);Assert.True(allowed.Headers.CacheControl?.NoStore);Assert.Equal(1,factory.Port.Reads);
            using var foreign=await client.GetAsync(path.Replace(Org.ToString(),Guid.NewGuid().ToString()));Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);Assert.Equal(1,factory.Port.Reads);
            using var foreignRestaurant=await client.GetAsync(path.Replace(Restaurant.ToString(),Guid.NewGuid().ToString()));Assert.Equal(HttpStatusCode.Forbidden,foreignRestaurant.StatusCode);Assert.Equal(1,factory.Port.Reads);
            using var invalid=await client.GetAsync(path.Replace("2026-09-02","2026-09-05"));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal(1,factory.Port.Reads);
            factory.Port.Unavailable=true;using var unavailable=await client.GetAsync(path);Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);Assert.DoesNotContain("restricted-db-detail",await unavailable.Content.ReadAsStringAsync());
        }
    }
    private sealed class Factory<T>(string service):WebApplicationFactory<T> where T:class
    {
        public Port Port=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder,service);
            builder.ConfigureServices(s=>
            {
                s.RemoveAll<OrderAccess.IOrderTenantAuthorizer>();s.AddSingleton<OrderAccess.IOrderTenantAuthorizer>(Port);s.AddSingleton<OrderApp.IOrderDayReader>(Port);
                s.RemoveAll<PaymentAccess.IPaymentTenantAuthorizer>();s.AddSingleton<PaymentAccess.IPaymentTenantAuthorizer>(Port);s.AddSingleton<PaymentApp.IPaymentDayReader>(Port);
                s.RemoveAll<PosAccess.IRestaurantScopeReader>();s.AddSingleton<PosAccess.IRestaurantScopeReader>(Port);
                s.RemoveAll<PosAccess.IAuthorizationDecisionClient>();s.AddSingleton<PosAccess.IAuthorizationDecisionClient>(Port);
                s.RemoveAll<PosApp.IPosDayReader>();s.AddSingleton<PosApp.IPosDayReader>(Port);
                s.AddAuthentication(o=>{o.DefaultAuthenticateScheme="DaySourceTest";o.DefaultChallengeScheme="DaySourceTest";o.DefaultForbidScheme="DaySourceTest";})
                    .AddScheme<AuthenticationSchemeOptions,Auth>("DaySourceTest",_=>{});
            });
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(string.IsNullOrWhiteSpace(Request.Headers.Authorization))return Task.FromResult(AuthenticateResult.NoResult());
            string role=Request.Headers.Authorization=="Bearer customer"?"customer-manager":"service-workload";
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.Role,role)],Scheme.Name)),Scheme.Name)));
        }
    }
    private sealed class Port:OrderAccess.IOrderTenantAuthorizer,PaymentAccess.IPaymentTenantAuthorizer,OrderApp.IOrderDayReader,PaymentApp.IPaymentDayReader,PosApp.IPosDayReader,PosAccess.IRestaurantScopeReader,PosAccess.IAuthorizationDecisionClient
    {
        public int Reads;public bool Unavailable;
        private void Read(){Reads++;if(Unavailable)throw new InvalidOperationException("restricted-db-detail");}
        public Task<bool> HasBranchAccessAsync(Guid o,Guid b,string p,string bearer,CancellationToken ct)=>Task.FromResult(o==Org&&b==Branch&&p=="order.read");
        public Task<bool> HasBranchFinancialAccessAsync(Guid o,Guid r,Guid b,string bearer,CancellationToken ct)=>Task.FromResult(o==Org&&r==Restaurant&&b==Branch);
        public Task<bool> CanReadBranchFinancialsAsync(Guid o,Guid r,Guid b,string bearer,CancellationToken ct)=>Task.FromResult(o==Org&&r==Restaurant&&b==Branch);
        public Task<bool> CanAccessAsync(Guid o,Guid r,Guid b,Guid id,string p,string bearer,CancellationToken ct)=>throw new NotSupportedException();
        Task<OrderDaySummary> OrderApp.IOrderDayReader.ReadAsync(EndOfDayWindow w,CancellationToken ct){Read();return Task.FromResult(new OrderDaySummary(w,DateTimeOffset.UtcNow,100,1,0,0,[new("cash","THB",100)],["THB"]));}
        Task<PaymentDaySummary> PaymentApp.IPaymentDayReader.ReadAsync(EndOfDayWindow w,CancellationToken ct){Read();return Task.FromResult(new PaymentDaySummary(w,DateTimeOffset.UtcNow,25,0,0,0,["THB"]));}
        Task<PosDaySummary> PosApp.IPosDayReader.ReadAsync(EndOfDayWindow w,CancellationToken ct){Read();return Task.FromResult(new PosDaySummary(w,DateTimeOffset.UtcNow,0,0,0,0,["THB"]));}
        public Task<PosAccess.RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new PosAccess.RestaurantAuthorizationScope(Org,Restaurant,Branch));
        public Task<PosAccess.AuthorizationDecision> DecideAsync(PosAccess.PosUserContext user,PosAccess.RestaurantAuthorizationScope scope,string permission,CancellationToken ct)
        {Assert.Equal("pos.cash-review.read",permission);Assert.Equal("customer",user.AccessToken);return Task.FromResult(new PosAccess.AuthorizationDecision(Guid.NewGuid(),true,null));}
    }
}
