using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NexaConnect.Observability;
using NexaConnect.Services.Payment.Application.Webhooks;
using NexaConnect.Services.Payment.Infrastructure.Webhooks;

namespace NexaConnect.Services.Payment.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payment/v1/webhooks/omise")]
public sealed class OmiseWebhooksController(IOptions<OmiseWebhookOptions> options,
    ILogger<OmiseWebhooksController> logger, OmiseWebhookIngress? ingress = null) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(65536)]
    [EnableRateLimiting("omise-webhook")]
    public async Task<IActionResult> Receive(CancellationToken token)
    {
        if (!options.Value.Enabled) return NotFound();
        if (ingress is null) return StatusCode(503);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        byte[] bytes = new byte[65537]; int count = 0, read;
        try { while (count < bytes.Length && (read = await Request.Body.ReadAsync(bytes.AsMemory(count), deadline.Token)) > 0) count += read; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return StatusCode(408); }
        if (count > 65536) return StatusCode(413);
        if (!OmiseWebhookSignature.Verify(bytes.AsSpan(0, count), Request.Headers["Omise-Signature-Timestamp"].ToString(),
            Request.Headers["Omise-Signature"].ToString(), options.Value.Secret!, DateTimeOffset.UtcNow))
        {
            logger.LogWarning("Omise webhook rejected at signature boundary");
            return Unauthorized();
        }
        string id;
        try { id = OmiseWebhookIdentity.Parse(bytes.AsMemory(0, count)); }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException) { return BadRequest(); }
        Guid correlation = Guid.TryParse(CorrelationContext.Current, out Guid parsed) && parsed != Guid.Empty ? parsed : Guid.NewGuid();
        try { await ingress.ReceiveAsync(id, correlation, CorrelationContext.Current, token); }
        catch (Npgsql.NpgsqlException)
        {
            logger.LogWarning("Omise webhook persistence unavailable; delivery not acknowledged");
            return StatusCode(503);
        }
        logger.LogInformation("Omise webhook durably received");
        return Ok();
    }
}
