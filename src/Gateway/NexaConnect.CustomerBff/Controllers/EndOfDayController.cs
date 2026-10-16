using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.CustomerBff.Application.Reporting;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController, Authorize(Policy = "CustomerSession"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("bff/customer/reports/end-of-day")]
public sealed class EndOfDayController(TenantSelectionCookie cookie, BffAccessTokenService tokens,
    IConfiguration configuration, CustomerEndOfDay completeness, ILogger<EndOfDayController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid branchId, [FromQuery] DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var tenant = cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if (tenant is null || tenant.SubjectId != User.FindFirstValue("sub"))
        {
            logger.LogWarning("End-of-day draft BFF tenant session rejected");
            return Unauthorized();
        }
        try
        {
            var settings = configuration.GetRequiredSection("Bff");
            string? token = await tokens.GetValidAccessTokenAsync(HttpContext, "CustomerCookie",
                settings["Authority"]!, settings["ClientId"]!, settings["ClientSecret"]!, cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
            using var response = await completeness.ReadAsync(tenant, token, new(branchId, businessDate), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("End-of-day draft BFF boundary rejected with status {StatusCode}", (int)response.StatusCode);
                return StatusCode((int)response.StatusCode, new ProblemDetails { Title = "End-of-day draft observation is unavailable.", Status = (int)response.StatusCode });
            }
            return Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json", System.Text.Encoding.UTF8);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("End-of-day draft BFF dependency unavailable; category {Category}", exception.GetType().Name);
            return StatusCode(503, new ProblemDetails { Title = "End-of-day draft observation is unavailable.", Status = 503 });
        }
    }
}

