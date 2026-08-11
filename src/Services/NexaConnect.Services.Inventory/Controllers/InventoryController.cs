using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Inventory.Application.Reservations;
using NexaConnect.Services.Inventory.Application.Tenant;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Inventory.Controllers;

[ApiController]
[Route("api/inventory/v1/branches/{branchId:guid}")]
public sealed class InventoryController(IInventoryReservations inventory, IInventoryTenantAuthorizer tenantAuthorizer) : ControllerBase
{
    [HttpGet("stock")]
    public async Task<ActionResult<IReadOnlyCollection<StockItem>>> GetStock(Guid branchId, CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(branchId, cancellationToken)) return Forbid();
        return Ok(inventory.GetStock(branchId));
    }

    [HttpPut("stock/{productId:guid}")]
    public ActionResult<StockItem> SetStock(Guid branchId, Guid productId, SetStockRequest request)
    {
        try { return Ok(inventory.SetStock(branchId, productId, request.Quantity)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("reservations")]
    public async Task<ActionResult<StockReservation>> Reserve(Guid branchId, ReserveRequest request, CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(branchId, cancellationToken)) return Forbid();
        try
        {
            return Created($"/api/inventory/v1/reservations/{request.OrderId}",
                inventory.Reserve(new ReserveStock(request.OrderId, branchId, request.Lines)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpPost("reservations/{orderId:guid}/release")]
    public IActionResult Release(Guid orderId)
    {
        inventory.Release(orderId);
        return NoContent();
    }

    private async Task<bool> HasCustomerAccessAsync(Guid branchId, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(TenantContextHeaders.PortalRequest, out var portal)
            || !string.Equals(portal.ToString(), "customer", StringComparison.Ordinal)) return true;
        return Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
            && string.Equals(Request.Headers[TenantContextHeaders.ApplicationCode], "nexa_connect", StringComparison.Ordinal)
            && Request.Headers.TryGetValue("Authorization", out var authorization)
            && await tenantAuthorizer.HasBranchAccessAsync(organizationId, branchId, authorization.ToString(), cancellationToken);
    }
}

public sealed record SetStockRequest(decimal Quantity);
public sealed record ReserveRequest(Guid OrderId, IReadOnlyCollection<ReservationLine> Lines);
