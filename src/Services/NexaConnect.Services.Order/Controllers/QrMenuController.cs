using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Order.Application.Orders;

namespace NexaConnect.Services.Order.Controllers;

[ApiController, AllowAnonymous, Route("api/order/v1/guest/qr")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QrMenuController(QrMenuService service, QrRequestService requests, IConfiguration configuration, BoundedRequestGate gate, ILogger<QrMenuController> logger) : ControllerBase
{
    [HttpGet("menu")] public Task<IActionResult> Menu(CancellationToken ct) => Run(async token => Ok(await service.MenuAsync(Capability(), token)), ct);
    [HttpPost("quote"), RequestSizeLimit(8192)] public Task<IActionResult> Quote(QrCart cart, CancellationToken ct) => Run(async token => Ok(configuration.GetValue<bool>("QrRequests:Enabled") ? await requests.QuoteAsync(Capability(), cart, token) : await service.QuoteAsync(Capability(), cart, token)), ct);
    [HttpGet("requests")] public Task<IActionResult> Mine(CancellationToken ct) => Requests(async token => Ok(await requests.MineAsync(Capability(), token)), ct);
    [HttpPost("requests"), RequestSizeLimit(1024)] public Task<IActionResult> Submit(QrSubmission input, CancellationToken ct) => Requests(async token => Ok(await requests.SubmitAsync(Capability(), input, Guid.TryParse(HttpContext.TraceIdentifier,out var id)?id:Guid.NewGuid(), token)), ct);
    private Task<IActionResult> Requests(Func<CancellationToken,Task<IActionResult>> action,CancellationToken ct)=>configuration.GetValue<bool>("QrRequests:Enabled")?Run(action,ct):Task.FromResult<IActionResult>(NotFound());
    private string Capability() => Request.Headers["X-Nexa-Guest-Session"].ToString();
    [HttpPost("requests/{id:guid}/confirm-price"),RequestSizeLimit(1024)]public Task<IActionResult> Confirm(Guid id,QrPriceConfirmation input,[FromServices]QrAcceptanceService acceptance,CancellationToken ct)=>!configuration.GetValue<bool>("QrAcceptance:Enabled")?Task.FromResult<IActionResult>(NotFound()):Requests(async token=>Ok(await acceptance.ConfirmAsync(Capability(),id,input,Guid.TryParse(HttpContext.TraceIdentifier,out var c)?c:Guid.NewGuid(),token)),ct);
    private async Task<IActionResult> Run(Func<CancellationToken, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("QrMenu:Enabled")) return NotFound();
        if (!gate.Admit("guest")) { logger.LogWarning("QR menu rate limited"); Response.Headers.RetryAfter = "60"; return StatusCode(429); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try { return await action(deadline.Token); }
        catch (UnauthorizedAccessException) { logger.LogWarning("QR menu capability rejected"); return Unauthorized(); }
        catch (PricingChangedException) { logger.LogWarning("QR cart item unavailable"); return Conflict(new { code = "cart_unavailable" }); }
        catch (NexaConnect.Services.Order.Domain.Qr.GuestRequestConflictException) { logger.LogWarning("QR request changed or pending"); return Conflict(new { code = "qr_request_conflict" }); }
        catch (NexaConnect.Services.Order.Domain.Qr.GuestRequestLimitException) { logger.LogWarning("QR request admission limited"); Response.Headers.RetryAfter="60";return StatusCode(429); }
        catch (ArgumentException) { logger.LogWarning("QR cart or pricing rejected"); return BadRequest(new { code = "invalid_cart_or_policy" }); }
        catch (Exception e) when (e is HttpRequestException or JsonException or OverflowException or Npgsql.NpgsqlException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("QR menu dependency unavailable"); return StatusCode(503); }
    }
}
