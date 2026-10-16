using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Order.Application.Orders;

namespace NexaConnect.Services.Order.Controllers;
[ApiController, Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/order/v1/customer/day-cutoffs")]
public sealed class SourceCutoffsController(OrderCutoffs application, OrderDayFences fences, ILogger<SourceCutoffsController> logger) : ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public Task<IActionResult> Capture(SourceCutoffCommand command, CancellationToken ct) => Execute(async token =>
        Ok(await application.CaptureAsync(command, Request.Headers.Authorization.ToString(), Subject(), token)), ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Read(Guid id, [FromQuery] EndOfDayWindow window, CancellationToken ct) => Execute(async token =>
        await application.ReadAsync(window,id,Request.Headers.Authorization.ToString(),token) is {} value ? Ok(value) : NotFound(), ct);
    [HttpPost("seals"),RequestSizeLimit(4096)]
    public Task<IActionResult> Seal(SourceSealCommand command,CancellationToken ct)=>Execute(async token=>
        Ok(await application.SealAsync(command,Request.Headers.Authorization.ToString(),Subject(),token)),ct);
    [HttpGet("seals/{id:guid}")]
    public Task<IActionResult> ReadSeal(Guid id,[FromQuery] EndOfDayWindow window,CancellationToken ct)=>Execute(async token=>
        await application.ReadSealAsync(window,id,Request.Headers.Authorization.ToString(),token) is {} value?Ok(value):NotFound(),ct);
    [HttpPost("fences"),RequestSizeLimit(4096)]
    public Task<IActionResult> AcquireFence(SourceFenceCommand command,CancellationToken ct)=>Execute(async token=>Ok(await fences.ExecuteAsync(command,Request.Headers.Authorization.ToString(),Subject(),false,token)),ct);
    [HttpPost("fences/cancel"),RequestSizeLimit(4096)]
    public Task<IActionResult> CancelFence(SourceFenceCommand command,CancellationToken ct)=>Execute(async token=>Ok(await fences.ExecuteAsync(command,Request.Headers.Authorization.ToString(),Subject(),true,token)),ct);
    [HttpGet("fences/{operation:guid}")]
    public Task<IActionResult> ReadFence(Guid operation,[FromQuery] EndOfDayWindow window,CancellationToken ct)=>Execute(async token=>await fences.ReadAsync(window,operation,Request.Headers.Authorization.ToString(),token) is {} value?Ok(value):NotFound(),ct);
    private string Subject() => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private async Task<IActionResult> Execute(Func<CancellationToken,Task<IActionResult>> action, CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try { return await action(deadline.Token); }
        catch(ArgumentException) { return BadRequest(); }
        catch(UnauthorizedAccessException) { logger.LogWarning("Day-cutoff source authorization denied"); return Forbid(); }
        catch(Npgsql.PostgresException e)when(e.SqlState=="PDS01"){logger.LogWarning("Source fence is pinned by settlement");return Conflict(new{code="financial_day_barrier"});}
        catch(SnapshotOperationConflictException) { logger.LogWarning("Day-cutoff source operation conflict"); return Conflict(); }
        catch(Exception e) when(!ct.IsCancellationRequested) { logger.LogWarning("Day-cutoff source unavailable; category {Category}",e.GetType().Name); return StatusCode(503); }
    }
}
