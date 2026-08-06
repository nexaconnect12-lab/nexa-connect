using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Inventory.Application.Reservations;

namespace NexaConnect.Services.Inventory.Controllers;

[ApiController]
[Route("api/inventory/v1/branches/{branchId:guid}")]
public sealed class InventoryController(IInventoryReservations inventory) : ControllerBase
{
    [HttpGet("stock")]
    public ActionResult<IReadOnlyCollection<StockItem>> GetStock(Guid branchId) => Ok(inventory.GetStock(branchId));

    [HttpPut("stock/{productId:guid}")]
    public ActionResult<StockItem> SetStock(Guid branchId, Guid productId, SetStockRequest request)
    {
        try { return Ok(inventory.SetStock(branchId, productId, request.Quantity)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("reservations")]
    public ActionResult<StockReservation> Reserve(Guid branchId, ReserveRequest request)
    {
        try
        {
            return Created($"/api/inventory/v1/reservations/{request.OrderId}",
                inventory.Reserve(new ReserveStock(request.OrderId, branchId, request.Lines)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }
}

public sealed record SetStockRequest(decimal Quantity);
public sealed record ReserveRequest(Guid OrderId, IReadOnlyCollection<ReservationLine> Lines);
