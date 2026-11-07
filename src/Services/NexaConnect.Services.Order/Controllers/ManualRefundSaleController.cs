using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.ManualTenders;

namespace NexaConnect.Services.Order.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/order/v1/orders/{orderId:guid}/manual-refund-sale")]
public sealed class ManualRefundSaleController(ManualTenderEvidence evidence, ILogger<ManualRefundSaleController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid orderId, [FromQuery] Guid restaurantId, [FromQuery] Guid branchId, CancellationToken ct)
    {
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organization)
            || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect") return Forbid();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var sale = await evidence.ReadRefundSaleAsync(organization, restaurantId, branchId, orderId,
                Request.Headers.Authorization.ToString(), deadline.Token);
            return sale is null ? NotFound() : Ok(sale);
        }
        catch (ArgumentException) { return BadRequest(); }
        catch (UnauthorizedAccessException) { logger.LogWarning("Manual refund sale boundary denied"); return NotFound(); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Manual refund sale evidence unavailable"); return StatusCode(503); }
    }
}
