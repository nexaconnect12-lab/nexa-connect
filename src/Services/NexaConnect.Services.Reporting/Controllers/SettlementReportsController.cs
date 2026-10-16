using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Controllers;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/reporting/v1/customer/organizations/{organizationId:guid}/reports/day-settlements")]
public sealed class SettlementReportsController(SettlementReporting reports, ILogger<SettlementReportsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(Guid organizationId, [FromQuery] Guid branchId,
        [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct) =>
        Read(organizationId, branchId, fromDate, toDate, false, ct);

    [HttpGet("export.csv")]
    public Task<IActionResult> Export(Guid organizationId, [FromQuery] Guid branchId,
        [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct) =>
        Read(organizationId, branchId, fromDate, toDate, true, ct);

    private async Task<IActionResult> Read(Guid organizationId, Guid branchId, DateOnly fromDate,
        DateOnly toDate, bool export, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(User.FindFirst("sub")?.Value)
                || !Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out var tenant) || tenant != organizationId
                || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect") throw new UnauthorizedAccessException();
            var range = new NexaConnect.Contracts.Reporting.SettlementReportRange(organizationId, branchId, fromDate, toDate);
            if (export)
                return File(await reports.ExportAsync(range, Request.Headers.Authorization.ToString(), ct),
                    "text/csv; charset=utf-8", SettlementCsv.FileName(branchId, fromDate, toDate));
            return Ok(await reports.ReadAsync(range, Request.Headers.Authorization.ToString(), ct));
        }
        catch (UnauthorizedAccessException) { logger.LogWarning("Settlement report authorization denied"); return Forbid(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Settlement report unavailable; category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
