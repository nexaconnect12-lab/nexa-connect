using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.POS.Application.Terminals;

namespace NexaConnect.Services.POS.Controllers;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/pos/v1/internal/manual-tender-admission")]
public sealed class ManualTenderAdmissionController(ManualTenderAdmission application,
    ILogger<ManualTenderAdmissionController> logger) : ControllerBase
{
    [HttpPost, RequestSizeLimit(4096)]
    public async Task<IActionResult> Admit(ManualTenderAdmissionRequest request, CancellationToken ct)
    {
        if (!ServiceWorkloadPrincipal.IsClientCredentials(User, "nexaconnect-order-service"))
        { logger.LogWarning("Manual tender admission workload denied"); return Forbid(); }
        try
        {
            var at = await application.AdmitAsync(request, ct);
            if (at is null) { logger.LogWarning("Manual tender admission scope rejected"); return Conflict(); }
            return Ok(new { admittedAtUtc = at.Value });
        }
        catch (ArgumentException) { return BadRequest(); }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Manual tender admission unavailable; category {Category}", exception.GetType().Name); return StatusCode(503); }
    }
}
