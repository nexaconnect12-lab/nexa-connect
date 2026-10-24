using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.Services.POS.Controllers;
[ApiController, Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/day-cutoffs")]
public sealed class SourceCutoffsController(PosCutoffs application, PosDayFences fences, ILogger<SourceCutoffsController> logger) : ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public Task<IActionResult> Capture(SourceCutoffCommand command, CancellationToken ct) => Execute(async token =>
        Ok(await application.CaptureAsync(command, UserContext(), Subject(), token)), ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Read(Guid id, [FromQuery] EndOfDayWindow window, CancellationToken ct) => Execute(async token =>
        await application.ReadAsync(window,id,UserContext(),token) is {} value ? Ok(value) : NotFound(), ct);
    [HttpPost("seals"),RequestSizeLimit(4096)]
    public Task<IActionResult> Seal(SourceSealCommand command,CancellationToken ct)=>Execute(async token=>
        Ok(await application.SealAsync(command,UserContext(),Subject(),token)),ct);
    [HttpGet("seals/{id:guid}")]
    public Task<IActionResult> ReadSeal(Guid id,[FromQuery] EndOfDayWindow window,CancellationToken ct)=>Execute(async token=>
        await application.ReadSealAsync(window,id,UserContext(),token) is {} value?Ok(value):NotFound(),ct);
    [HttpPost("fences"),RequestSizeLimit(4096)]
    public Task<IActionResult> AcquireFence(SourceFenceCommand command,CancellationToken ct)=>Execute(async token=>Ok(await fences.ExecuteAsync(command,UserContext(),Subject(),false,token)),ct);
    [HttpPost("fences/cancel"),RequestSizeLimit(4096)]
    public Task<IActionResult> CancelFence(SourceFenceCommand command,CancellationToken ct)=>Execute(async token=>Ok(await fences.ExecuteAsync(command,UserContext(),Subject(),true,token)),ct);
    [HttpGet("fences/{operation:guid}")]
    public Task<IActionResult> ReadFence(Guid operation,[FromQuery] EndOfDayWindow window,CancellationToken ct)=>Execute(async token=>await fences.ReadAsync(window,operation,UserContext(),token) is {} value?Ok(value):NotFound(),ct);
    private string Subject() => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private PosUserContext UserContext()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) || !header.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter)) throw new UnauthorizedAccessException();
        return new(Subject(),header.Parameter);
    }
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
