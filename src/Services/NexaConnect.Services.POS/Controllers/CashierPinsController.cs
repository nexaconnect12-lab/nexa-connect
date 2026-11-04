using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.CashierPins;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Controllers;

[ApiController, Authorize, EnableRateLimiting("cashier-pins"), RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/cashier-pin")]
public sealed class CashierPinsController(CashierPins application, ILogger<CashierPinsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Status(Guid organizationId, [FromQuery] Guid branchId, [FromQuery] Guid storeId, [FromQuery] Guid terminalId, CancellationToken ct)
        => Execute(async (user, token) => Ok(await application.StatusAsync(organizationId, new(branchId, storeId, terminalId), user, token)), ct);
    [HttpPut]
    public Task<IActionResult> Change(Guid organizationId, CashierPinChangeV1 command, CancellationToken ct)
        => Execute(async (user, token) => Ok(await application.ChangeAsync(organizationId, command, user, token)), ct);
    [HttpPost("verify")]
    public Task<IActionResult> Verify(Guid organizationId, CashierPinVerifyV1 command, CancellationToken ct)
        => Execute(async (user, token) => Ok(await application.VerifyAsync(organizationId, command, user, token)), ct);
    [HttpPost("staff")]
    public Task<IActionResult> Administer(Guid organizationId, CashierPinAdminV1 command, CancellationToken ct)
        => Execute(async (user, token) => Ok(await application.AdministerAsync(organizationId, command, user, token)), ct);
    [HttpPost("staff/status")]
    public Task<IActionResult> StaffStatus(Guid organizationId, CashierPinStaffV1 command, CancellationToken ct)
        => Execute(async (user, token) => Ok(await application.StaffStatusAsync(organizationId, command, user, token)), ct);
    private async Task<IActionResult> Execute(Func<PosUserContext, CancellationToken, Task<IActionResult>> action, CancellationToken ct)
    {
        string? subject = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var bearer)
            || !bearer.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(bearer.Parameter)) return Unauthorized();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var result = await action(new(subject, bearer.Parameter), deadline.Token);
            if (result is OkObjectResult { Value: CashierPinResultV1 { Accepted: false } }) logger.LogWarning("Cashier PIN boundary {Category}", "denied-or-conflict");
            return result;
        }
        catch (ArgumentException) { return BadRequest(new { code = "cashier_pin_invalid" }); }
        catch (UnauthorizedAccessException) { logger.LogWarning("Cashier PIN boundary {Category}", "authorization-denied"); return Forbid(); }
        catch (Exception) when (!ct.IsCancellationRequested) { logger.LogWarning("Cashier PIN boundary {Category}", "unavailable"); return StatusCode(503, new { code = "cashier_pin_unavailable" }); }
    }
}
