using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Controllers;

[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/day-close-settlements")]
public sealed class DaySettlementsController(DaySettlement application,ILogger<DaySettlementsController> logger):ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Read(Guid organizationId,[FromQuery] Guid branchId,[FromQuery] DateOnly businessDate,CancellationToken ct)=>
        Execute(user=>application.ReadAsync(organizationId,branchId,businessDate,user,ct),ct);
    [HttpPost,RequestSizeLimit(4096)]
    public Task<IActionResult> Finalize(Guid organizationId,SettlementCommand command,CancellationToken ct)=>
        Execute(user=>application.FinalizeAsync(organizationId,command,user,Guid.NewGuid(),ct),ct);
    private async Task<IActionResult> Execute(Func<PosUserContext,Task<SettlementView>> action,CancellationToken ct)
    {
        var subject=User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(subject is null || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization,out var header)
            || !header.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))return Unauthorized();
        try{return Ok(await action(new(subject,header.Parameter)));}
        catch(ArgumentException){return BadRequest();}
        catch(UnauthorizedAccessException){logger.LogWarning("Settlement authorization denied");return Forbid();}
        catch(DayCloseConflictException e){logger.LogWarning("Settlement conflict");return Conflict(new{code=e.Message});}
        catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Settlement unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
