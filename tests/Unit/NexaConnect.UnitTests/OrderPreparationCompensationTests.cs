using System.Net;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Authorization.Domain;
using NexaConnect.Services.Order.Application.Cancellations;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Controllers;
using NexaConnect.Services.Order.Infrastructure.Messaging;
using NexaConnect.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NexaConnect.UnitTests;

public sealed class OrderPreparationCompensationTests
{
    [Theory]
    [InlineData("cashier")][InlineData("store-manager")][InlineData("tenant-admin")]
    public void NewOperationalAssignmentsIncludeExistingCancellationAuthority(string role)
        => Assert.Contains("order.cancel", ProductRoleDefaults.PermissionsFor(role));

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task StockIsReleasedOnlyAfterConfirmedKitchenCancellation(bool blocked)
    {
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),[new OrderLine(Guid.NewGuid(),"Meal",80,1,"kitchen")],"THB");
        var calls=new List<string>();var inventory=new Inventory(calls);var kitchen=new Kitchen(calls,blocked);
        if(blocked)await Assert.ThrowsAsync<OrderCancellationConflictException>(()=>OrderPreparationCompensation.CancelAndReleaseAsync(order,inventory,kitchen,default));
        else await OrderPreparationCompensation.CancelAndReleaseAsync(order,inventory,kitchen,default);
        Assert.Equal(blocked?new[]{"cancel"}:new[]{"cancel","release"},calls);
        Assert.Equal(order.OrganizationId,kitchen.Organization);Assert.Equal(order.Id,kitchen.Order);Assert.Equal(order.BranchId,kitchen.Branch);
    }
    [Fact]
    public async Task PlacementReturnsPreparationReviewConflictWithoutFailingOrPayingTheOrder()
    {
        Guid product=Guid.NewGuid(),org=Guid.NewGuid(),branch=Guid.NewGuid(),restaurant=Guid.NewGuid();
        var calls=new List<string>();var repository=new Repository();var menu=new Menu(product);
        var workflow=new PlaceOrderWorkflow(menu,new Inventory(calls),new Kitchen(calls,true),new Payment(),repository,new InMemoryIntegrationEventPublisher());
        var controller=new OrderWorkflowController(workflow,new Authorizer(),new OrderPricingService(menu,new Pricing()),NullLogger<OrderWorkflowController>.Instance)
        {ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        controller.Request.Headers[TenantContextHeaders.OrganizationId]=org.ToString("D");
        controller.Request.Headers[TenantContextHeaders.ApplicationCode]="nexa_connect";
        controller.Request.Headers.Authorization="Bearer test-only";
        var result=await controller.Place(new(restaurant,org,branch,"THB","card",Guid.NewGuid().ToString("D"),[new(product,1)]),default);
        var conflict=Assert.IsType<ConflictObjectResult>(result.Result);Assert.Equal(409,conflict.StatusCode);
        Assert.Contains("preparation_requires_review",System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        Assert.Equal(OrderStatus.KitchenAccepted,Assert.IsType<OrderAggregate>(repository.Order).Status);
        Assert.Equal(new[]{"cancel"},calls);
    }
    private sealed class Repository:IOrderRepository
    {
        public OrderAggregate? Order{get;private set;}
        public Task SaveAsync(OrderAggregate order,CancellationToken ct){Order=order;return Task.CompletedTask;}
    }
    private sealed class Menu(Guid product):IMenuCatalogPort
    {
        public Task<IReadOnlyDictionary<Guid,CatalogMenuItem>> GetItemsAsync(Guid branch,IReadOnlyCollection<Guid> products,CancellationToken ct)
            =>Task.FromResult<IReadOnlyDictionary<Guid,CatalogMenuItem>>(new Dictionary<Guid,CatalogMenuItem>{{product,new(product,"Meal",80,"THB",true,"kitchen")}});
    }
    private sealed class Pricing:IBranchPricingPort
    {public Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)=>Task.FromResult(new PricingPolicy(1,0,false,0));}
    private sealed class Authorizer:IOrderTenantAuthorizer
    {public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string permission,string header,CancellationToken ct)=>Task.FromResult(true);}
    private sealed class Payment:IPaymentPort
    {public Task<PaymentResult> AuthorizeAsync(Guid org,Guid restaurant,Guid branch,Guid order,decimal amount,string currency,string method,CancellationToken ct)=>Task.FromResult(new PaymentResult(false,Guid.NewGuid(),"declined","failed"));}
    private sealed class Inventory(List<string> calls):IInventoryReservationPort
    {
        public Task<InventoryReservationResult> ReserveAsync(Guid org,Guid order,Guid branch,IReadOnlyCollection<OrderLine> lines,CancellationToken ct)=>Task.FromResult(new InventoryReservationResult(true,Guid.NewGuid(),null));
        public Task ReleaseAsync(Guid org,Guid order,Guid branch,CancellationToken ct){calls.Add("release");return Task.CompletedTask;}
    }
    private sealed class Kitchen(List<string> calls,bool blocked):IKitchenPort
    {
        public Guid Organization{get;private set;}public Guid Order{get;private set;}public Guid Branch{get;private set;}
        public Task<KitchenTicketResult> CreateTicketAsync(Guid org,Guid restaurant,Guid order,Guid branch,IReadOnlyCollection<OrderLine> lines,CancellationToken ct)=>Task.FromResult(new KitchenTicketResult(Guid.NewGuid()));
        public Task CancelTicketAsync(Guid org,Guid order,Guid branch,CancellationToken ct){calls.Add("cancel");Organization=org;Order=order;Branch=branch;if(blocked)throw new OrderCancellationConflictException("Terminal preparation");return Task.CompletedTask;}
    }
}
