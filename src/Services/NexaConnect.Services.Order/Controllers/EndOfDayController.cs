using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Order.Application.Orders;

namespace NexaConnect.Services.Order.Controllers;

[ApiController, Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
[Route("api/order/v1/customer/end-of-day")]
public sealed class EndOfDayController(OrderDayRead query, ILogger<EndOfDayController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] EndOfDayWindow window, CancellationToken ct)
    {
        try { return Ok(await query.ReadAsync(window, Request.Headers.Authorization.ToString(), ct)); }
        catch (ArgumentException) { return BadRequest(); }
        catch (UnauthorizedAccessException) { logger.LogWarning("End-of-day source authorization denied"); return Forbid(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("End-of-day source unavailable, category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
