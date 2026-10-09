using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Security.Claims;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.CashReviews;

namespace NexaConnect.Services.POS.Controllers;

[ApiController, Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/end-of-day")]
public sealed class EndOfDayController(PosDayRead query, ILogger<EndOfDayController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] EndOfDayWindow window, CancellationToken ct)
    {
        string? subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (subject is null || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
            return Unauthorized();
        try { return Ok(await query.ReadAsync(window, new NexaConnect.Services.POS.Application.Shifts.PosUserContext(subject, header.Parameter), ct)); }
        catch (ArgumentException) { return BadRequest(); }
        catch (UnauthorizedAccessException) { logger.LogWarning("End-of-day source authorization denied"); return Forbid(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("End-of-day source unavailable, category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
