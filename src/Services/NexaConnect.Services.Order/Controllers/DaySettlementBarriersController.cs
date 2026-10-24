using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Order.Application.Orders;
namespace NexaConnect.Services.Order.Controllers;

[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/order/v1/internal/day-settlement-barriers")]
public sealed class DaySettlementBarriersController(OrderDayBarriers application, ILogger<DaySettlementBarriersController> logger) : ControllerBase
{
    [HttpPost, RequestSizeLimit(8192)]
    public Task<IActionResult> Execute(SourceBarrierRequest request, CancellationToken ct) => Run(async token => Ok(await application.ExecuteAsync(request, token)), ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Read(Guid id, [FromQuery] EndOfDayWindow window, CancellationToken ct) => Run(async token =>
        await application.ReadAsync(window, id, token) is {} proof ? Ok(proof) : NotFound(), ct);

    private async Task<IActionResult> Run(Func<CancellationToken, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!ServiceWorkloadPrincipal.IsClientCredentials(User, "nexaconnect-pos-service"))
        { logger.LogWarning("Settlement barrier workload authorization denied"); return Forbid(); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try { return await action(deadline.Token); }
        catch (ArgumentException) { return BadRequest(); }
        catch (SnapshotOperationConflictException) { logger.LogWarning("Settlement barrier conflict"); return Conflict(); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Settlement barrier unavailable; category {Category}", e.GetType().Name); return StatusCode(503); }
    }
}
