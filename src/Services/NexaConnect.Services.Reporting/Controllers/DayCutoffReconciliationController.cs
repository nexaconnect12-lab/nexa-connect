using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;
namespace NexaConnect.Services.Reporting.Controllers;
[ApiController,Authorize(Roles="customer-owner,customer-admin,customer-manager,customer-viewer")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/reporting/v1/customer/day-cutoff-reconciliation")]
public sealed class DayCutoffReconciliationController(DayCutoffReconciliation application,ILogger<DayCutoffReconciliationController> logger):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Check(CutoffReconciliationCommand command,CancellationToken ct)
    {
        try{return Ok(await application.CheckAsync(command,Request.Headers.Authorization.ToString(),ct));}
        catch(ArgumentException){return BadRequest();}
        catch(UnauthorizedAccessException){logger.LogWarning("Day-cutoff reconciliation authorization denied");return Forbid();}
        catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Day-cutoff reconciliation unavailable; category {Category}",e.GetType().Name);return StatusCode(503);}
    }
}
