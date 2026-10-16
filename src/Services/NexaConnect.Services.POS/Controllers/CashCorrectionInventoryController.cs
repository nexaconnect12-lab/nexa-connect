using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/cash-correction-manifest")]
public sealed class CashCorrectionInventoryController(CashCorrectionInventory inventory, ILogger<CashCorrectionInventoryController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid organizationId, [FromQuery] Guid branchId,
        [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc, CancellationToken ct)
    {
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (subject is null || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var auth)
            || !auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(auth.Parameter)) return Unauthorized();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { return Ok(await inventory.ReadAsync(organizationId, branchId, fromUtc, toUtc, new PosUserContext(subject, auth.Parameter), deadline.Token)); }
        catch (ArgumentException) { return BadRequest(); }
        catch (UnauthorizedAccessException) { logger.LogWarning("Cash correction inventory authorization denied"); return Forbid(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Cash correction inventory unavailable; category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
