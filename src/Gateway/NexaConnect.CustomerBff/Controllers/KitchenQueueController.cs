using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Kitchen;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.CustomerBff.Controllers;

[ApiController, Authorize(Policy = "CustomerSession")]
[Route("bff/customer/kitchen")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KitchenQueueController(TenantSelectionCookie cookie, BffAccessTokenService tokens,
    IConfiguration configuration, CustomerKitchenService kitchen, ILogger<KitchenQueueController> logger) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery) => Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpGet("branches/{branchId:guid}/tickets")]
    public Task<IActionResult> Queue(Guid branchId, [FromQuery] string? station, [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Forward(KitchenOperation.Queue, new(branchId, Station: station, Limit: limit, Cursor: cursor), ct);

    [HttpGet("branches/{branchId:guid}/tickets/{ticketId:guid}")]
    public Task<IActionResult> Detail(Guid branchId, Guid ticketId, CancellationToken ct) =>
        Forward(KitchenOperation.Detail, new(branchId, ticketId), ct);

    [HttpPost("branches/{branchId:guid}/tickets/{ticketId:guid}/transitions"), ValidateAntiForgeryToken]
    [RequestSizeLimit(4096)]
    public Task<IActionResult> Transition(Guid branchId, Guid ticketId, KitchenTransitionRequest request, CancellationToken ct) =>
        Forward(KitchenOperation.Transition, new(branchId, ticketId, Transition: request), ct);

    private async Task<IActionResult> Forward(KitchenOperation operation, KitchenRequest request, CancellationToken ct)
    {
        if (request.BranchId == Guid.Empty || request.Limit is < 1 or > 100 || request.Station is { Length: > 100 } || request.Cursor is { Length: > 64 })
            return BadRequest();
        TenantContext? tenant = cookie.Unprotect(Request.Cookies["__Host-nexa-customer-tenant"]);
        if (tenant is null || tenant.SubjectId != User.FindFirstValue("sub"))
        {
            logger.LogWarning("Kitchen BFF tenant session rejected");
            return Unauthorized();
        }
        var settings = configuration.GetRequiredSection("Bff");
        string? token = await tokens.GetValidAccessTokenAsync(HttpContext, "CustomerCookie", settings["Authority"]!, settings["ClientId"]!, settings["ClientSecret"]!, ct);
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        try
        {
            using var response = await kitchen.ExecuteAsync(tenant, token, operation, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Kitchen BFF operation {Operation} returned status {StatusCode}", operation, (int)response.StatusCode);
                return StatusCode((int)response.StatusCode, new { title = "Kitchen request could not be completed." });
            }
            return Content(await response.Content.ReadAsStringAsync(ct), "application/json", System.Text.Encoding.UTF8);
        }
        catch (HttpRequestException) { logger.LogWarning("Kitchen BFF dependency unavailable"); return StatusCode(503); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { logger.LogWarning("Kitchen BFF dependency timed out"); return StatusCode(503); }
    }
}
