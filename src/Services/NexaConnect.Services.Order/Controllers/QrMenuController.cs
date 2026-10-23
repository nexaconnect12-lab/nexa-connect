using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Order.Application.Orders;

namespace NexaConnect.Services.Order.Controllers;

[ApiController, AllowAnonymous, Route("api/order/v1/guest/qr")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QrMenuController(QrMenuService service, IConfiguration configuration, BoundedRequestGate gate, ILogger<QrMenuController> logger) : ControllerBase
{
    [HttpGet("menu")] public Task<IActionResult> Menu(CancellationToken ct) => Run(async token => Ok(await service.MenuAsync(Capability(), token)), ct);
    [HttpPost("quote"), RequestSizeLimit(8192)] public Task<IActionResult> Quote(QrCart cart, CancellationToken ct) => Run(async token => Ok(await service.QuoteAsync(Capability(), cart, token)), ct);
    private string Capability() => Request.Headers["X-Nexa-Guest-Session"].ToString();
    private async Task<IActionResult> Run(Func<CancellationToken, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("QrMenu:Enabled")) return NotFound();
        if (!gate.Admit("guest")) { logger.LogWarning("QR menu rate limited"); Response.Headers.RetryAfter = "60"; return StatusCode(429); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try { return await action(deadline.Token); }
        catch (UnauthorizedAccessException) { logger.LogWarning("QR menu capability rejected"); return Unauthorized(); }
        catch (PricingChangedException) { logger.LogWarning("QR cart item unavailable"); return Conflict(new { code = "cart_unavailable" }); }
        catch (ArgumentException) { logger.LogWarning("QR cart or pricing rejected"); return BadRequest(new { code = "invalid_cart_or_policy" }); }
        catch (Exception e) when (e is HttpRequestException or JsonException or OverflowException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("QR menu dependency unavailable"); return StatusCode(503); }
    }
}
