using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Controllers;

[ApiController, Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
[Route("api/reporting/v1/customer/organizations/{organizationId:guid}/reports/end-of-day")]
public sealed class EndOfDayController(EndOfDayDraft query, ILogger<EndOfDayController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, [FromQuery] Guid branchId, [FromQuery] DateOnly businessDate, CancellationToken ct)
    {
        try { return Ok(await query.ReadAsync(organizationId, branchId, businessDate, Request.Headers.Authorization.ToString(), ct)); }
        catch (ArgumentException) { return BadRequest(new { title="Select a branch and completed business date with supported day boundaries." }); }
        catch (UnauthorizedAccessException) { logger.LogWarning("End-of-day draft authorization denied"); return Forbid(); }
        catch (MixedReportingCurrencyException) { return Conflict(new { title="Source currencies differ from the branch currency." }); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("End-of-day draft unavailable, category {Category}", e.GetType().Name); return StatusCode(503, new { title="End-of-day draft unavailable." }); }
    }
}
