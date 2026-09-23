using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.CustomerBff.Application.Reporting;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController, Authorize(Policy = "CustomerSession"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("bff/customer/reports/cash-close")]
public sealed class CashCloseReportsController(TenantSelectionCookie cookie, BffAccessTokenService tokens,
    IConfiguration configuration, CustomerCashCloseReports reports, ILogger<CashCloseReportsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid branchId, [FromQuery] Guid storeId, [FromQuery] DateTimeOffset fromUtc,
        [FromQuery] DateTimeOffset toUtc, [FromQuery] int limit = 50, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var tenant = cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if (tenant is null || tenant.SubjectId != User.FindFirstValue("sub"))
        { logger.LogWarning("Cash-close BFF tenant session rejected"); return Unauthorized(); }
        var settings = configuration.GetRequiredSection("Bff");
        string? token = await tokens.GetValidAccessTokenAsync(HttpContext, "CustomerCookie", settings["Authority"]!, settings["ClientId"]!, settings["ClientSecret"]!, ct);
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        try
        {
            using var response = await reports.ReadAsync(tenant, token, new(branchId, storeId, fromUtc, toUtc, limit, cursor), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cash-close BFF returned status {StatusCode}", (int)response.StatusCode);
                return StatusCode((int)response.StatusCode, new { title = "Cash-close report unavailable." });
            }
            return Content(await response.Content.ReadAsStringAsync(ct), "application/json", System.Text.Encoding.UTF8);
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        { logger.LogWarning("Cash-close BFF dependency unavailable"); return StatusCode(503); }
    }
}
