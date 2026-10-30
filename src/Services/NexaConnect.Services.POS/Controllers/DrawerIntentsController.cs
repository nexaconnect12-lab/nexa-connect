using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.Drawers;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.Services.POS.Controllers;

public sealed class DrawerIntentOptions {public bool Enabled{get;set;}public bool HistoryEnabled{get;set;}}
[ApiController,Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/drawer-intents")]
public sealed class DrawerIntentsController(DrawerIntents application,IOptions<DrawerIntentOptions> options,ILogger<DrawerIntentsController> logger):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public Task<IActionResult> Reserve(Guid organizationId,DrawerIntentRequestV1 request,CancellationToken ct)=>
        Run(organizationId,async(user,token)=>request.OrganizationId!=organizationId?throw new ArgumentException():await application.ReserveAsync(request,user,token),ct);
    [HttpGet("{operationId:guid}")]
    public Task<IActionResult> Read(Guid organizationId,Guid operationId,CancellationToken ct)=>Run(organizationId,(u,t)=>application.ReadAsync(organizationId,operationId,u,t),ct);
    [HttpPost("{operationId:guid}/outcome"),RequestSizeLimit(4096)]
    public Task<IActionResult> Report(Guid organizationId,Guid operationId,DrawerOutcomeRequestV1 request,CancellationToken ct)=>
        Run(organizationId,(u,t)=>request.OrganizationId!=organizationId?throw new ArgumentException():application.ReportAsync(operationId,request,u,t),ct);
    private async Task<IActionResult> Run(Guid org,Func<PosUserContext,CancellationToken,Task<DrawerIntentV1?>> work,CancellationToken ct)
    {
        var subject=User.FindFirst("sub")?.Value;
        if(subject is null||!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization,out var header)
            ||!header.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(header.Parameter))return Unauthorized();
        if(!options.Value.Enabled)return StatusCode(503,new{code="drawer_authority_disabled"});
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try{if(org==Guid.Empty)throw new ArgumentException();var result=await work(new(subject,header.Parameter),deadline.Token);return result is null?NotFound():Ok(result);}
        catch(ArgumentException){return BadRequest();}
        catch(UnauthorizedAccessException){logger.LogWarning("Drawer intent authorization denied");return Forbid();}
        catch(DrawerConflictException){logger.LogWarning("Drawer intent conflict; no new reservation granted");return Conflict(new{code="drawer_intent_conflict"});}
        catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Drawer intent unavailable; category {Category}",e.GetType().Name);return StatusCode(503,new{code="drawer_intent_unavailable"});}
    }
}
