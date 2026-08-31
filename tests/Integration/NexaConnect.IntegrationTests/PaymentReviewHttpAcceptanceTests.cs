extern alias ORDER;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Platform;
using ORDER::NexaConnect.Services.Order.Application.PaymentReviews;
using ORDER::NexaConnect.Services.Order.Application.Tenant;
using ORDER::NexaConnect.Services.Order.Application.Workflow;
using ORDER::NexaConnect.Services.Order.Domain;

namespace NexaConnect.IntegrationTests;

public sealed class PaymentReviewHttpAcceptanceTests : IClassFixture<RestaurantWorkflowServiceFixture>
{
    private readonly RestaurantWorkflowServiceFixture fixture;
    public PaymentReviewHttpAcceptanceTests(RestaurantWorkflowServiceFixture fixture)=>this.fixture=fixture;

    [Theory]
    [InlineData("operator",false,200)]
    [InlineData("denied",false,404)]
    [InlineData("operator",true,404)]
    public async Task History_is_branch_authorized_and_tenant_scoped(string token,bool otherTenant,int status)
    {
        var repository=new ReviewRepository();using var factory=CreateFactory(repository);using var client=factory.CreateClient();
        Guid organization=otherTenant?Guid.NewGuid():repository.Order.OrganizationId;
        using var request=Request(repository,HttpMethod.Get,organization,token);
        request.RequestUri=new Uri($"/api/order/v1/payment-reviews/{repository.Order.Id}/history?organizationId={organization}",UriKind.Relative);
        using var response=await client.SendAsync(request);Assert.Equal(status,(int)response.StatusCode);
        Assert.Equal(status==200?1:0,repository.HistoryReads);
        if(status==200)Assert.Empty((await response.Content.ReadFromJsonAsync<PaymentReviewHistoryEntry[]>())!);
    }

    [Theory]
    [InlineData("operator",true)]
    [InlineData("denied",false)]
    public async Task Branch_permission_probe_reflects_authorizer(string token,bool allowed)
    {
        var repository=new ReviewRepository();using var factory=CreateFactory(repository);using var client=factory.CreateClient();
        using var request=Request(repository,HttpMethod.Get,repository.Order.OrganizationId,token);
        request.RequestUri=new Uri($"/api/order/v1/payment-reviews/branches/{repository.Order.BranchId}/access?organizationId={repository.Order.OrganizationId}",UriKind.Relative);
        using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var body=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(allowed,body.GetProperty("canRead").GetBoolean());Assert.Equal(allowed,body.GetProperty("canResolve").GetBoolean());
    }

    [Fact]
    public async Task Cross_tenant_read_and_resolution_are_undiscoverable()
    {
        var repository=new ReviewRepository();using var factory=CreateFactory(repository);using var client=factory.CreateClient();
        Guid other=Guid.NewGuid();
        using var read=Request(repository,HttpMethod.Get,other);
        using var readResponse=await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.NotFound,readResponse.StatusCode);
        using var resolve=Request(repository,HttpMethod.Post,other);
        using var resolveResponse=await client.SendAsync(resolve);
        Assert.Equal(HttpStatusCode.NotFound,resolveResponse.StatusCode);
        Assert.Equal(0,repository.Claims);Assert.Null(repository.Audit);
    }

    [Fact]
    public async Task Denied_operator_cannot_resolve_an_existing_case()
    {
        var repository=new ReviewRepository();using var factory=CreateFactory(repository);using var client=factory.CreateClient();
        using var request=Request(repository,HttpMethod.Post,repository.Order.OrganizationId,token:"denied");
        using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(0,repository.Claims);Assert.Null(repository.Audit);
    }

    [Fact]
    public async Task Stale_http_decision_returns_conflict_without_claim_or_audit()
    {
        var repository=new ReviewRepository();using var factory=CreateFactory(repository);using var client=factory.CreateClient();
        using var request=Request(repository,HttpMethod.Post,repository.Order.OrganizationId,version:99);
        using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.Equal(0,repository.Claims);Assert.Null(repository.Audit);
        foreach(string status in new[]{"resolved","resolving"})
        {
            repository.SetStatus(status);
            using var closed=Request(repository,HttpMethod.Post,repository.Order.OrganizationId);
            using var closedResponse=await client.SendAsync(closed);
            Assert.Equal(HttpStatusCode.Conflict,closedResponse.StatusCode);Assert.Equal(0,repository.Claims);Assert.Null(repository.Audit);
        }
    }

    [Fact]
    public async Task Resolution_attributes_actor_and_authorization_decision_to_server_context()
    {
        var repository=new ReviewRepository();using var factory=CreateFactory(repository);using var client=factory.CreateClient();
        Guid correlation=Guid.NewGuid();
        using var request=Request(repository,HttpMethod.Post,repository.Order.OrganizationId,correlation:correlation);
        using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("integration-test-user",repository.Audit!.SubjectId);
        Assert.Equal(ReviewAuthorizer.DecisionId,repository.Resolved!.AuthorizationDecisionId);
        Assert.Equal(correlation,repository.Resolved.CorrelationId);
        Assert.Equal(repository.Order.OrganizationId,repository.Audit.OrganizationId);
        Assert.Equal(1,repository.Claims);Assert.Equal(OrderStatus.PaymentPending,repository.Order.Status);
    }

    private WebApplicationFactory<ORDER::OrderProgram> CreateFactory(ReviewRepository repository)=>fixture.Order.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
    {
        services.RemoveAll<IOrderRepository>();services.AddSingleton<IOrderRepository>(repository);
        services.RemoveAll<IOrderTenantAuthorizer>();services.AddSingleton<IOrderTenantAuthorizer>(new ReviewAuthorizer(repository.Order));
    }));

    private static HttpRequestMessage Request(ReviewRepository repository,HttpMethod method,Guid organization,string token="operator",long version=1,Guid? correlation=null)
    {
        string path=$"/api/order/v1/payment-reviews/{repository.Order.Id:D}";
        var request=new HttpRequestMessage(method,method==HttpMethod.Get?$"{path}?organizationId={organization:D}":path+"/resolve");
        request.Headers.TryAddWithoutValidation("Authorization","Bearer "+token);
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId,organization.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode,"nexa_connect");
        if(method==HttpMethod.Post)request.Content=JsonContent.Create(new{OrganizationId=organization,Resolution="resume_payment",Reason="operator verified",ExpectedConcurrencyVersion=version,CorrelationId=correlation,ActorSubjectId="spoofed-actor",AuthorizationDecisionId=Guid.NewGuid()});
        return request;
    }

    private sealed class ReviewAuthorizer(OrderAggregate order):IOrderTenantAuthorizer
    {
        public static readonly Guid DecisionId=Guid.Parse("714655c5-10c9-4572-b2ca-5d27de9161f2");
        public Task<bool> HasBranchAccessAsync(Guid organizationId,Guid branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)=>Task.FromResult(organizationId==order.OrganizationId&&branchId==order.BranchId&&authorizationHeader=="Bearer operator");
        public async Task<Guid?> GetBranchDecisionAsync(Guid organizationId,Guid branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)=>await HasBranchAccessAsync(organizationId,branchId,permission,authorizationHeader,cancellationToken)?DecisionId:null;
    }

    private sealed class ReviewRepository:IOrderRepository,IOrderLookup,IPaymentReviewRepository,IPaymentReviewHistoryRepository
    {
        public OrderAggregate Order{get;}
        private PaymentReviewCase value;
        public void SetStatus(string status)=>value=value with{Status=status};
        public int Claims{get;private set;}
        public int HistoryReads{get;private set;}
        public Task<IReadOnlyCollection<PaymentReviewHistoryEntry>> ReadHistoryAsync(Guid organizationId,Guid orderId,CancellationToken ct){HistoryReads++;return Task.FromResult<IReadOnlyCollection<PaymentReviewHistoryEntry>>([]);}
        public PlatformAuditEventV1? Audit{get;private set;}
        public OrderPaymentReviewResolvedV1? Resolved{get;private set;}
        public ReviewRepository()
        {
            Guid intent=Guid.NewGuid();Order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),[new OrderLine(Guid.NewGuid(),"Meal",10m,1,"kitchen")],"USD");
            Order.Submit();Order.MarkInventoryReserved();Order.MarkKitchenAccepted();Order.MarkPaymentPending(intent);Order.MarkPaymentReview();
            value=new(Order.Id,Order.OrganizationId,Order.BranchId,intent,"open","void_failed",null,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        }
        public Task SaveAsync(OrderAggregate order,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<OrderAggregate?> GetAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult<OrderAggregate?>(id==Order.Id?Order:null);
        public Task<PaymentReviewCase?> GetReviewAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken)=>Task.FromResult<PaymentReviewCase?>(organizationId==value.OrganizationId&&orderId==value.OrderId?value:null);
        public Task<IReadOnlyCollection<PaymentReviewCase>> ListOpenAsync(Guid organizationId,Guid branchId,int limit,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyCollection<PaymentReviewCase>>(organizationId==value.OrganizationId&&branchId==value.BranchId?[value]:[]);
        public Task<Guid?> ClaimResolutionAsync(PaymentReviewCase review,string resolution,string actor,DateTimeOffset now,CancellationToken cancellationToken){Claims++;value=value with{Status="resolving",ConcurrencyVersion=value.ConcurrencyVersion+1};return Task.FromResult<Guid?>(Guid.NewGuid());}
        public Task ReleaseResolutionAsync(PaymentReviewCase review,Guid claimId,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<bool> ResolveAsync(OrderAggregate order,PaymentReviewCase review,string resolution,string reason,string actor,Guid claimId,OrderPaymentReviewResolvedV1 integrationEvent,PlatformAuditEventV1 audit,CancellationToken cancellationToken){Audit=audit;Resolved=integrationEvent;value=value with{Status="resolved",Resolution=resolution};return Task.FromResult(true);}
    }
}
