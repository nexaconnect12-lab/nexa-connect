using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Orders;

namespace NexaConnect.Services.Order.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-manager,customer-user,customer-viewer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/order/v1/organizations/{organization:guid}/branches/{branch:guid}/orders")]
public sealed class OrderListController(OrderListService application, ILogger<OrderListController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organization, Guid branch, [FromQuery] Guid restaurantId,
        [FromQuery] string channel = "all", [FromQuery] string state = "all", [FromQuery] string? cursor = null,
        [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out var tenant) || tenant != organization
            || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect"
            || string.IsNullOrWhiteSpace(User.FindFirstValue("sub")))
        { logger.LogWarning("Order list tenant authorization denied"); return Forbid(); }
        try
        {
            return Ok(await application.ListAsync(new(organization, restaurantId, branch), channel, state,
                cursor, limit, Request.Headers.Authorization.ToString(), ct));
        }
        catch (UnauthorizedAccessException) { logger.LogWarning("Order list branch authorization denied"); return Forbid(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Order list unavailable; category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
