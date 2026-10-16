using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Controllers;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/reporting/v1/customer/organizations/{organizationId:guid}/reports/cash-corrections")]
public sealed class CashCorrectionReportsController(CashCorrectionReporting reports, ILogger<CashCorrectionReportsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, [FromQuery] Guid branchId,
        [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(User.FindFirst("sub")?.Value)
                || !Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out var tenant) || tenant != organizationId
                || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect") throw new UnauthorizedAccessException();
            return Ok(await reports.ReadAsync(new(organizationId, branchId, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime()), Request.Headers.Authorization.ToString(), ct));
        }
        catch (UnauthorizedAccessException) { logger.LogWarning("Cash correction report authorization denied"); return Forbid(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Cash correction report unavailable; category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
