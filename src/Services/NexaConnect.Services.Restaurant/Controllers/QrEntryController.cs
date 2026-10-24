using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Restaurant.Application.Qr;
using NexaConnect.Services.Restaurant.Domain.Qr;

namespace NexaConnect.Services.Restaurant.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-user,customer-manager")]
[Route("api/restaurant/v1/customer/organizations/{org:guid}/branches/{branch:guid}/qr-entries")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QrEntryController(QrEntryService service, IConfiguration configuration, ILogger<QrEntryController> logger) : ControllerBase
{
    [HttpGet] public Task<IActionResult> List(Guid org, Guid branch, CancellationToken ct) => Run(async () => await service.ListAsync(org, branch, Request.Headers.Authorization.ToString(), ct));
    [HttpPost, RequestSizeLimit(1024)] public Task<IActionResult> Create(Guid org, Guid branch, CreateQrEntry input, CancellationToken ct) =>
        Run(async () => await service.CreateAsync(org, branch, input.TableCode, User.FindFirstValue("sub") ?? "", Request.Headers.Authorization.ToString(), ct));
    [HttpPost("{id:guid}/rotate"), RequestSizeLimit(1024)] public Task<IActionResult> Rotate(Guid org, Guid branch, Guid id, ChangeQrEntry input, CancellationToken ct) => Change(org, branch, id, input, false, ct);
    [HttpPost("{id:guid}/revoke"), RequestSizeLimit(1024)] public Task<IActionResult> Revoke(Guid org, Guid branch, Guid id, ChangeQrEntry input, CancellationToken ct) => Change(org, branch, id, input, true, ct);
    private Task<IActionResult> Change(Guid org, Guid branch, Guid id, ChangeQrEntry input, bool revoke, CancellationToken ct) => Run(async () =>
    {
        var issue = await service.ChangeAsync(org, branch, id, input.ExpectedVersion, revoke, User.FindFirstValue("sub") ?? "", Request.Headers.Authorization.ToString(), ct);
        return revoke ? (object)issue.Entry : issue;
    });
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        if (!configuration.GetValue<bool>("QrEntry:Enabled")) return NotFound();
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { logger.LogWarning("QR administration authorization denied"); return Forbid(); }
        catch (ArgumentException) { logger.LogWarning("QR administration input rejected"); return BadRequest(new { code = "invalid_qr_input" }); }
        catch (QrConflictException) { logger.LogInformation("QR administration entry changed or unavailable"); return Conflict(new { code = "qr_changed", title = "Reload the table QR list before changing it." }); }
        catch (QrLimitException) { logger.LogWarning("QR administration entry capacity reached"); return StatusCode(429); }
        catch (Exception e) when (e is HttpRequestException or Npgsql.NpgsqlException || e is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        { logger.LogWarning("QR administration dependency unavailable"); return StatusCode(503); }
    }
}
