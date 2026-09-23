using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Controllers;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/reporting/v1/customer/organizations/{organizationId:guid}/reports/cash-close")]
public sealed class CashCloseReportsController(CashCloseReporting reports, ILogger<CashCloseReportsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, [FromQuery] Guid branchId, [FromQuery] Guid storeId,
        [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc, [FromQuery] int limit = 50, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(User.FindFirstValue("sub")) ||
                !Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid tenant) || tenant != organizationId ||
                Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect") throw new UnauthorizedAccessException();
            return Ok(await reports.ReadAsync(organizationId, branchId, storeId, fromUtc, toUtc, limit, cursor, Request.Headers.Authorization.ToString(), ct));
        }
        catch (UnauthorizedAccessException) { logger.LogWarning("Cash-close report authorization denied"); return Forbid(); }
        catch (ArgumentException) { return BadRequest(new { title = "Invalid cash-close report scope, date range or cursor." }); }
        catch (Exception e) when (e is HttpRequestException or Npgsql.NpgsqlException or System.Text.Json.JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        { logger.LogWarning("Cash-close report dependency unavailable"); return StatusCode(503); }
    }
}
