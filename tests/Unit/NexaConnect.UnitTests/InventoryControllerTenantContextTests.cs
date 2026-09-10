using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Inventory.Application.Reservations;
using NexaConnect.Services.Inventory.Application.Tenant;
using NexaConnect.Services.Inventory.Controllers;

namespace NexaConnect.UnitTests;

public sealed class InventoryControllerTenantContextTests
{
    [Fact]
    public async Task Trusted_order_workload_with_tenant_headers_uses_tenant_reservation_path()
    {
        Guid organizationId=Guid.NewGuid(),branchId=Guid.NewGuid(),orderId=Guid.NewGuid(),productId=Guid.NewGuid();
        var inventory=new RecordingInventory();
        var controller=new InventoryController(inventory,new DeniedAuthorizer())
        {
            ControllerContext=new ControllerContext{HttpContext=Context(organizationId)}
        };

        ActionResult<StockReservation> response=await controller.Reserve(branchId,new ReserveRequest(orderId,[new ReservationLine(productId,1)]),CancellationToken.None);

        Assert.IsType<CreatedResult>(response.Result);
        Assert.Equal(organizationId,inventory.OrganizationId);
        Assert.Equal(0,inventory.LegacyCalls);
    }

    private static DefaultHttpContext Context(Guid organizationId)
    {
        var context=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp","nexaconnect-order-service")],"test")),TraceIdentifier=Guid.NewGuid().ToString("D")};
        context.Request.Headers[TenantContextHeaders.OrganizationId]=organizationId.ToString("D");
        context.Request.Headers[TenantContextHeaders.ApplicationCode]="nexa_connect";
        return context;
    }

    private sealed class RecordingInventory:IInventoryReservations
    {
        public Guid? OrganizationId{get;private set;} public int LegacyCalls{get;private set;}
        public IReadOnlyCollection<StockItem> GetStock(Guid branchId)=>[];
        public StockItem SetStock(Guid branchId,Guid productId,decimal quantity)=>new(productId,quantity);
        public StockReservation Reserve(ReserveStock command){LegacyCalls++;return new(Guid.NewGuid(),command.OrderId,command.BranchId,command.Lines);}
        public StockReservation Reserve(Guid organizationId,ReserveStock command,InventoryMutationContext? context=null){OrganizationId=organizationId;return new(Guid.NewGuid(),command.OrderId,command.BranchId,command.Lines);}
    }

    private sealed class DeniedAuthorizer:IInventoryTenantAuthorizer
    {
        public Task<bool> HasBranchAccessAsync(Guid organizationId,Guid branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)=>Task.FromResult(false);
    }
}
