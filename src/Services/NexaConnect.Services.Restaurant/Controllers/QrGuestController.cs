using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Restaurant.Application.Qr;
using NexaConnect.Services.Restaurant.Domain.Qr;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController, AllowAnonymous, Route("api/restaurant/v1/guest/qr")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QrGuestController(QrEntryService service, IConfiguration configuration, BoundedRequestGate gate, ILogger<QrGuestController> logger) : ControllerBase
{
    [HttpPost("sessions"), RequestSizeLimit(1024)]
    public Task<IActionResult> Start(QrEntryExchange input, CancellationToken ct) => Run(async () =>
    {
        var result = await service.StartAsync(input.EntryToken, ct);
        return result is null ? Unauthorized() : Ok(result);
    });
    [HttpGet("context")]
    public Task<IActionResult> Context(CancellationToken ct) => Run(async () =>
    {
        var result = await service.ContextAsync(Request.Headers["X-Nexa-Guest-Session"].ToString(), ct);
        return result is null ? Unauthorized() : Ok(result);
    });
    [HttpDelete("sessions")]
    public Task<IActionResult> End(CancellationToken ct) => Run(async () =>
    { await service.EndAsync(Request.Headers["X-Nexa-Guest-Session"].ToString(), ct); return NoContent(); });
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        if (!configuration.GetValue<bool>("QrEntry:Enabled")) return NotFound();
        if (!gate.Admit("guest")) { logger.LogWarning("QR guest transport admission limited"); Response.Headers.RetryAfter = "60"; return StatusCode(429); }
        try
        {
            var result = await action();
            if (result is UnauthorizedResult) logger.LogWarning("QR guest capability rejected");
            return result;
        }
        catch (QrLimitException) { logger.LogWarning("QR guest admission capacity reached"); Response.Headers.RetryAfter = "60"; return StatusCode(429); }
        catch (Exception e) when (e is Npgsql.NpgsqlException || e is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { logger.LogWarning("QR guest persistence unavailable"); return StatusCode(503); }
    }
}
