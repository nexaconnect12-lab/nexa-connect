using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.ManualRefunds;
using NexaConnect.Services.POS.Application.Shifts;
using Npgsql;

namespace NexaConnect.Services.POS.Controllers;

[ApiController, Authorize(Roles = "customer-owner,customer-admin,customer-manager,customer-viewer"), RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/branches/{branchId:guid}/orders/{orderId:guid}/manual-refunds")]
public sealed class ManualRefundsController(ManualRefunds application, IConfiguration configuration,
    ILogger<ManualRefundsController> logger) : ControllerBase
{
    [HttpGet("/api/pos/v1/customer/organizations/{organizationId:guid}/branches/{branchId:guid}/manual-refunds/assigned")]
    public Task<IActionResult> Assigned(Guid organizationId, Guid branchId, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.AssignedAsync(organizationId, branchId, user, token), newWork: false, ct);

    [HttpPost("/api/pos/v1/customer/organizations/{organizationId:guid}/branches/{branchId:guid}/manual-refunds/available")]
    public Task<IActionResult> Register(Guid organizationId, Guid branchId, ManualRefundAvailabilityV1 request, CancellationToken ct) =>
        Execute<ManualRefundAvailabilityResultV1>(organizationId, async (user, token) =>
            await application.RegisterAsync(organizationId, branchId, request, user, token) ? new(true) : null, newWork: true, ct);

    [HttpGet("cashiers")]
    public Task<IActionResult> Cashiers(Guid organizationId, Guid branchId, Guid orderId, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.CashiersAsync(organizationId, branchId, orderId, user, token), newWork: true, ct);

    [HttpGet("managed")]
    public Task<IActionResult> Managed(Guid organizationId, Guid branchId, Guid orderId, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.ManagedAsync(organizationId, branchId, orderId, user, token), newWork: false, ct);

    [HttpGet("preview")]
    public Task<IActionResult> Preview(Guid organizationId, Guid branchId, Guid orderId, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.PreviewAsync(organizationId, branchId, orderId, user, token), newWork: true, ct);

    [HttpPost("approve")]
    public Task<IActionResult> Approve(Guid organizationId, Guid branchId, Guid orderId, ApproveManualRefundV1 request, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.ApproveAsync(organizationId, branchId, orderId, request, user, token), newWork: true, ct);

    [HttpGet("{operationId:guid}")]
    public Task<IActionResult> Read(Guid organizationId, Guid branchId, Guid orderId, Guid operationId, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.ReadAsync(organizationId, branchId, orderId, operationId, user, token), newWork: false, ct);

    [HttpPost("{operationId:guid}/release")]
    public Task<IActionResult> Release(Guid organizationId, Guid branchId, Guid orderId, Guid operationId, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.ReleaseAsync(organizationId, branchId, orderId, operationId, user, token), newWork: false, ct);

    [HttpPost("{operationId:guid}/start")]
    public Task<IActionResult> Start(Guid organizationId, Guid branchId, Guid orderId, Guid operationId, ManualRefundCustodyV1 request, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.PayoutAsync(organizationId, branchId, orderId, operationId, request, null, user, token), newWork: true, ct);

    [HttpPost("{operationId:guid}/complete")]
    public Task<IActionResult> Complete(Guid organizationId, Guid branchId, Guid orderId, Guid operationId, CompleteManualRefundV1 request, CancellationToken ct) =>
        Execute(organizationId, (user, token) => application.PayoutAsync(organizationId, branchId, orderId, operationId, request.Custody, request, user, token), newWork: false, ct);

    private async Task<IActionResult> Execute<T>(Guid organizationId, Func<PosUserContext, CancellationToken, Task<T?>> action,
        bool newWork, CancellationToken ct) where T : class
    {
        if (newWork && !configuration.GetValue<bool>("ManualRefunds:Enabled")) return NotFound();
        if (!Guid.TryParse(Request.Headers[TenantContextHeaders.OrganizationId], out Guid organization)
            || organization != organizationId || Request.Headers[TenantContextHeaders.ApplicationCode] != "nexa_connect") return NotFound();
        string? subject = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 200 || subject.Any(char.IsControl)
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var bearer)
            || !bearer.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(bearer.Parameter)) return Unauthorized();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var result = await action(new(subject, bearer.Parameter), deadline.Token);
            if (result is not null) return Ok(result);
            logger.LogWarning("Manual refund boundary missing or denied");
            return NotFound();
        }
        catch (ArgumentException) { return BadRequest(new { code = "manual_refund_invalid" }); }
        catch (UnauthorizedAccessException) { logger.LogWarning("Manual refund boundary denied"); return NotFound(); }
        catch (InvalidOperationException) { logger.LogWarning("Manual refund state conflict"); return Conflict(new { code = "manual_refund_conflict" }); }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation or "PDS01" || e.SqlState == "P0001" && e.MessageText == "financial_day_fenced")
        { logger.LogWarning("Manual refund database conflict"); return Conflict(new { code = "manual_refund_conflict" }); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Manual refund operation unavailable"); return StatusCode(503, new { code = "manual_refund_unavailable" }); }
    }
}
