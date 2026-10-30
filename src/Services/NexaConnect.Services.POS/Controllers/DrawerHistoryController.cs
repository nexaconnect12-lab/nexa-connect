using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexaConnect.Services.POS.Application.Drawers;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.Services.POS.Controllers;

[ApiController,Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/pos/v1/customer/organizations/{organizationId:guid}/drawer-intents")]
public sealed class DrawerHistoryController(DrawerHistoryReader application,IOptions<DrawerIntentOptions> options,ILogger<DrawerHistoryController> logger):ControllerBase
{
    [HttpGet("{operationId:guid}/review")]
    public Task<IActionResult> Review(Guid organizationId,Guid operationId,[FromQuery]Guid restaurantId,[FromQuery]Guid branchId,[FromQuery]Guid storeId,[FromQuery]Guid terminalId,CancellationToken ct)=>
        Execute(async(user,token)=>{var item=await application.ReviewAsync(new(organizationId,restaurantId,branchId,storeId,terminalId),operationId,user,token);return item is null?NotFound():Ok(item);},ct);
    [HttpGet]
    public async Task<IActionResult> Read(Guid organizationId,[FromQuery]Guid restaurantId,[FromQuery]Guid branchId,[FromQuery]Guid storeId,[FromQuery]Guid terminalId,
        [FromQuery]DateTimeOffset fromUtc,[FromQuery]DateTimeOffset toUtc,[FromQuery]int limit=25,[FromQuery]string? cursor=null,CancellationToken ct=default)
        =>await Execute(async(user,token)=>Ok(await application.ReadAsync(new(organizationId,restaurantId,branchId,storeId,terminalId),fromUtc,toUtc,limit,cursor,user,token)),ct);
    private async Task<IActionResult> Execute(Func<PosUserContext,CancellationToken,Task<IActionResult>> action,CancellationToken ct)
    {
        var subject=User.FindFirst("sub")?.Value;
        if(subject is null||!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization,out var bearer)||!bearer.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(bearer.Parameter))return Unauthorized();
        if(!options.Value.Enabled&&!options.Value.HistoryEnabled)return StatusCode(503,new{code="drawer_authority_disabled"});
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try{return await action(new(subject,bearer.Parameter),deadline.Token);}
        catch(ArgumentException){return BadRequest();}
        catch(UnauthorizedAccessException){logger.LogWarning("Drawer history authorization denied");return Forbid();}
        catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Drawer history unavailable; category {Category}",e.GetType().Name);return StatusCode(503,new{code="drawer_history_unavailable"});}
    }
}
