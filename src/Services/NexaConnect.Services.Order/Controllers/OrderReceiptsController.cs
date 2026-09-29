using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Orders;

namespace NexaConnect.Services.Order.Controllers;

[ApiController]
[Route("api/order/v1/orders/{orderId:guid}/receipt")]
public sealed class OrderReceiptsController(OrderReceiptService receipts, ILogger<OrderReceiptsController> logger) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid orderId, [FromQuery] Guid branchId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || branchId == Guid.Empty) return BadRequest();
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organizationId)
            || organizationId == Guid.Empty || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect"
            || string.IsNullOrWhiteSpace(Request.Headers.Authorization))
        {
            logger.LogWarning("Order receipt authorization denied");
            return Forbid();
        }
        try
        {
            var receipt = await receipts.ReadAsync(organizationId, branchId, orderId, Request.Headers.Authorization.ToString(), cancellationToken);
            return receipt is null ? NotFound() : Ok(receipt);
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Order receipt authorization denied");
            return Forbid();
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Order receipt authorization dependency unavailable");
            return StatusCode(503);
        }
    }
}
