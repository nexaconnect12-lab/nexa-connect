using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.CustomerBff.Application.Reporting;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController, Authorize(Policy = "CustomerSession"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("bff/customer/reports/day-settlements")]
public sealed class SettlementReportsController(TenantSelectionCookie cookie, BffAccessTokenService tokens,
    IConfiguration configuration, CustomerSettlementReports completeness, ILogger<SettlementReportsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get([FromQuery] Guid branchId, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken cancellationToken) =>
        Read(branchId, fromDate, toDate, false, cancellationToken);

    [HttpGet("export.csv")]
    public Task<IActionResult> Export([FromQuery] Guid branchId, [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate, CancellationToken cancellationToken) =>
        Read(branchId, fromDate, toDate, true, cancellationToken);

    private async Task<IActionResult> Read(Guid branchId, DateOnly fromDate, DateOnly toDate,
        bool export, CancellationToken cancellationToken)
    {
        var tenant = cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if (tenant is null || tenant.SubjectId != User.FindFirstValue("sub"))
        {
            logger.LogWarning("Settlement reporting BFF tenant session rejected");
            return Unauthorized();
        }
        try
        {
            var settings = configuration.GetRequiredSection("Bff");
            string? token = await tokens.GetValidAccessTokenAsync(HttpContext, "CustomerCookie",
                settings["Authority"]!, settings["ClientId"]!, settings["ClientSecret"]!, cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
            using var response = await completeness.ReadAsync(tenant, token, new(branchId, fromDate, toDate, export), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Settlement reporting BFF boundary rejected with status {StatusCode}", (int)response.StatusCode);
                return StatusCode((int)response.StatusCode, new ProblemDetails { Title = "Settlement reporting observation is unavailable.", Status = (int)response.StatusCode });
            }
            if (export)
            {
                if (response.Content.Headers.ContentType?.MediaType != "text/csv")
                    throw new HttpRequestException("Unexpected settlement export content type.");
                // Use only validated route values for the attachment; never forward arbitrary upstream headers.
                return File(await response.Content.ReadAsByteArrayAsync(cancellationToken), "text/csv; charset=utf-8",
                    FormattableString.Invariant($"day-settlements-{branchId:D}-{fromDate:yyyy-MM-dd}-{toDate:yyyy-MM-dd}.csv"));
            }
            return Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json", System.Text.Encoding.UTF8);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Settlement reporting BFF dependency unavailable; category {Category}", exception.GetType().Name);
            return StatusCode(503, new ProblemDetails { Title = "Settlement reporting observation is unavailable.", Status = 503 });
        }
    }
}
