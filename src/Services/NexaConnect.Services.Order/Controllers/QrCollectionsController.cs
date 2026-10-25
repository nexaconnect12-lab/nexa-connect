using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.ManualTenders;
using System.Security.Claims;

namespace NexaConnect.Services.Order.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-manager,customer-user,customer-viewer"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/order/v1/organizations/{organization:guid}/branches/{branch:guid}/qr-collections")]
public sealed class QrCollectionsController(IConfiguration configuration, IServiceProvider services,
    ILogger<QrCollectionsController> logger) : ControllerBase
{
    [HttpPost("{id:guid}"), RequestSizeLimit(2048)]
    public Task<IActionResult> Collect(Guid organization, Guid branch, Guid id, QrCollectionCommand input, CancellationToken ct)
        => Run(organization,branch,id,input,false,ct);
    [HttpPost("{id:guid}/preview"), RequestSizeLimit(2048)]
    public Task<IActionResult> Preview(Guid organization, Guid branch, Guid id, QrCollectionCommand input, CancellationToken ct)
        => Run(organization,branch,id,input,true,ct);
    private async Task<IActionResult> Run(Guid organization, Guid branch, Guid id, QrCollectionCommand input, bool preview, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("QrCollection:Enabled")) return NotFound();
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out var tenant) || tenant != organization
            || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect"
            || string.IsNullOrWhiteSpace(User.FindFirstValue("sub")))
        { logger.LogWarning("QR collection tenant authorization denied"); return Forbid(); }
        try
        {
            var result = await services.GetRequiredService<QrCollectionService>().CollectAsync(organization, branch, id, input,
                User.FindFirstValue("sub")!, Request.Headers.Authorization.ToString(),
                Guid.TryParse(HttpContext.TraceIdentifier, out var correlation) ? correlation : Guid.NewGuid(), ct, preview);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException) { logger.LogWarning("QR collection branch authorization denied"); return Forbid(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (Exception e) when (e is InvalidOperationException or ManualTenderAdmissionConflictException)
        { logger.LogWarning("QR collection scope or state conflict"); return Conflict(new { code = "collection_conflict" }); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("QR collection unavailable; category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
