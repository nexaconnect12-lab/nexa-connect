using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Inventory.Application.Reservations;
using NexaConnect.Services.Inventory.Application.Tenant;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using System.Security.Claims;

namespace NexaConnect.Services.Inventory.Controllers;

[ApiController]
[Route("api/inventory/v1/branches/{branchId:guid}")]
public sealed class InventoryController(IInventoryReservations inventory, IInventoryTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpGet("stock")]
    public async Task<ActionResult<IReadOnlyCollection<StockItem>>> GetStock(Guid branchId, CancellationToken cancellationToken)
    {
        Guid? organizationId = await GetCustomerOrganizationAsync(branchId, ProductPermissions.InventoryStockRead, cancellationToken);
        if (organizationId == Guid.Empty) return Forbid();
        return Ok(organizationId is Guid tenant ? inventory.GetStock(tenant, branchId) : inventory.GetStock(branchId));
    }

    [HttpPut("stock/{productId:guid}")]
    public async Task<ActionResult<StockItem>> SetStock(Guid branchId, Guid productId, SetStockRequest request, CancellationToken cancellationToken)
    {
        Guid? organizationId = await GetCustomerOrganizationAsync(branchId, ProductPermissions.InventoryStockWrite, cancellationToken);
        if (organizationId == Guid.Empty) return Forbid();
        try { return Ok(organizationId is Guid tenant ? inventory.SetStock(tenant, branchId, productId, request.Quantity, MutationContext()) : inventory.SetStock(branchId, productId, request.Quantity)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("reservations")]
    public async Task<ActionResult<StockReservation>> Reserve(Guid branchId, ReserveRequest request, CancellationToken cancellationToken)
    {
        Guid? organizationId = await GetCustomerOrganizationAsync(branchId, ProductPermissions.InventoryReservationCreate, cancellationToken);
        if (organizationId == Guid.Empty) return Forbid();
        try
        {
            return Created($"/api/inventory/v1/reservations/{request.OrderId}",
                organizationId is Guid tenant ? inventory.Reserve(tenant, new ReserveStock(request.OrderId, branchId, request.Lines), MutationContext()) : inventory.Reserve(new ReserveStock(request.OrderId, branchId, request.Lines)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpPost("reservations/{orderId:guid}/release")]
    public async Task<IActionResult> Release(Guid branchId, Guid orderId, CancellationToken cancellationToken)
    {
        Guid? organizationId = await GetCustomerOrganizationAsync(branchId, ProductPermissions.InventoryReservationRelease, cancellationToken);
        if (organizationId == Guid.Empty) return Forbid();
        if (organizationId is Guid tenant) inventory.Release(tenant, orderId, MutationContext()); else inventory.Release(orderId);
        return NoContent();
    }

    private async Task<bool> HasCustomerAccessAsync(Guid branchId, string permission, CancellationToken cancellationToken)
    {
        if (ServiceWorkloadPrincipal.IsTrusted(User)) return true;
        return Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
            && string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await tenantAuthorizer.HasBranchAccessAsync(organizationId, branchId, permission, authorization.ToString(), cancellationToken);
    }

    private async Task<Guid?> GetCustomerOrganizationAsync(Guid branchId, string permission, CancellationToken cancellationToken)
    {
        if (ServiceWorkloadPrincipal.IsTrusted(User)) return null;
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)) return Guid.Empty;
        return await HasCustomerAccessAsync(branchId, permission, cancellationToken) ? organizationId : Guid.Empty;
    }
    private InventoryMutationContext MutationContext()=>new(User.FindFirstValue("sub")??User.FindFirstValue("azp")??"trusted-workload",Guid.TryParse(HttpContext.TraceIdentifier,out Guid id)?id:Guid.NewGuid());
}

public sealed record SetStockRequest(decimal Quantity);
public sealed record ReserveRequest(Guid OrderId, IReadOnlyCollection<ReservationLine> Lines);
